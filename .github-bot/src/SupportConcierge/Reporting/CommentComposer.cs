using SupportConcierge.Agents;
using SupportConcierge.Scoring;
using System.Text;

namespace SupportConcierge.Reporting;

public class CommentComposer
{
    /// <summary>
    /// Compose a follow-up question comment.
    /// </summary>
    public string ComposeFollowUpComment(
        List<FollowUpQuestion> questions,
        int loopCount,
        string? username = null)
    {
        var sb = new StringBuilder();
        
        // Scenario 1ii: @mention the user if provided
        if (!string.IsNullOrEmpty(username))
        {
            sb.AppendLine($"@{username}");
            sb.AppendLine();
        }
        
        sb.AppendLine("👋 Hi! I need a bit more information to help route this issue effectively.");
        sb.AppendLine();

        for (int i = 0; i < questions.Count; i++)
        {
            sb.AppendLine($"**{i + 1}. {questions[i].Question}**");
            sb.AppendLine($"   _{questions[i].Why_Needed}_");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"_This is follow-up round {loopCount} of 3. Please provide as much detail as possible._");
        sb.AppendLine();
        
        // Scenario 1ii: Add command instructions
        sb.AppendLine("### 📝 Quick Commands");
        sb.AppendLine("- **`/stop`** - Stop asking me questions on this issue (opt-out)");
        sb.AppendLine("- **`/diagnose`** - Activate the bot for your specific sub-issue or different problem (for other users in this thread)");

        return sb.ToString();
    }

    /// <summary>
    /// Compose the engineer-ready brief comment.
    /// </summary>
    public string ComposeEngineerBrief(
        EngineerBrief brief,
        ScoringResult scoring,
        Dictionary<string, string> extractedFields,
        List<string> secretWarnings,
        string? username = null)
    {
        var sb = new StringBuilder();
        
        // Scenario 1ii: @mention the user if provided
        if (!string.IsNullOrEmpty(username))
        {
            sb.AppendLine($"@{username}");
            sb.AppendLine();
        }
        
        sb.AppendLine($"**Summary:** {brief.Summary}");
        sb.AppendLine();

        // Symptoms
        if (brief.Symptoms.Count > 0)
        {
            sb.AppendLine("### 🔍 Symptoms");
            foreach (var symptom in brief.Symptoms)
            {
                sb.AppendLine($"- {symptom}");
            }
            sb.AppendLine();
        }

        // Environment
        if (brief.Environment.Count > 0)
        {
            sb.AppendLine("### 💻 Environment");
            foreach (var kvp in brief.Environment)
            {
                sb.AppendLine($"- **{kvp.Key}:** {kvp.Value}");
            }
            sb.AppendLine();
        }

        // Repro Steps
        if (brief.Repro_Steps.Count > 0)
        {
            sb.AppendLine("### 🔄 Reproduction Steps");
            for (int i = 0; i < brief.Repro_Steps.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {brief.Repro_Steps[i]}");
            }
            sb.AppendLine();
        }

        // Key Evidence
        if (brief.Key_Evidence.Count > 0)
        {
            sb.AppendLine("### 📊 Key Evidence");
            sb.AppendLine("```");
            foreach (var evidence in brief.Key_Evidence)
            {
                sb.AppendLine(evidence);
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Warnings
        if (scoring.Warnings.Count > 0 || secretWarnings.Count > 0)
        {
            sb.AppendLine("### ⚠️ Warnings");
            foreach (var warning in scoring.Warnings)
            {
                sb.AppendLine($"- {warning}");
            }
            foreach (var warning in secretWarnings)
            {
                sb.AppendLine($"- 🔒 {warning}");
            }
            sb.AppendLine();
        }

        // Next Steps
        if (brief.Next_Steps.Count > 0)
        {
            sb.AppendLine("### ✅ Suggested Next Steps");
            foreach (var step in brief.Next_Steps)
            {
                sb.AppendLine($"- {step}");
            }
            sb.AppendLine();
        }

        // Validation Confirmations (Scenario 7a)
        if (brief.Validation_Confirmations.Count > 0)
        {
            sb.AppendLine("### ❓ Please Confirm");
            sb.AppendLine("Before proceeding with the steps above, please confirm:");
            foreach (var confirmation in brief.Validation_Confirmations)
            {
                sb.AppendLine($"- {confirmation}");
            }
            sb.AppendLine();
        }

        // Possible Duplicates
        if (brief.Possible_Duplicates.Count > 0)
        {
            sb.AppendLine("### 🔗 Possibly Related Issues");
            foreach (var dup in brief.Possible_Duplicates)
            {
                sb.AppendLine($"- #{dup.Issue_Number}: {dup.Similarity_Reason}");
            }
            sb.AppendLine();
        }

        // Metadata
        sb.AppendLine("---");
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>📦 Case Packet (JSON)</summary>");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(extractedFields, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("```");
        sb.AppendLine("</details>");
        sb.AppendLine();
        sb.AppendLine($"**Completeness Score:** {scoring.Score}/100 (threshold: {scoring.Threshold})");

        return sb.ToString();
    }

    /// <summary>
    /// Compose escalation comment when max loops reached.
    /// </summary>
    public string ComposeEscalationComment(
        ScoringResult scoring,
        List<string> escalationMentions)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("## ⚠️ Escalation Notice");
        sb.AppendLine();
        sb.AppendLine("After 3 rounds of follow-up questions, this issue still doesn't have enough information to be actionable.");
        sb.AppendLine();
        
        sb.AppendLine("### ❌ Still Missing");
        foreach (var field in scoring.MissingFields)
        {
            sb.AppendLine($"- {field}");
        }
        sb.AppendLine();

        if (scoring.Issues.Count > 0)
        {
            sb.AppendLine("### 🔍 Issues Identified");
            foreach (var issue in scoring.Issues)
            {
                sb.AppendLine($"- {issue}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"**Current Completeness Score:** {scoring.Score}/100 (needs {scoring.Threshold})");
        sb.AppendLine();
        
        var mentions = string.Join(" ", escalationMentions);
        sb.AppendLine($"Tagging for manual review: {mentions}");

        return sb.ToString();
    }
}
