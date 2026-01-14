using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Text;

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
    /// Handles both compressed and uncompressed state formats.
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
            var data = match.Groups[1].Value;
            
            // Scenario 3: Handle compressed state
            if (data.StartsWith("compressed:"))
            {
                var compressed = data.Substring("compressed:".Length);
                data = DecompressString(compressed);
            }
            
            return JsonSerializer.Deserialize<BotState>(data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Embed state into a comment body as a hidden HTML comment.
    /// Applies compression if state exceeds size threshold.
    /// </summary>
    public string EmbedState(string commentBody, BotState state)
    {
        var json = JsonSerializer.Serialize(state);
        
        // Scenario 3: Monitor state size and warn if approaching GitHub limits
        var stateSize = Encoding.UTF8.GetByteCount(json);
        if (stateSize > 50000) // GitHub comment limit is ~65KB
        {
            Console.WriteLine($"WARNING: State size is {stateSize} bytes (approaching 65KB GitHub limit)");
            Console.WriteLine("Consider pruning AskedFields history or using external storage");
        }
        
        // Scenario 3: Compress state if it's large (> 5KB)
        var stateComment = stateSize > 5000 
            ? $"{StateMarkerPrefix}compressed:{CompressString(json)}{StateMarkerSuffix}"
            : $"{StateMarkerPrefix}{json}{StateMarkerSuffix}";
        
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
    public BotState CreateInitialState(string category, string issueAuthor)
    {
        return new BotState
        {
            Category = category,
            LoopCount = 0,
            AskedFields = new List<string>(),
            LastUpdated = DateTime.UtcNow,
            IssueAuthor = issueAuthor
        };
    }
    
    /// <summary>
    /// Scenario 3: Prune old data from state to prevent size bloat.
    /// Keeps only the most recent N asked fields.
    /// </summary>
    public BotState PruneState(BotState state, int maxAskedFieldsHistory = 20)
    {
        // Keep only recent asked fields to prevent unbounded growth
        if (state.AskedFields.Count > maxAskedFieldsHistory)
        {
            state.AskedFields = state.AskedFields
                .Skip(state.AskedFields.Count - maxAskedFieldsHistory)
                .ToList();
            Console.WriteLine($"Pruned AskedFields history to {maxAskedFieldsHistory} most recent items");
        }
        
        return state;
    }
    
    /// <summary>
    /// Scenario 3: Compress string using GZip.
    /// </summary>
    private static string CompressString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var msi = new MemoryStream(bytes);
        using var mso = new MemoryStream();
        using (var gs = new GZipStream(mso, CompressionMode.Compress))
        {
            msi.CopyTo(gs);
        }
        return Convert.ToBase64String(mso.ToArray());
    }
    
    /// <summary>
    /// Scenario 3: Decompress GZip-compressed string.
    /// </summary>
    private static string DecompressString(string compressedText)
    {
        var bytes = Convert.FromBase64String(compressedText);
        using var msi = new MemoryStream(bytes);
        using var mso = new MemoryStream();
        using (var gs = new GZipStream(msi, CompressionMode.Decompress))
        {
            gs.CopyTo(mso);
        }
        return Encoding.UTF8.GetString(mso.ToArray());
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
    
    // Scenario 1 fixes: Track issue author and finalization status
    public string IssueAuthor { get; set; } = "";
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    
    // Scenario 7: Track brief feedback and iteration
    public long? EngineerBriefCommentId { get; set; }
    public int BriefIterationCount { get; set; }
}
