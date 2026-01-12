using System.Text.Json;
using SupportConcierge.Orchestration;
using SupportConcierge.GitHub;
using SupportConcierge.SpecPack;
using SupportConcierge.Parsing;
using SupportConcierge.Scoring;
using SupportConcierge.Agents;

namespace EvalRunner;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Support Concierge Evaluation Runner ===\n");

        // Check required environment variables
        var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(openaiKey))
        {
            Console.WriteLine("ERROR: OPENAI_API_KEY environment variable required");
            return 1;
        }

        // Set up environment
        Environment.SetEnvironmentVariable("SUPPORTBOT_SPEC_DIR", "../../.supportbot");

        // Load scenarios
        var scenarioDir = "scenarios";
        if (!Directory.Exists(scenarioDir))
        {
            scenarioDir = "../scenarios";
        }

        if (!Directory.Exists(scenarioDir))
        {
            Console.WriteLine($"ERROR: Scenarios directory not found");
            return 1;
        }

        var scenarioFiles = Directory.GetFiles(scenarioDir, "*.json");
        Console.WriteLine($"Found {scenarioFiles.Length} test scenarios\n");

        var results = new List<EvalResult>();

        foreach (var scenarioFile in scenarioFiles)
        {
            var scenarioName = Path.GetFileNameWithoutExtension(scenarioFile);
            Console.WriteLine($"--- Running: {scenarioName} ---");

            try
            {
                var result = await RunScenarioAsync(scenarioFile);
                results.Add(result);

                Console.WriteLine($"✓ Category: {result.DetectedCategory}");
                Console.WriteLine($"✓ Score: {result.CompletenessScore}");
                Console.WriteLine($"✓ Actionable: {result.IsActionable}");
                Console.WriteLine($"✓ Extracted Fields: {result.ExtractedFieldCount}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ ERROR: {ex.Message}");
                Console.WriteLine();
                results.Add(new EvalResult
                {
                    ScenarioName = scenarioName,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        // Generate report
        Console.WriteLine("\n=== Evaluation Report ===\n");
        
        var successful = results.Count(r => r.Success);
        var total = results.Count;
        
        Console.WriteLine($"Scenarios Run: {total}");
        Console.WriteLine($"Successful: {successful}/{total} ({(double)successful / total * 100:F1}%)");
        Console.WriteLine();

        Console.WriteLine("Metrics:");
        if (results.Any(r => r.Success))
        {
            var avgScore = results.Where(r => r.Success).Average(r => r.CompletenessScore);
            var avgFields = results.Where(r => r.Success).Average(r => r.ExtractedFieldCount);
            var actionableRate = results.Where(r => r.Success).Count(r => r.IsActionable) / 
                                 (double)results.Count(r => r.Success);

            Console.WriteLine($"  Average Completeness Score: {avgScore:F1}");
            Console.WriteLine($"  Average Fields Extracted: {avgFields:F1}");
            Console.WriteLine($"  Actionable Rate: {actionableRate * 100:F1}%");
        }

        Console.WriteLine("\nDetailed Results:");
        foreach (var result in results)
        {
            var status = result.Success ? "✓" : "✗";
            Console.WriteLine($"  {status} {result.ScenarioName}");
            if (!result.Success)
            {
                Console.WriteLine($"      Error: {result.ErrorMessage}");
            }
            else
            {
                Console.WriteLine($"      Category: {result.DetectedCategory}, Score: {result.CompletenessScore}, Fields: {result.ExtractedFieldCount}");
                
                if (result.HallucinationWarnings.Count > 0)
                {
                    Console.WriteLine($"      ⚠ Hallucination Warnings: {string.Join("; ", result.HallucinationWarnings)}");
                }
            }
        }

        // Save report to file
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "eval_report.json");
        var reportJson = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(reportPath, reportJson);
        Console.WriteLine($"\nReport saved to: {reportPath}");

        return successful == total ? 0 : 1;
    }

    private static async Task<EvalResult> RunScenarioAsync(string scenarioFile)
    {
        var scenarioName = Path.GetFileNameWithoutExtension(scenarioFile);
        var json = await File.ReadAllTextAsync(scenarioFile);
        var scenario = JsonSerializer.Deserialize<TestScenario>(json);

        if (scenario == null)
        {
            throw new Exception("Failed to parse scenario");
        }

        var result = new EvalResult
        {
            ScenarioName = scenarioName,
            Success = true
        };

        // Load SpecPack
        var specPackLoader = new SpecPackLoader();
        var specPack = await specPackLoader.LoadSpecPackAsync();

        // Initialize components
        var parser = new IssueFormParser();
        var validators = new Validators(specPack.Validators);
        var secretRedactor = new SecretRedactor(specPack.Validators.SecretPatterns);
        var scorer = new CompletenessScorer(validators);
        var openAiClient = new OpenAiClient();

        // Determine category
        var categoryNames = specPack.Categories.Select(c => c.Name).ToList();
        var classification = await openAiClient.ClassifyCategoryAsync(
            scenario.Issue.Title, scenario.Issue.Body ?? "", categoryNames);

        result.DetectedCategory = classification.Category;

        // Extract fields
        var parsedFields = parser.ParseIssueForm(scenario.Issue.Body ?? "");
        var kvPairs = parser.ExtractKeyValuePairs(scenario.Issue.Body ?? "");
        var deterministicFields = parser.MergeFields(parsedFields, kvPairs);

        // Get checklist
        if (!specPack.Checklists.TryGetValue(classification.Category, out var checklist))
        {
            throw new Exception($"No checklist for category {classification.Category}");
        }

        var requiredFieldNames = checklist.RequiredFields.Select(f => f.Name).ToList();
        var llmFields = await openAiClient.ExtractCasePacketAsync(
            scenario.Issue.Body ?? "", "", requiredFieldNames);

        var allFields = parser.MergeFields(deterministicFields, llmFields);
        result.ExtractedFieldCount = allFields.Count;

        // Score
        var scoring = scorer.ScoreCompleteness(allFields, checklist);
        result.CompletenessScore = scoring.Score;
        result.IsActionable = scoring.IsActionable;

        // Validation checks
        if (scenario.Expected != null)
        {
            // Check category
            if (!string.IsNullOrEmpty(scenario.Expected.Category) && 
                scenario.Expected.Category != classification.Category)
            {
                result.HallucinationWarnings.Add(
                    $"Expected category '{scenario.Expected.Category}' but got '{classification.Category}'");
            }

            // Check actionability
            if (scenario.Expected.ShouldBeActionable.HasValue && 
                scenario.Expected.ShouldBeActionable != scoring.IsActionable)
            {
                result.HallucinationWarnings.Add(
                    $"Expected actionable={scenario.Expected.ShouldBeActionable} but got {scoring.IsActionable}");
            }

            // Check expected fields were extracted
            if (scenario.Expected.ShouldExtractFields != null)
            {
                var missingExpected = scenario.Expected.ShouldExtractFields
                    .Where(f => !allFields.ContainsKey(f))
                    .ToList();
                
                if (missingExpected.Count > 0)
                {
                    result.HallucinationWarnings.Add(
                        $"Failed to extract expected fields: {string.Join(", ", missingExpected)}");
                }
            }
        }

        // Check for hallucinations in extracted values
        var issueText = scenario.Issue.Body ?? "";
        foreach (var field in allFields)
        {
            // Check if extracted value appears in source text (basic check)
            if (field.Value.Length > 10 && !issueText.Contains(field.Value, StringComparison.OrdinalIgnoreCase))
            {
                // Value might be a summary/extraction, check for key terms
                var terms = field.Value.Split(' ').Where(t => t.Length > 4).Take(3).ToList();
                var foundTerms = terms.Count(t => issueText.Contains(t, StringComparison.OrdinalIgnoreCase));
                
                if (foundTerms == 0)
                {
                    result.HallucinationWarnings.Add(
                        $"Field '{field.Key}' value may be hallucinated (no matching terms in source)");
                }
            }
        }

        return result;
    }
}

// Models for eval scenarios
public class TestScenario
{
    public GitHubIssue Issue { get; set; } = new();
    public GitHubRepository Repository { get; set; } = new();
    public ExpectedResults? Expected { get; set; }
}

public class ExpectedResults
{
    public string? Category { get; set; }
    public bool? ShouldBeActionable { get; set; }
    public bool? ShouldAskFollowup { get; set; }
    public List<string>? ExpectedLabels { get; set; }
    public List<string>? ShouldExtractFields { get; set; }
    public List<string>? ExpectedMissingFields { get; set; }
    public int? ExpectedQuestions { get; set; }
}

public class EvalResult
{
    public string ScenarioName { get; set; } = "";
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string DetectedCategory { get; set; } = "";
    public int CompletenessScore { get; set; }
    public bool IsActionable { get; set; }
    public int ExtractedFieldCount { get; set; }
    public List<string> HallucinationWarnings { get; set; } = new();
}
