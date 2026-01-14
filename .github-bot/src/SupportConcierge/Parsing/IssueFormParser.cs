using System.Text.RegularExpressions;

namespace SupportConcierge.Parsing;

public class IssueFormParser
{
    /// <summary>
    /// Parse issue form markdown into structured fields.
    /// Looks for patterns like "### Field Name" followed by content.
    /// </summary>
    public Dictionary<string, string> ParseIssueForm(string issueBody)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(issueBody))
        {
            return fields;
        }

        // Split by markdown headings (### or ##)
        var lines = issueBody.Split('\n');
        string? currentField = null;
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            // Check if this is a heading
            var headingMatch = Regex.Match(line, @"^#+\s+(.+)$");
            if (headingMatch.Success)
            {
                // Save previous field
                if (currentField != null)
                {
                    var content = string.Join("\n", currentContent).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        fields[NormalizeFieldName(currentField)] = content;
                    }
                }

                // Start new field
                currentField = headingMatch.Groups[1].Value.Trim();
                currentContent.Clear();
            }
            else if (currentField != null)
            {
                currentContent.Add(line);
            }
        }

        // Save last field
        if (currentField != null)
        {
            var content = string.Join("\n", currentContent).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                fields[NormalizeFieldName(currentField)] = content;
            }
        }

        return fields;
    }

    /// <summary>
    /// Normalize field names to match spec pack field names.
    /// </summary>
    private string NormalizeFieldName(string fieldName)
    {
        // Remove special characters and convert to lowercase with underscores
        var normalized = Regex.Replace(fieldName, @"[^\w\s]", "");
        normalized = Regex.Replace(normalized, @"\s+", "_");
        return normalized.ToLowerInvariant();
    }

    /// <summary>
    /// Extract key-value pairs from simple patterns like "Key: Value"
    /// </summary>
    public Dictionary<string, string> ExtractKeyValuePairs(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(text))
        {
            return fields;
        }

        var lines = text.Split('\n');
        foreach (var line in lines)
        {
            // Match patterns like "Key: Value" or "Key = Value"
            var match = Regex.Match(line, @"^\s*([^:=]+)\s*[:=]\s*(.+)$");
            if (match.Success)
            {
                var key = NormalizeFieldName(match.Groups[1].Value.Trim());
                var value = match.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    fields[key] = value;
                }
            }
        }

        return fields;
    }

    /// <summary>
    /// Merge multiple field dictionaries, with later ones taking precedence.
    /// </summary>
    public Dictionary<string, string> MergeFields(params Dictionary<string, string>[] fieldDicts)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dict in fieldDicts)
        {
            foreach (var kvp in dict)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        return merged;
    }
}
