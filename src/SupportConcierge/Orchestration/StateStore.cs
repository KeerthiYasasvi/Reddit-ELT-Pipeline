using System.Text.Json;
using System.Text.RegularExpressions;

namespace SupportConcierge.Orchestration;

/// <summary>
/// Stores and retrieves bot state from hidden HTML comments in GitHub issue comments.
/// Pattern: <!-- supportbot_state:{"loop_count":1,"asked_fields":["os"]} -->
/// </summary>
public class StateStore
{
    private const string StateMarkerPrefix = "<!-- supportbot_state:";
    private const string StateMarkerSuffix = " -->";

    /// <summary>
    /// Extract state from a comment body.
    /// </summary>
    public BotState? ExtractState(string commentBody)
    {
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            return null;
        }

        var pattern = Regex.Escape(StateMarkerPrefix) + @"(.+?)" + Regex.Escape(StateMarkerSuffix);
        var match = Regex.Match(commentBody, pattern, RegexOptions.Singleline);

        if (!match.Success)
        {
            return null;
        }

        try
        {
            var json = match.Groups[1].Value;
            return JsonSerializer.Deserialize<BotState>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Embed state into a comment body as a hidden HTML comment.
    /// </summary>
    public string EmbedState(string commentBody, BotState state)
    {
        var json = JsonSerializer.Serialize(state);
        var stateComment = $"{StateMarkerPrefix}{json}{StateMarkerSuffix}";
        
        // Remove any existing state markers first
        var cleanedBody = RemoveState(commentBody);
        
        // Append state marker at the end
        return $"{cleanedBody}\n\n{stateComment}";
    }

    /// <summary>
    /// Remove state markers from comment body.
    /// </summary>
    public string RemoveState(string commentBody)
    {
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            return commentBody;
        }

        var pattern = Regex.Escape(StateMarkerPrefix) + @".+?" + Regex.Escape(StateMarkerSuffix);
        return Regex.Replace(commentBody, pattern, "", RegexOptions.Singleline).Trim();
    }

    /// <summary>
    /// Create initial state for a new issue.
    /// </summary>
    public BotState CreateInitialState(string category)
    {
        return new BotState
        {
            Category = category,
            LoopCount = 0,
            AskedFields = new List<string>(),
            LastUpdated = DateTime.UtcNow
        };
    }
}

public class BotState
{
    public string Category { get; set; } = "";
    public int LoopCount { get; set; }
    public List<string> AskedFields { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public bool IsActionable { get; set; }
    public int CompletenessScore { get; set; }
}
