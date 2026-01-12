using static SupportConcierge.SpecPack.SpecModels;

namespace SupportConcierge.Scoring;

public class CompletenessScorer
{
    private readonly Validators _validators;

    public CompletenessScorer(Validators validators)
    {
        _validators = validators;
    }

    /// <summary>
    /// Compute completeness score for extracted fields against a checklist.
    /// Returns score (0-100) and list of missing/invalid fields.
    /// </summary>
    public ScoringResult ScoreCompleteness(
        Dictionary<string, string> extractedFields,
        CategoryChecklist checklist)
    {
        var result = new ScoringResult
        {
            Category = checklist.Category,
            Threshold = checklist.CompletenessThreshold
        };

        var totalWeight = 0;
        var earnedWeight = 0;

        foreach (var requiredField in checklist.RequiredFields)
        {
            totalWeight += requiredField.Weight;

            // Check if field exists (try main name and aliases)
            var fieldValue = FindFieldValue(extractedFields, requiredField);

            if (fieldValue == null)
            {
                result.MissingFields.Add(requiredField.Name);
                if (!requiredField.Optional)
                {
                    result.Issues.Add($"Required field '{requiredField.Name}' is missing");
                }
                continue;
            }

            // Validate field
            var validation = _validators.ValidateField(requiredField.Name, fieldValue);
            if (!validation.IsValid)
            {
                result.InvalidFields.Add(requiredField.Name);
                result.Issues.Add($"Field '{requiredField.Name}': {validation.Message}");
                
                // Give partial credit for invalid but present fields
                if (!requiredField.Optional)
                {
                    earnedWeight += requiredField.Weight / 3; // 33% credit
                }
            }
            else
            {
                earnedWeight += requiredField.Weight;
            }
        }

        // Calculate score
        result.Score = totalWeight > 0 ? (int)Math.Round((double)earnedWeight / totalWeight * 100) : 0;
        result.IsActionable = result.Score >= checklist.CompletenessThreshold;

        // Check for contradictions
        var contradictions = _validators.CheckContradictions(extractedFields);
        result.Warnings.AddRange(contradictions);

        return result;
    }

    /// <summary>
    /// Find field value by checking main name and all aliases.
    /// </summary>
    private string? FindFieldValue(Dictionary<string, string> fields, RequiredField requiredField)
    {
        // Try main name
        if (fields.TryGetValue(requiredField.Name, out var value))
        {
            return value;
        }

        // Try aliases
        foreach (var alias in requiredField.Aliases)
        {
            if (fields.TryGetValue(alias, out value))
            {
                return value;
            }
        }

        return null;
    }
}

public class ScoringResult
{
    public string Category { get; set; } = "";
    public int Score { get; set; }
    public int Threshold { get; set; }
    public bool IsActionable { get; set; }
    public List<string> MissingFields { get; set; } = new();
    public List<string> InvalidFields { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
