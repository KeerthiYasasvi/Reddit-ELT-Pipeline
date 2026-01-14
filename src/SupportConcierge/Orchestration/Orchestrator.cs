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

        // SCENARIO 8: Extract action type for issue events to detect edits
        string? actionType = null;
        if (eventName == "issues" && eventPayload.TryGetProperty("action", out var actionElement))
        {
            actionType = actionElement.GetString();
            Console.WriteLine($"Issue action type: {actionType}");
        }

        // Extract issue and repository info
        var issue = eventPayload.GetProperty("issue").Deserialize<GitHubIssue>();
        var repository = eventPayload.GetProperty("repository").Deserialize<GitHubRepository>();

        if (issue == null || repository == null)
        {
            Console.WriteLine("ERROR: Could not parse issue or repository from event");
            return;
        }

        // SCENARIO 1 FIX: For issue_comment events, we'll handle command parsing later
        // (moved to main logic to properly track comment and author info)

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

        // SCENARIO 8: Smart Edit Detection - skip trivial edits
        if (eventName == "issues" && actionType == "edited" && currentState != null)
        {
            Console.WriteLine("Edit event detected. Checking if re-processing is needed...");
            
            // Check if we have changes information
            if (eventPayload.TryGetProperty("changes", out var changesElement))
            {
                // Get old and new body if body was changed
                bool bodyChanged = changesElement.TryGetProperty("body", out var bodyChange);
                bool titleChanged = changesElement.TryGetProperty("title", out var titleChange);
                
                if (!bodyChanged && !titleChanged)
                {
                    Console.WriteLine("Edit detected but no body or title changes. Skipping.");
                    return;
                }

                // If only title changed, skip
                if (!bodyChanged && titleChanged)
                {
                    Console.WriteLine("Only title was edited. Skipping re-processing.");
                    return;
                }

                // Check edit count limit
                const int MaxEditsToProcess = 3;
                if (currentState.EditCount >= MaxEditsToProcess)
                {
                    Console.WriteLine($"Edit count ({currentState.EditCount}) has reached maximum ({MaxEditsToProcess}). Skipping re-processing.");
                    return;
                }

                // Compare new body with last processed body
                string? oldBody = bodyChange.TryGetProperty("from", out var fromElement) ? fromElement.GetString() : null;
                string newBody = issue.Body ?? "";
                
                // If we have a cached last processed body, use that for comparison
                string? lastProcessedBody = currentState.LastProcessedBody;
                
                if (!string.IsNullOrEmpty(lastProcessedBody))
                {
                    // Check if the change is meaningful
                    if (IsTrivialEdit(lastProcessedBody, newBody))
                    {
                        Console.WriteLine("Edit appears to be trivial (typo fix or formatting). Skipping re-processing.");
                        return;
                    }
                }
                else if (!string.IsNullOrEmpty(oldBody))
                {
                    // Fallback to old body from changes element
                    if (IsTrivialEdit(oldBody, newBody))
                    {
                        Console.WriteLine("Edit appears to be trivial (typo fix or formatting). Skipping re-processing.");
                        return;
                    }
                }

                // Meaningful edit detected - increment edit count and update cached body
                Console.WriteLine($"Meaningful edit detected. Processing edit #{currentState.EditCount + 1}");
                currentState.EditCount++;
                currentState.LastProcessedBody = newBody;
            }
        }

        // SCENARIO 1ii: Extract comment info for command handling (if this is an issue_comment event)
        GitHubComment? incomingComment = null;
        string? commentAuthor = null;
        if (eventName == "issue_comment")
        {
            incomingComment = eventPayload.GetProperty("comment").Deserialize<GitHubComment>();
            if (incomingComment != null)
            {
                commentAuthor = incomingComment.User.Login;
                Console.WriteLine($"Processing comment from {commentAuthor}");
            }
        }

        // SCENARIO 1ii: Check for /stop command (user opt-out) - process regardless of finalization status
        if (incomingComment != null && commentAuthor != null)
        {
            var (command, args) = ParseCommand(incomingComment.Body);
            if (command == "stop")
            {
                Console.WriteLine($"User {commentAuthor} used /stop command. Adding to OptedOutUsers.");
                if (currentState == null)
                {
                    currentState = stateStore.CreateInitialState("unknown", issue.User.Login);
                }
                if (!currentState.OptedOutUsers.Contains(commentAuthor, StringComparer.OrdinalIgnoreCase))
                {
                    currentState.OptedOutUsers.Add(commentAuthor);
                }
                // Post acknowledgment
                var stopAck = $"Got it! I won't ask you any more questions on this issue.";
                var stopWithState = stateStore.EmbedState(stopAck, currentState);
                await githubApi.PostCommentAsync(
                    repository.Owner.Login, repository.Name, issue.Number, stopWithState);
                Console.WriteLine("Posted /stop acknowledgment.");
                return;
            }
        }

        // SCENARIO 1 FIX: Check if issue is already finalized
        // But allow if comment author is using /diagnose or is the original author or is a tracked sub-issue user
        bool isOriginalAuthor = commentAuthor?.Equals(issue.User.Login, StringComparison.OrdinalIgnoreCase) ?? false;
        bool isDiagnoseCommand = false;
        
        if (incomingComment != null && currentState != null)
        {
            var (command, args) = ParseCommand(incomingComment.Body);
            isDiagnoseCommand = (command == "diagnose");
        }

        // Check if commenter is being tracked as sub-issue user (needed for finalization check)
        bool isTrackedSubIssueUserEarly = commentAuthor != null && currentState != null && currentState.SubIssueUsers.ContainsKey(commentAuthor.ToLowerInvariant());

        if (currentState != null && currentState.IsFinalized && !isDiagnoseCommand && !isOriginalAuthor && !isTrackedSubIssueUserEarly)
        {
            Console.WriteLine($"Issue already finalized and no /diagnose command. Skipping non-author processing.");
            return;
        }

        // SCENARIO 1iii: Check if commenter is opted-out, but allow re-engagement via /diagnose
        if (incomingComment != null && commentAuthor != null && currentState != null)
        {
            if (IsUserOptedOut(commentAuthor, currentState))
            {
                if (isDiagnoseCommand)
                {
                    // Re-engage: Remove user from opted-out list
                    currentState.OptedOutUsers.Remove(commentAuthor.ToLowerInvariant());
                    Console.WriteLine($"User {commentAuthor} re-engaged via /diagnose. Removed from opted-out list.");
                }
                else
                {
                    Console.WriteLine($"User {commentAuthor} is opted-out and not using /diagnose. Skipping processing.");
                    return;
                }
            }
        }

        // SCENARIO 1ii: Check if commenter is being tracked as sub-issue user
        bool isTrackedSubIssueUser = false;
        if (commentAuthor != null && currentState != null)
        {
            isTrackedSubIssueUser = currentState.SubIssueUsers.ContainsKey(commentAuthor.ToLowerInvariant());
        }

        // SCENARIO 1 FIX: Only process comments from the issue author (unless /diagnose, already tracked, or new issue)
        if (eventName == "issue_comment" && commentAuthor != null && !isOriginalAuthor && !isDiagnoseCommand && !isTrackedSubIssueUser)
        {
            Console.WriteLine($"Skipping: Comment from {commentAuthor} (not from issue author, not using /diagnose, and not tracked sub-issue user)");
            return;
        }

        // SCENARIO 1ii: If this is a tracked sub-issue user, set them as current
        if (isTrackedSubIssueUser && currentState != null && commentAuthor != null)
        {
            currentState.CurrentSubIssueUser = commentAuthor;
            Console.WriteLine($"Processing tracked sub-issue user: {commentAuthor}");
        }

        // SCENARIO 1 FIX: Only process comments from the issue author
        // Filter comments to only include issue author's responses (for field extraction)
        var issueAuthor = issue.User.Login;
        
        // SCENARIO 1ii: If processing a sub-issue user, use their comments instead
        var targetUser = currentState?.CurrentSubIssueUser ?? issueAuthor;
        var targetComments = comments
            .Where(c => c.User.Login.Equals(targetUser, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        Console.WriteLine($"Issue author: {issueAuthor}");
        Console.WriteLine($"Target user: {targetUser}");
        Console.WriteLine($"Total comments: {comments.Count}, Target user comments: {targetComments.Count}");

        // Initialize validators and scorers
        var validators = new Validators(specPack.Validators);
        var secretRedactor = new SecretRedactor(specPack.Validators.SecretPatterns);
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

        // Step 2: Extract fields (using target user's comments + original issue)
        Console.WriteLine("Extracting fields from issue...");
        var extractedFields = await ExtractFieldsAsync(
            issue, targetComments, checklist, parser, openAiClient, secretRedactor);

        Console.WriteLine($"Extracted {extractedFields.Count} fields");

        // Step 3: Score completeness
        var scoring = scorer.ScoreCompleteness(extractedFields, checklist);
        Console.WriteLine($"Completeness score: {scoring.Score}/{scoring.Threshold}");
        Console.WriteLine($"Missing fields: {string.Join(", ", scoring.MissingFields)}");

        // Initialize or update state
        if (currentState == null)
        {
            currentState = stateStore.CreateInitialState(category, issueAuthor);
            // SCENARIO 8: Cache initial issue body for edit detection
            currentState.LastProcessedBody = issue.Body;
        }

        // SCENARIO 1ii: Handle /diagnose command for sub-issues
        if (isDiagnoseCommand && incomingComment != null && commentAuthor != null)
        {
            Console.WriteLine($"Processing /diagnose command from {commentAuthor}");
            
            // Add or update sub-issue user tracking
            var userLower = commentAuthor.ToLowerInvariant();
            if (!currentState.SubIssueUsers.ContainsKey(userLower))
            {
                currentState.SubIssueUsers[userLower] = 0;  // Start at round 0
                Console.WriteLine($"Added {commentAuthor} to SubIssueUsers tracking");
            }
            
            currentState.CurrentSubIssueUser = commentAuthor;
            
            // Check if this user has already reached max rounds
            if (GetUserRoundCount(commentAuthor, currentState) >= MaxLoops)
            {
                Console.WriteLine($"User {commentAuthor} has already reached max {MaxLoops} rounds for sub-issue. Skipping.");
                return;
            }
        }

        // Step 4: Decide action based on completeness and loop count
        if (scoring.IsActionable)
        {
            // Issue is actionable - create engineer brief and route
            Console.WriteLine("Issue is actionable. Creating engineer brief...");
            var targetUsername = currentState.CurrentSubIssueUser ?? issueAuthor;
            await FinalizeIssueAsync(
                issue, repository, extractedFields, scoring, category,
                specPack, githubApi, openAiClient, commentComposer, 
                secretRedactor, stateStore, currentState, targetUsername);
        }
        else if (currentState.LoopCount >= MaxLoops || (currentState.CurrentSubIssueUser != null && GetUserRoundCount(currentState.CurrentSubIssueUser, currentState) >= MaxLoops))
        {
            // Max loops reached - escalate
            var targetUserForEscalation = currentState.CurrentSubIssueUser ?? issue.User.Login;
            Console.WriteLine($"Max loops ({MaxLoops}) reached for {targetUserForEscalation} without becoming actionable. Escalating...");
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

        // SCENARIO 1ii: Update state - handle sub-issue users separately
        if (state.CurrentSubIssueUser != null)
        {
            // Track round count per sub-issue user
            var userLower = state.CurrentSubIssueUser.ToLowerInvariant();
            state.SubIssueUsers[userLower]++;
            Console.WriteLine($"Incremented round count for {state.CurrentSubIssueUser}: {state.SubIssueUsers[userLower]}");
        }
        else
        {
            // Original author - use global loop count
            state.LoopCount++;
        }

        state.AskedFields.AddRange(questions.Select(q => q.Field));
        state.CompletenessScore = scoring.Score;
        state.LastUpdated = DateTime.UtcNow;

        // SCENARIO 1ii: Determine who to @mention in the comment
        var targetUsername = state.CurrentSubIssueUser ?? issue.User.Login;

        // Compose and post comment
        var loopDisplay = state.CurrentSubIssueUser != null ? state.SubIssueUsers[state.CurrentSubIssueUser.ToLowerInvariant()] : state.LoopCount;
        var commentBody = commentComposer.ComposeFollowUpComment(questions, loopDisplay, targetUsername);
        var commentWithState = stateStore.EmbedState(commentBody, state);

        await githubApi.PostCommentAsync(
            repository.Owner.Login, repository.Name, issue.Number, commentWithState);

        Console.WriteLine($"Posted follow-up questions (round {loopDisplay}) to @{targetUsername}");
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
        BotState state,
        string targetUsername)
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

        // SCENARIO 1ii FIX: Collect comments from target user only and label for LLM context
        var comments = await githubApi.GetIssueCommentsAsync(
            repository.Owner.Login, repository.Name, issue.Number);
        var botUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_BOT_USERNAME") ?? "github-actions[bot]";
        var userComments = comments
            .Where(c => !c.User.Login.Equals(botUsername, StringComparison.OrdinalIgnoreCase) &&
                        c.User.Login.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
            .ToList();

        string commentsText;

        // If this is a sub-issue, separate original vs sub-issue comments
        if (!string.IsNullOrEmpty(state.CurrentSubIssueUser) && 
            state.CurrentSubIssueUser.Equals(targetUsername, StringComparison.OrdinalIgnoreCase))
        {
            // Find the /diagnose command comment
            int diagnoseIndex = userComments.FindIndex(c => c.Body.Contains("/diagnose"));
            
            if (diagnoseIndex >= 0)
            {
                var originalComments = userComments.Take(diagnoseIndex).Select(c => c.Body);
                var subIssueComments = userComments.Skip(diagnoseIndex).Select(c => c.Body);
                
                commentsText = $"ORIGINAL ISSUE CONTEXT:\n{string.Join("\n\n", originalComments)}\n\n" +
                              $"SUB-ISSUE FOR @{targetUsername} (USE THIS FOR ENGINEER BRIEF):\n{string.Join("\n\n", subIssueComments)}";
                
                Console.WriteLine($"Sub-issue mode: Split comments at /diagnose (original: {originalComments.Count()}, sub-issue: {subIssueComments.Count()})");
            }
            else
            {
                // Fallback if /diagnose not found
                commentsText = string.Join("\n\n", userComments.Select(c => c.Body));
                Console.WriteLine($"Warning: Sub-issue mode but /diagnose not found in comments");
            }
        }
        else
        {
            // Regular issue or different user's sub-issue
            commentsText = string.Join("\n\n", userComments.Select(c => c.Body));
            Console.WriteLine($"Using comments from target user '{targetUsername}' for engineer brief ({userComments.Count} comments)");
        }

        // Generate engineer brief
        var brief = await openAiClient.GenerateEngineerBriefAsync(
            issue.Body ?? "", commentsText, category, extractedFields,
            playbook, repoDocs, duplicates);

        // Check for secrets in extracted fields
        var allFieldsText = string.Join("\n", extractedFields.Values);
        var (_, secretFindings) = secretRedactor.RedactSecrets(allFieldsText);

        // SCENARIO 1ii: Compose and post engineer brief with @mention
        var briefComment = commentComposer.ComposeEngineerBrief(
            brief, scoring, extractedFields, secretFindings, targetUsername);

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
    /// Scenario 1ii: Parse commands from comment text
    /// </summary>
    private (string? command, string args) ParseCommand(string commentBody)
    {
        var trimmed = commentBody.Trim();
        
        // Check for /diagnose command
        if (trimmed.StartsWith("/diagnose", StringComparison.OrdinalIgnoreCase))
        {
            var args = trimmed.Length > 9 ? trimmed.Substring(9).Trim() : "";
            return ("diagnose", args);
        }
        
        // Check for /stop command (also support /no-questions)
        if (trimmed.StartsWith("/stop", StringComparison.OrdinalIgnoreCase) || 
            trimmed.StartsWith("/no-questions", StringComparison.OrdinalIgnoreCase))
        {
            return ("stop", "");
        }
        
        return (null, "");
    }

    /// <summary>
    /// Scenario 1ii: Check if a user is opted out
    /// </summary>
    private bool IsUserOptedOut(string username, BotState state)
    {
        return state.OptedOutUsers.Contains(username, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scenario 1ii: Get round count for a sub-issue user (or 0 if not tracked)
    /// </summary>
    private int GetUserRoundCount(string username, BotState state)
    {
        if (state.SubIssueUsers.TryGetValue(username.ToLowerInvariant(), out var count))
        {
            return count;
        }
        return 0;
    }

    /// <summary>
    /// Scenario 1ii: Check if original author has reached max rounds
    /// </summary>
    private bool HasMaxRoundsBeenReached(BotState state, bool isSubIssueUser)
    {
        const int MaxRounds = 3;
        if (isSubIssueUser && state.CurrentSubIssueUser != null)
        {
            return GetUserRoundCount(state.CurrentSubIssueUser, state) >= MaxRounds;
        }
        return state.LoopCount >= MaxRounds;
    }

    /// <summary>
    /// Scenario 8: Determine if an edit is trivial (typo fix, formatting) vs meaningful (new information)
    /// </summary>
    private bool IsTrivialEdit(string oldText, string newText)
    {
        if (string.IsNullOrEmpty(oldText) || string.IsNullOrEmpty(newText))
        {
            return false; // Empty to non-empty or vice versa is meaningful
        }

        // Normalize both texts for comparison (remove extra whitespace, normalize line endings)
        var normalizedOld = NormalizeText(oldText);
        var normalizedNew = NormalizeText(newText);

        // If they're identical after normalization, it's just formatting
        if (normalizedOld == normalizedNew)
        {
            Console.WriteLine("Edit is purely formatting (whitespace/line ending changes)");
            return true;
        }

        // Calculate edit distance as percentage of original length
        int editDistance = CalculateLevenshteinDistance(normalizedOld, normalizedNew);
        int maxLength = Math.Max(normalizedOld.Length, normalizedNew.Length);
        double changePercentage = (double)editDistance / maxLength * 100;

        Console.WriteLine($"Edit distance: {editDistance}, Max length: {maxLength}, Change: {changePercentage:F2}%");

        // If less than 5% of the text changed, consider it trivial
        const double TrivialThreshold = 5.0;
        if (changePercentage < TrivialThreshold)
        {
            Console.WriteLine($"Edit is trivial (< {TrivialThreshold}% change)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Normalize text for comparison by removing extra whitespace and normalizing line endings
    /// </summary>
    private string NormalizeText(string text)
    {
        // Replace all whitespace sequences with single space
        var normalized = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return normalized.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Calculate Levenshtein distance between two strings
    /// </summary>
    private int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return target?.Length ?? 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        int sourceLength = source.Length;
        int targetLength = target.Length;

        var distance = new int[sourceLength + 1, targetLength + 1];

        for (int i = 0; i <= sourceLength; i++)
        {
            distance[i, 0] = i;
        }

        for (int j = 0; j <= targetLength; j++)
        {
            distance[0, j] = j;
        }

        for (int i = 1; i <= sourceLength; i++)
        {
            for (int j = 1; j <= targetLength; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sourceLength, targetLength];
    }
}
