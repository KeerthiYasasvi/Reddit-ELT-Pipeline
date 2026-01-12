using System.Text.RegularExpressions;

namespace SupportConcierge.Scoring;

public class SecretRedactor
{
    private readonly List<Regex> _secretPatterns;

    public SecretRedactor(List<string> patternStrings)
    {
        _secretPatterns = patternStrings
            .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Redact secrets from text, returning both redacted text and list of findings.
    /// </summary>
    public (string redactedText, List<string> findings) RedactSecrets(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (text, new List<string>());
        }

        var findings = new List<string>();
        var redacted = text;

        foreach (var pattern in _secretPatterns)
        {
            var matches = pattern.Matches(redacted);
            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    // Extract the secret type from the pattern
                    var secretType = "credential";
                    if (match.Groups.Count > 1)
                    {
                        secretType = match.Groups[1].Value.ToLowerInvariant();
                    }

                    findings.Add($"Possible {secretType} detected and redacted");

                    // Redact the secret value (usually the last capturing group)
                    var secretValue = match.Groups[match.Groups.Count - 1].Value;
                    if (secretValue.Length > 4)
                    {
                        redacted = redacted.Replace(secretValue, $"[REDACTED_{secretType.ToUpperInvariant()}]");
                    }
                }
            }
        }

        return (redacted, findings);
    }

    /// <summary>
    /// Check if text contains potential secrets without redacting.
    /// </summary>
    public bool ContainsSecrets(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return _secretPatterns.Any(pattern => pattern.IsMatch(text));
    }
}
