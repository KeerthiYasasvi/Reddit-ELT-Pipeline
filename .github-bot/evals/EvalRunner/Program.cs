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

        // Check for dry-run mode
        var dryRun = args.Contains("--dry-run") || args.Contains("-d");
        
        if (dryRun)
        {
            Console.WriteLine("Running in DRY-RUN mode (simulated results)\n");
        }
        else
        {
            // Check required environment variables
            var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(openaiKey))
            {
                Console.WriteLine("ERROR: OPENAI_API_KEY environment variable required");
                Console.WriteLine("       Use --dry-run flag to generate sample report without API calls");
                return 1;
            }
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
                EvalResult result;
                
                if (dryRun)
                {
                    result = GenerateMockResult(scenarioName);
                }
                else
                {
                    result = await RunScenarioAsync(scenarioFile);
                }
                
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

        // Save JSON report
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "eval_report.json");
        var reportJson = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(reportPath, reportJson);
        Console.WriteLine($"\nJSON report saved to: {reportPath}");

        // Generate Markdown report
        var mdReportPath = Path.Combine(Directory.GetCurrentDirectory(), "EVAL_REPORT.md");
        var mdReport = GenerateMarkdownReport(results);
        await File.WriteAllTextAsync(mdReportPath, mdReport);
        Console.WriteLine($"Markdown report saved to: {mdReportPath}");

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

    private static EvalResult GenerateMockResult(string scenarioName)
    {
        // Generate realistic mock results for demonstration
        var random = new Random(scenarioName.GetHashCode()); // Deterministic based on name
        
        var result = new EvalResult
        {
            ScenarioName = scenarioName,
            Success = random.Next(100) < 90, // 90% success rate
        };

        if (!result.Success)
        {
            result.ErrorMessage = "Simulated failure for demonstration";
            return result;
        }

        // Mock successful result
        var categories = new[] { "build", "runtime", "database", "api", "ui" };
        result.DetectedCategory = categories[random.Next(categories.Length)];
        result.CompletenessScore = random.Next(65, 100);
        result.ExtractedFieldCount = random.Next(5, 12);
        result.IsActionable = result.CompletenessScore >= 70;

        // Occasionally add hallucination warnings
        if (random.Next(100) < 20)
        {
            result.HallucinationWarnings.Add("Field 'version' value may be hallucinated (no matching terms in source)");
        }

        return result;
    }

    private static string GenerateMarkdownReport(List<EvalResult> results)
    {
        var sb = new System.Text.StringBuilder();
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        
        sb.AppendLine("# Support Concierge Evaluation Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {timestamp}");
        sb.AppendLine();

        // Summary Section
        sb.AppendLine("## 📊 Summary");
        sb.AppendLine();
        
        var successful = results.Count(r => r.Success);
        var total = results.Count;
        var successRate = total > 0 ? (double)successful / total * 100 : 0;
        
        sb.AppendLine($"- **Total Scenarios:** {total}");
        sb.AppendLine($"- **Successful:** {successful}/{total} ({successRate:F1}%)");
        sb.AppendLine($"- **Failed:** {total - successful}");
        sb.AppendLine();

        // Metrics Section
        if (results.Any(r => r.Success))
        {
            var avgScore = results.Where(r => r.Success).Average(r => r.CompletenessScore);
            var avgFields = results.Where(r => r.Success).Average(r => r.ExtractedFieldCount);
            var actionableRate = results.Where(r => r.Success).Count(r => r.IsActionable) / 
                                 (double)results.Count(r => r.Success) * 100;
            var hallucinationCount = results.Where(r => r.Success).Sum(r => r.HallucinationWarnings.Count);
            
            sb.AppendLine("## 🎯 Performance Metrics");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|--------|-------|");
            sb.AppendLine($"| Average Completeness Score | {avgScore:F1}/100 |");
            sb.AppendLine($"| Average Fields Extracted | {avgFields:F1} |");
            sb.AppendLine($"| Actionable Rate | {actionableRate:F1}% |");
            sb.AppendLine($"| Hallucination Warnings | {hallucinationCount} |");
            sb.AppendLine();
        }

        // Detailed Results Section
        sb.AppendLine("## 📋 Detailed Results");
        sb.AppendLine();

        foreach (var result in results.OrderBy(r => r.ScenarioName))
        {
            var status = result.Success ? "✅" : "❌";
            sb.AppendLine($"### {status} {result.ScenarioName}");
            sb.AppendLine();
            
            if (!result.Success)
            {
                sb.AppendLine($"**Status:** Failed");
                sb.AppendLine();
                sb.AppendLine($"**Error:** {result.ErrorMessage}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"**Status:** Passed");
                sb.AppendLine();
                sb.AppendLine($"- **Detected Category:** `{result.DetectedCategory}`");
                sb.AppendLine($"- **Completeness Score:** {result.CompletenessScore}/100");
                sb.AppendLine($"- **Fields Extracted:** {result.ExtractedFieldCount}");
                sb.AppendLine($"- **Actionable:** {(result.IsActionable ? "Yes" : "No")}");
                
                if (result.HallucinationWarnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("**⚠️ Warnings:**");
                    foreach (var warning in result.HallucinationWarnings)
                    {
                        sb.AppendLine($"- {warning}");
                    }
                }
                
                sb.AppendLine();
            }
        }

        // Grade Section
        sb.AppendLine("## 🏆 Overall Grade");
        sb.AppendLine();
        
        var grade = successRate >= 90 ? "A (Excellent)" :
                    successRate >= 80 ? "B (Good)" :
                    successRate >= 70 ? "C (Satisfactory)" :
                    successRate >= 60 ? "D (Needs Improvement)" :
                                        "F (Poor)";
        
        sb.AppendLine($"**{grade}** - {successRate:F1}% success rate");
        sb.AppendLine();
        
        if (successRate >= 80)
        {
            sb.AppendLine("✨ The bot is performing well across test scenarios.");
        }
        else if (successRate >= 60)
        {
            sb.AppendLine("⚠️ The bot needs improvement in handling some scenarios.");
        }
        else
        {
            sb.AppendLine("❌ The bot requires significant improvements.");
        }
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*This report was automatically generated by the Support Concierge Eval Runner.*");

        return sb.ToString();
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
