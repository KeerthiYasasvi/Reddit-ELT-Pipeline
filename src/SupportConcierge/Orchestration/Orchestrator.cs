using SupportConcierge.Agents;
using SupportConcierge.GitHub;
using SupportConcierge.Parsing;
using SupportConcierge.Reporting;
using SupportConcierge.Scoring;
using SupportConcierge.SpecPack;
using System.Text.Json;

namespace SupportConcierge.Orchestration;

public class Orchestrator
{
    private const int MaxLoops = 3;

    public async Task ProcessEventAsync(string? eventName, JsonElement eventPayload)
    {
        Console.WriteLine($"Processing event: {eventName}");

        // Only process relevant events
        if (eventName != "issues" && eventName != "issue_comment")
        {
            Console.WriteLine("Skipping: Not an issue or comment event");
            return;
        }

        // Extract issue and repository info
        var issue = eventPayload.GetProperty("issue").Deserialize<GitHubIssue>();
        var repository = eventPayload.GetProperty("repository").Deserialize<GitHubRepository>();

        if (issue == null || repository == null)
        {
            Console.WriteLine("ERROR: Could not parse issue or repository from event");
            return;
        }

        // SCENARIO 1 FIX: For issue_comment events, only process if comment is from issue author
        if (eventName == "issue_comment")
        {
            var comment = eventPayload.GetProperty("comment").Deserialize<GitHubComment>();
            if (comment == null)
            {
                Console.WriteLine("ERROR: Could not parse comment from issue_comment event");
                return;
            }

            var commentAuthor = issue.User.Login;
            if (!comment.User.Login.Equals(commentAuthor, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Skipping: Comment from {comment.User.Login} (not from issue author {commentAuthor})");
                return;
            }
        }

        Console.WriteLine($"Issue #{issue.Number}: {issue.Title}");
        Console.WriteLine($"Repository: {repository.FullName}");

        // Initialize services
        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")!;
        var botUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_BOT_USERNAME") ?? "github-actions[bot]";
        
        var githubApi = new GitHubApi(githubToken);
        var specPackLoader = new SpecPackLoader();
        var openAiClient = new OpenAiClient();
        var stateStore = new StateStore();
        var parser = new IssueFormParser();
        var commentComposer = new CommentComposer();

        // Load configuration
        Console.WriteLine("Loading SpecPack configuration...");
        var specPack = await specPackLoader.LoadSpecPackAsync();

        // Get all comments
        var comments = await githubApi.GetIssueCommentsAsync(
            repository.Owner.Login, repository.Name, issue.Number);

        // Find latest bot state from previous comments
        BotState? currentState = null;
        foreach (var comment in comments.OrderByDescending(c => c.CreatedAt))
        {
            if (comment.User.Login.Equals(botUsername, StringComparison.OrdinalIgnoreCase))
            {
                currentState = stateStore.ExtractState(comment.Body);
                if (currentState != null)
                {
                    Console.WriteLine($"Found existing state: Loop {currentState.LoopCount}, Category: {currentState.Category}");
                    break;
                }
            }
        }

        // Initialize validators and scorers early (needed for Scenario 7)
        var validators = new Validators(specPack.Validators);
        var secretRedactor = new SecretRedactor(specPack.Validators.SecretPatterns);

        // SCENARIO 1 FIX: Check if issue is already finalized
        if (currentState != null && currentState.IsFinalized)
        {
            // SCENARIO 7: Check for disagreement in new comment
            if (eventName == "issue_comment" && currentState.BriefIterationCount < 2)
            {
                var incomingComment = eventPayload.GetProperty("comment").Deserialize<GitHubComment>();
                if (incomingComment != null && DetectDisagreement(incomingComment.Body))
                {
                    Console.WriteLine($"Disagreement detected from {incomingComment.User.Login}. Regenerating brief...");
                    await HandleBriefDisagreementAsync(
                        issue, repository, incomingComment.Body, currentState,
                        specPack, githubApi, openAiClient, commentComposer, 
                        secretRedactor, stateStore, comments);
                    return;
                }
            }
            
            Console.WriteLine($"Issue already finalized at {currentState.FinalizedAt}. Skipping processing to prevent duplicate responses.");
            Console.WriteLine("Note: If this is a new issue from the same author, they should open a new issue.");
            return;
        }

        // SCENARIO 1 FIX: Only process comments from the issue author
        // Filter comments to only include issue author's responses (for field extraction)
        var issueAuthor = issue.User.Login;
        var authorComments = comments
            .Where(c => c.User.Login.Equals(issueAuthor, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        Console.WriteLine($"Issue author: {issueAuthor}");
        Console.WriteLine($"Total comments: {comments.Count}, Author comments: {authorComments.Count}");

        // Scorer already initialized above
        var scorer = new CompletenessScorer(validators);

        // Step 1: Determine category
        string category;
        if (currentState != null)
        {
            category = currentState.Category;
            Console.WriteLine($"Using existing category: {category}");
        }
        else
        {
            category = await DetermineCategoryAsync(issue, specPack, openAiClient, parser);
            Console.WriteLine($"Determined category: {category}");
        }

        // Get checklist for this category
        if (!specPack.Checklists.TryGetValue(category, out var checklist))
        {
            Console.WriteLine($"ERROR: No checklist found for category '{category}'");
            return;
        }

        // Step 2: Extract fields (using only author comments + original issue)
        Console.WriteLine("Extracting fields from issue...");
        var extractedFields = await ExtractFieldsAsync(
            issue, authorComments, checklist, parser, openAiClient, secretRedactor);

        Console.WriteLine($"Extracted {extractedFields.Count} fields");

        // Step 3: Score completeness
        var scoring = scorer.ScoreCompleteness(extractedFields, checklist);
        Console.WriteLine($"Completeness score: {scoring.Score}/{scoring.Threshold}");
        Console.WriteLine($"Missing fields: {string.Join(", ", scoring.MissingFields)}");

        // Initialize or update state
        if (currentState == null)
        {
            currentState = stateStore.CreateInitialState(category, issueAuthor);
        }

        // Step 4: Decide action based on completeness and loop count
        if (scoring.IsActionable)
        {
            // Issue is actionable - create engineer brief and route
            Console.WriteLine("Issue is actionable. Creating engineer brief...");
            await FinalizeIssueAsync(
                issue, repository, extractedFields, scoring, category,
                specPack, githubApi, openAiClient, commentComposer, 
                secretRedactor, stateStore, currentState);
        }
        else if (currentState.LoopCount >= MaxLoops)
        {
            // Max loops reached - escalate
            Console.WriteLine($"Max loops ({MaxLoops}) reached without becoming actionable. Escalating...");
            await EscalateIssueAsync(
                issue, repository, scoring, specPack, 
                githubApi, commentComposer, stateStore, currentState);
        }
        else
        {
            // Ask follow-up questions
            Console.WriteLine("Asking follow-up questions...");
            await AskFollowUpQuestionsAsync(
                issue, repository, extractedFields, scoring, category,
                currentState, githubApi, openAiClient, commentComposer, stateStore);
        }

        Console.WriteLine("Processing complete.");
    }

    private async Task<string> DetermineCategoryAsync(
        GitHubIssue issue,
        SpecModels.SpecPackConfig specPack,
        OpenAiClient openAiClient,
        IssueFormParser parser)
    {
        // Try deterministic methods first
        var issueBody = issue.Body ?? "";
        var parsedFields = parser.ParseIssueForm(issueBody);

        // Check for explicit issue type field
        if (parsedFields.TryGetValue("issue_type", out var issueType) || 
            parsedFields.TryGetValue("type", out issueType))
        {
            var normalizedType = issueType.ToLowerInvariant();
            if (specPack.Categories.Any(c => c.Name.Equals(normalizedType, StringComparison.OrdinalIgnoreCase)))
            {
                return normalizedType;
            }
        }

        // Try keyword matching
        var text = $"{issue.Title} {issueBody}".ToLowerInvariant();
        var categoryScores = new Dictionary<string, int>();

        foreach (var category in specPack.Categories)
        {
            var score = category.Keywords.Count(keyword => 
                text.Contains(keyword.ToLowerInvariant()));
            categoryScores[category.Name] = score;
        }

        var bestMatch = categoryScores.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        if (bestMatch.Value > 0)
        {
            return bestMatch.Key;
        }

        // Fall back to LLM classification
        var categoryNames = specPack.Categories.Select(c => c.Name).ToList();
        var classification = await openAiClient.ClassifyCategoryAsync(
            issue.Title, issueBody, categoryNames);

        return classification.Category;
    }

    private async Task<Dictionary<string, string>> ExtractFieldsAsync(
        GitHubIssue issue,
        List<GitHubComment> comments,
        SpecModels.CategoryChecklist checklist,
        IssueFormParser parser,
        OpenAiClient openAiClient,
        SecretRedactor secretRedactor)
    {
        var issueBody = issue.Body ?? "";
        
        // Redact secrets before processing
        var (redactedBody, _) = secretRedactor.RedactSecrets(issueBody);

        // Try deterministic parsing first
        var parsedFields = parser.ParseIssueForm(redactedBody);
        var kvPairs = parser.ExtractKeyValuePairs(redactedBody);
        var fields = parser.MergeFields(parsedFields, kvPairs);

        // Collect user comments (non-bot)
        var botUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_BOT_USERNAME") ?? "github-actions[bot]";
        var userComments = comments
            .Where(c => !c.User.Login.Equals(botUsername, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Body)
            .ToList();

        var commentsText = string.Join("\n\n---\n\n", userComments);
        var (redactedComments, _) = secretRedactor.RedactSecrets(commentsText);

        // Use LLM to extract any missing fields
        var requiredFieldNames = checklist.RequiredFields.Select(f => f.Name).ToList();
        var llmFields = await openAiClient.ExtractCasePacketAsync(
            redactedBody, redactedComments, requiredFieldNames);

        // Merge with preference for LLM-extracted fields (more accurate for complex cases)
        return parser.MergeFields(fields, llmFields);
    }

    private async Task AskFollowUpQuestionsAsync(
        GitHubIssue issue,
        GitHubRepository repository,
        Dictionary<string, string> extractedFields,
        ScoringResult scoring,
        string category,
        BotState state,
        GitHubApi githubApi,
        OpenAiClient openAiClient,
        CommentComposer commentComposer,
        StateStore stateStore)
    {
        // Filter out already-asked fields
        var missingToAsk = scoring.MissingFields
            .Where(f => !state.AskedFields.Contains(f))
            .ToList();

        if (missingToAsk.Count == 0)
        {
            Console.WriteLine("No new fields to ask about (all have been asked)");
            return;
        }

        var issueBody = issue.Body ?? "";
        var questions = await openAiClient.GenerateFollowUpQuestionsAsync(
            issueBody, category, missingToAsk, state.AskedFields);

        if (questions.Count == 0)
        {
            Console.WriteLine("No questions generated");
            return;
        }

        // Update state
        state.LoopCount++;
        state.AskedFields.AddRange(questions.Select(q => q.Field));
        state.CompletenessScore = scoring.Score;
        state.LastUpdated = DateTime.UtcNow;
        
        // Scenario 3: Prune state to prevent unbounded growth
        state = stateStore.PruneState(state);

        // Compose and post comment
        var commentBody = commentComposer.ComposeFollowUpComment(questions, state.LoopCount);
        var commentWithState = stateStore.EmbedState(commentBody, state);

        await githubApi.PostCommentAsync(
            repository.Owner.Login, repository.Name, issue.Number, commentWithState);

        Console.WriteLine($"Posted follow-up questions (loop {state.LoopCount})");
    }

    private async Task FinalizeIssueAsync(
        GitHubIssue issue,
        GitHubRepository repository,
        Dictionary<string, string> extractedFields,
        ScoringResult scoring,
        string category,
        SpecModels.SpecPackConfig specPack,
        GitHubApi githubApi,
        OpenAiClient openAiClient,
        CommentComposer commentComposer,
        SecretRedactor secretRedactor,
        StateStore stateStore,
        BotState state)
    {
        // Get playbook and repo docs
        var playbook = specPack.Playbooks.TryGetValue(category, out var pb) ? pb : "";
        
        var readmeContent = await githubApi.GetFileContentAsync(
            repository.Owner.Login, repository.Name, "README.md", repository.DefaultBranch);
        var troubleshootingContent = await githubApi.GetFileContentAsync(
            repository.Owner.Login, repository.Name, "TROUBLESHOOTING.md", repository.DefaultBranch);
        
        var repoDocs = $"{readmeContent}\n\n{troubleshootingContent}".Trim();
        if (repoDocs.Length > 3000)
        {
            repoDocs = repoDocs.Substring(0, 3000) + "...";
        }

        // Search for potential duplicates
        var duplicates = new List<(int, string)>();
        if (extractedFields.TryGetValue("error_message", out var errorMsg) && !string.IsNullOrEmpty(errorMsg))
        {
            // Extract key terms from error message
            var keywords = errorMsg.Split(' ')
                .Where(w => w.Length > 4)
                .Take(3)
                .ToList();
            
            if (keywords.Count > 0)
            {
                var searchQuery = string.Join(" ", keywords);
                var similarIssues = await githubApi.SearchIssuesAsync(
                    repository.Owner.Login, repository.Name, searchQuery, 3);
                
                duplicates = similarIssues
                    .Where(i => i.Number != issue.Number)
                    .Select(i => (i.Number, i.Title))
                    .ToList();
            }
        }

        // Collect all comments
        var comments = await githubApi.GetIssueCommentsAsync(
            repository.Owner.Login, repository.Name, issue.Number);
        var botUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_BOT_USERNAME") ?? "github-actions[bot]";
        var userComments = comments
            .Where(c => !c.User.Login.Equals(botUsername, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Body)
            .ToList();
        var commentsText = string.Join("\n\n", userComments);

        // Generate engineer brief
        var brief = await openAiClient.GenerateEngineerBriefAsync(
            issue.Body ?? "", commentsText, category, extractedFields,
            playbook, repoDocs, duplicates);

        // Check for secrets in extracted fields
        var allFieldsText = string.Join("\n", extractedFields.Values);
        var (_, secretFindings) = secretRedactor.RedactSecrets(allFieldsText);

        // Compose and post engineer brief
        var briefComment = commentComposer.ComposeEngineerBrief(
            brief, scoring, extractedFields, secretFindings);

        // SCENARIO 1 FIX: Mark state as finalized to prevent reprocessing
        state.IsActionable = true;
        state.CompletenessScore = scoring.Score;
        state.LastUpdated = DateTime.UtcNow;
        state.IsFinalized = true;
        state.FinalizedAt = DateTime.UtcNow;

        var briefWithState = stateStore.EmbedState(briefComment, state);
        await githubApi.PostCommentAsync(
            repository.Owner.Login, repository.Name, issue.Number, briefWithState);

        // Apply labels and assignees
        var route = specPack.Routing.Routes.FirstOrDefault(r => 
            r.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        
        if (route != null)
        {
            if (route.Labels.Count > 0)
            {
                await githubApi.AddLabelsAsync(
                    repository.Owner.Login, repository.Name, issue.Number, route.Labels);
                Console.WriteLine($"Applied labels: {string.Join(", ", route.Labels)}");
            }

            if (route.Assignees.Count > 0)
            {
                // Filter out placeholder usernames
                var validAssignees = route.Assignees
                    .Where(a => !a.StartsWith("@"))
                    .ToList();
                
                if (validAssignees.Count > 0)
                {
                    await githubApi.AddAssigneesAsync(
                        repository.Owner.Login, repository.Name, issue.Number, validAssignees);
                    Console.WriteLine($"Added assignees: {string.Join(", ", validAssignees)}");
                }
            }
        }

        Console.WriteLine("Posted engineer brief and applied routing");
    }

    private async Task EscalateIssueAsync(
        GitHubIssue issue,
        GitHubRepository repository,
        ScoringResult scoring,
        SpecModels.SpecPackConfig specPack,
        GitHubApi githubApi,
        CommentComposer commentComposer,
        StateStore stateStore,
        BotState state)
    {
        var escalationComment = commentComposer.ComposeEscalationComment(
            scoring, specPack.Routing.EscalationMentions);

        // SCENARIO 1 FIX: Mark state as finalized to prevent reprocessing
        state.LastUpdated = DateTime.UtcNow;
        state.CompletenessScore = scoring.Score;
        state.IsFinalized = true;
        state.FinalizedAt = DateTime.UtcNow;

        var commentWithState = stateStore.EmbedState(escalationComment, state);
        await githubApi.PostCommentAsync(
            repository.Owner.Login, repository.Name, issue.Number, commentWithState);

        // Add escalation label
        await githubApi.AddLabelsAsync(
            repository.Owner.Login, repository.Name, issue.Number, 
            new List<string> { "needs-maintainer-review", "incomplete-info" });

        Console.WriteLine("Posted escalation comment and added labels");
    }

    /// <summary>
    /// Detect disagreement keywords in user feedback (Scenario 7).
    /// </summary>
    private bool DetectDisagreement(string userComment)
    {
        var disagreementKeywords = new[]
        {
            "doesn't apply", "don't apply", "does not apply", "do not apply",
            "already tried", "already did", "already done",
            "didn't work", "did not work", "doesn't work", "does not work",
            "still broken", "still failing", "still see", "still getting",
            "not working", "not relevant", "not applicable",
            "different error", "different issue", "different problem",
            "need clarification", "not sure how", "unclear how",
            "not my case", "not my situation", "doesn't match"
        };

        var lowerComment = userComment.ToLowerInvariant();
        return disagreementKeywords.Any(keyword => lowerComment.Contains(keyword));
    }

    /// <summary>
    /// Handle user disagreement with engineer brief (Scenario 7).
    /// </summary>
    private async Task HandleBriefDisagreementAsync(
        GitHubIssue issue,
        GitHubRepository repository,
        string userFeedback,
        BotState state,
        SpecModels.SpecPackConfig specPack,
        GitHubApi githubApi,
        OpenAiClient openAiClient,
        CommentComposer commentComposer,
        SecretRedactor secretRedactor,
        StateStore stateStore,
        List<GitHubComment> allComments)
    {
        state.BriefIterationCount++;
        
        if (state.BriefIterationCount >= 2)
        {
            // Escalate after 2 iterations
            var escalationComment = $@"I've attempted to provide guidance twice, but it seems we're not addressing your specific situation yet. 

This issue may benefit from human review. I'm adding the escalation label for a maintainer to take a closer look.

{string.Join(" ", specPack.Routing.EscalationMentions)}";

            state.LastUpdated = DateTime.UtcNow;
            state.IsFinalized = true;
            state.FinalizedAt = DateTime.UtcNow;

            var commentWithState = stateStore.EmbedState(escalationComment, state);
            await githubApi.PostCommentAsync(
                repository.Owner.Login, repository.Name, issue.Number, commentWithState);

            await githubApi.AddLabelsAsync(
                repository.Owner.Login, repository.Name, issue.Number,
                new List<string> { "needs-maintainer-review" });

            Console.WriteLine($"Escalated after {state.BriefIterationCount} iterations");
            return;
        }

        // Regenerate brief with feedback
        Console.WriteLine($"Regenerating brief (iteration {state.BriefIterationCount})...");

        // Extract fields from all comments
        var parser = new IssueFormParser();
        var issueAuthor = issue.User.Login;
        var authorComments = allComments
            .Where(c => c.User.Login.Equals(issueAuthor, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Body)
            .ToList();

        var allText = $"{issue.Body}\n\n{string.Join("\n\n", authorComments)}";
        var checklist = specPack.Checklists.TryGetValue(state.Category, out var ck) ? ck : null;

        var requiredFieldNames = checklist?.RequiredFields.Select(f => f.Name).ToList() ?? new List<string>();
        var extractedFields = await openAiClient.ExtractCasePacketAsync(
            issue.Body,
            string.Join("\n\n", authorComments),
            requiredFieldNames);

        var playbook = specPack.Playbooks.TryGetValue(state.Category, out var pb) ? pb : "";
        var previousBrief = ""; // Could extract from previous comment if needed

        var revisedBrief = await openAiClient.RegenerateEngineerBriefAsync(
            previousBrief, userFeedback, extractedFields, playbook, state.Category);

        var dummyScoring = new ScoringResult { Score = 100, IsActionable = true };
        var (redactedFields, secretFindings) = secretRedactor.RedactSecrets(string.Join("\n", extractedFields.Values));
        
        var briefComment = commentComposer.ComposeEngineerBrief(
            revisedBrief, dummyScoring, extractedFields, secretFindings, issue.User.Login);

        briefComment = $@"Thanks for the clarification! Here's a revised approach:

{briefComment}";

        var (redactedBrief, _) = secretRedactor.RedactSecrets(briefComment);
        state.LastUpdated = DateTime.UtcNow;

        var revisedBriefWithState = stateStore.EmbedState(redactedBrief, state);
        var postedComment = await githubApi.PostCommentAsync(
            repository.Owner.Login, repository.Name, issue.Number, revisedBriefWithState);

        if (postedComment != null)
        {
            state.EngineerBriefCommentId = postedComment.Id;
        }

        Console.WriteLine($"Posted revised engineer brief (iteration {state.BriefIterationCount})");
    }
}
