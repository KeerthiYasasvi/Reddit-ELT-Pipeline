using System.Text.RegularExpressions;
using static SupportConcierge.SpecPack.SpecModels;

namespace SupportConcierge.Scoring;

public class Validators
{
    private readonly ValidatorRules _rules;
    private readonly List<Regex> _junkPatterns;
    private readonly Dictionary<string, Regex> _formatValidators;

    public Validators(ValidatorRules rules)
    {
        _rules = rules;
        _junkPatterns = rules.JunkPatterns
            .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            .ToList();
        
        _formatValidators = rules.FormatValidators
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new Regex(kvp.Value, RegexOptions.Compiled),
                StringComparer.OrdinalIgnoreCase
            );
    }

    /// <summary>
    /// Check if a field value is considered junk/placeholder.
    /// </summary>
    public bool IsJunkValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return _junkPatterns.Any(pattern => pattern.IsMatch(value.Trim()));
    }

    /// <summary>
    /// Validate a field value against format rules.
    /// </summary>
    public ValidationResult ValidateField(string fieldName, string? value)
    {
        var result = new ValidationResult { FieldName = fieldName, IsValid = true };

        if (string.IsNullOrWhiteSpace(value))
        {
            result.IsValid = false;
            result.Message = "Field is empty";
            return result;
        }

        if (IsJunkValue(value))
        {
            result.IsValid = false;
            result.Message = "Field contains placeholder or junk value";
            return result;
        }

        // Check format validators
        var normalizedFieldName = fieldName.ToLowerInvariant();
        
        // Check if field name contains validator keywords
        foreach (var validator in _formatValidators)
        {
            if (normalizedFieldName.Contains(validator.Key.ToLowerInvariant()))
            {
                if (!validator.Value.IsMatch(value))
                {
                    result.IsValid = false;
                    result.Message = $"Field does not match expected format for {validator.Key}";
                    return result;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Check for contradictions between fields.
    /// </summary>
    public List<string> CheckContradictions(Dictionary<string, string> fields)
    {
        var warnings = new List<string>();

        foreach (var rule in _rules.ContradictionRules)
        {
            if (!fields.ContainsKey(rule.Field1) || !fields.ContainsKey(rule.Field2))
            {
                continue;
            }

            var value1 = fields[rule.Field1].ToLowerInvariant();
            var value2 = fields[rule.Field2].ToLowerInvariant();

            // Basic contradiction checks
            switch (rule.Condition.ToLowerInvariant())
            {
                case "version_mismatch":
                    // Extract version numbers and compare major versions
                    var version1 = ExtractVersionNumber(value1);
                    var version2 = ExtractVersionNumber(value2);
                    if (version1 != null && version2 != null && 
                        Math.Abs(version1.Value - version2.Value) > 2)
                    {
                        warnings.Add($"{rule.Description}: {rule.Field1} ({value1}) may be incompatible with {rule.Field2} ({value2})");
                    }
                    break;

                case "windows_with_bash_native":
                    if (value1.Contains("windows") && value2.Contains("bash") && !value2.Contains("wsl"))
                    {
                        warnings.Add($"{rule.Description}: Windows typically requires WSL for bash");
                    }
                    break;
            }
        }

        return warnings;
    }

    private int? ExtractVersionNumber(string text)
    {
        var match = Regex.Match(text, @"(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
        {
            return version;
        }
        return null;
    }
}

public class ValidationResult
{
    public string FieldName { get; set; } = "";
    public bool IsValid { get; set; }
    public string Message { get; set; } = "";
}
