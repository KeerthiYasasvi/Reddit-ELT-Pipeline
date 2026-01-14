using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportConcierge.Agents;

/// <summary>
/// OpenAI client using direct HTTP API instead of SDK to avoid model parameter bug in OpenAI SDK v2.x
/// </summary>
public class OpenAiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private const string OpenAiApiUrl = "https://api.openai.com/v1/chat/completions";

    public OpenAiClient()
    {
        _apiKey = (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "")
            .Trim();
        
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY not set or empty");
        
        // Use gpt-4o model which supports response_format
        _model = "gpt-4o-2024-08-06";
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        Console.WriteLine($"Using OpenAI model: {_model}");
    }

    /// <summary>
    /// Make direct HTTP request to OpenAI API with full parameter control
    /// </summary>
    private async Task<string> CallOpenAiApiAsync(
        List<ChatMessage> messages,
        string schemaJson,
        string schemaName,
        int temperature = 0)
    {
        // Build request payload with explicit model parameter
        var requestBody = new
        {
            model = _model,  // Explicitly set model parameter
            messages = messages.Select(m => new
            {
                role = m.Role,
                content = m.Content
            }).ToList(),
            temperature = temperature,
            response_format = new
            {
                type = "json_object"
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            System.Text.Encoding.UTF8,
            "application/json"
        );

        try
        {
            var response = await _httpClient.PostAsync(OpenAiApiUrl, jsonContent);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"OpenAI API Error: {response.StatusCode}");
                Console.Error.WriteLine($"Response: {responseContent}");
                response.EnsureSuccessStatusCode();
            }

            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

            // Extract the assistant's response content
            if (jsonResponse.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? "";
                }
            }

            throw new InvalidOperationException("Unexpected response format from OpenAI API");
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"OpenAI API HTTP Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Classify issue category using structured output.
    /// </summary>
    public async Task<CategoryClassificationResult> ClassifyCategoryAsync(
        string issueTitle, 
        string issueBody, 
        List<string> categoryNames)
    {
        var categoriesText = string.Join("\n", categoryNames.Select(c => $"- {c}"));
        var prompt = Prompts.CategoryClassification(issueTitle, issueBody, categoriesText);

        // Build schema with enum for categories
        string schemaJson;
        if (categoryNames != null && categoryNames.Count > 0)
        {
            var enumValues = string.Join(", ", categoryNames.Select(c => $"\"{c}\""));
            schemaJson = "{\n" +
                "  \"type\": \"object\",\n" +
                "  \"properties\": {\n" +
                "    \"category\": {\n" +
                "      \"type\": \"string\",\n" +
                "      \"enum\": [" + enumValues + "]\n" +
                "    },\n" +
                "    \"confidence\": {\n" +
                "      \"type\": \"number\",\n" +
                "      \"minimum\": 0,\n" +
                "      \"maximum\": 1\n" +
                "    },\n" +
                "    \"reasoning\": {\n" +
                "      \"type\": \"string\"\n" +
                "    }\n" +
                "  },\n" +
                "  \"required\": [\"category\", \"confidence\", \"reasoning\"],\n" +
                "  \"additionalProperties\": false\n" +
            "}";
        }
        else
        {
            schemaJson = "{\n" +
                "  \"type\": \"object\",\n" +
                "  \"properties\": {\n" +
                "    \"category\": { \"type\": \"string\" },\n" +
                "    \"confidence\": { \"type\": \"number\", \"minimum\": 0, \"maximum\": 1 },\n" +
                "    \"reasoning\": { \"type\": \"string\" }\n" +
                "  },\n" +
                "  \"required\": [\"category\", \"confidence\", \"reasoning\"],\n" +
                "  \"additionalProperties\": false\n" +
            "}";
        }

        var messages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = "You are a precise issue classification assistant. Always respond with valid JSON matching the schema." },
            new ChatMessage { Role = "user", Content = prompt }
        };

        var content = await CallOpenAiApiAsync(messages, schemaJson, "category_classification");
        
        var result = JsonSerializer.Deserialize<CategoryClassificationResult>(content);

        // Fallback: choose first configured category (if available) to keep flow moving
        if (result == null || string.IsNullOrWhiteSpace(result.Category))
        {
            var fallbackCategory = (categoryNames != null && categoryNames.Count > 0) ? categoryNames[0] : "bug";
            return new CategoryClassificationResult { Category = fallbackCategory, Confidence = 0.5 };
        }

        return result;
    }

    /// <summary>
    /// Extract structured case packet from issue text.
    /// </summary>
    public async Task<Dictionary<string, string>> ExtractCasePacketAsync(
        string issueBody,
        string comments,
        List<string> requiredFields)
    {
        var fieldsText = string.Join(", ", requiredFields);
        var prompt = Prompts.ExtractCasePacket(issueBody, comments, fieldsText);

        var messages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = "You are a precise information extraction assistant. Extract only explicitly stated information." },
            new ChatMessage { Role = "user", Content = prompt }
        };

        var content = await CallOpenAiApiAsync(messages, Schemas.CasePacketExtractionSchema, "case_packet");
        
        var extracted = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content) 
            ?? new Dictionary<string, JsonElement>();

        // Convert to string dictionary, filtering out empty values
        var result = new Dictionary<string, string>();
        foreach (var kvp in extracted)
        {
            var value = kvp.Value.ValueKind == JsonValueKind.String 
                ? kvp.Value.GetString() ?? ""
                : kvp.Value.ToString();
            
            if (!string.IsNullOrWhiteSpace(value))
            {
                result[kvp.Key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Generate follow-up questions for missing fields.
    /// </summary>
    public async Task<List<FollowUpQuestion>> GenerateFollowUpQuestionsAsync(
        string issueBody,
        string category,
        List<string> missingFields,
        List<string> previouslyAskedFields)
    {
        var prompt = Prompts.GenerateFollowUpQuestions(
            issueBody, category, missingFields, previouslyAskedFields);

        var messages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = "You are a helpful support bot that asks clear, targeted questions." },
            new ChatMessage { Role = "user", Content = prompt }
        };

        var content = await CallOpenAiApiAsync(messages, Schemas.FollowUpQuestionsSchema, "follow_up_questions");
        
        var result = JsonSerializer.Deserialize<FollowUpQuestionsResponse>(content);
        return result?.Questions ?? new List<FollowUpQuestion>();
    }

    /// <summary>
    /// Generate engineer-ready brief with structured output.
    /// </summary>
    public async Task<EngineerBrief> GenerateEngineerBriefAsync(
        string issueBody,
        string comments,
        string category,
        Dictionary<string, string> extractedFields,
        string playbook,
        string repoDocs,
        List<(int number, string title)> duplicates)
    {
        var duplicatesText = duplicates.Count > 0
            ? string.Join("\n", duplicates.Select(d => $"#{d.number}: {d.title}"))
            : "";

        var prompt = Prompts.GenerateEngineerBrief(
            issueBody, comments, category, extractedFields, playbook, repoDocs, duplicatesText);

        var messages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = "You are an expert technical support engineer creating actionable case briefs." },
            new ChatMessage { Role = "user", Content = prompt }
        };

        var content = await CallOpenAiApiAsync(messages, Schemas.EngineerBriefSchema, "engineer_brief");
        
        Console.WriteLine($"Engineer Brief raw response:\n{content}\n");
        
        try
        {
            return JsonSerializer.Deserialize<EngineerBrief>(content) 
                ?? new EngineerBrief();
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Error deserializing engineer brief JSON: {ex.Message}");
            Console.WriteLine($"Raw response content:\n{content}");
            
            // Try to parse with lenient settings - ignore possible_duplicates errors
            try
            {
                // Remove the problematic field and try again
                var parsed = JsonSerializer.Deserialize<JsonElement>(content);
                if (parsed.ValueKind == JsonValueKind.Object)
                {
                    // Create a clean brief from what we can parse
                    var brief = new EngineerBrief();
                    
                    if (parsed.TryGetProperty("summary", out var summary))
                        brief.Summary = summary.GetString() ?? "";
                    
                    if (parsed.TryGetProperty("symptoms", out var symptoms) && symptoms.ValueKind == JsonValueKind.Array)
                        brief.Symptoms = symptoms.EnumerateArray()
                            .Select(s => s.GetString() ?? "")
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                    
                    if (parsed.TryGetProperty("repro_steps", out var repro) && repro.ValueKind == JsonValueKind.Array)
                        brief.Repro_Steps = repro.EnumerateArray()
                            .Select(s => s.GetString() ?? "")
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                    
                    if (parsed.TryGetProperty("environment", out var env) && env.ValueKind == JsonValueKind.Object)
                    {
                        brief.Environment = env.EnumerateObject()
                            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                    }
                    
                    if (parsed.TryGetProperty("key_evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Array)
                        brief.Key_Evidence = evidence.EnumerateArray()
                            .Select(s => s.GetString() ?? "")
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                    
                    if (parsed.TryGetProperty("next_steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
                        brief.Next_Steps = steps.EnumerateArray()
                            .Select(s => s.GetString() ?? "")
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                    
                    // Skip possible_duplicates - it's optional
                    
                    return brief;
                }
            }
            catch (Exception innerEx)
            {
                Console.WriteLine($"Error in lenient parsing: {innerEx.Message}");
            }
            
            // Return default brief with error indication
            return new EngineerBrief
            {
                Summary = "Error processing issue data - please check the response format",
                Symptoms = new List<string> { "JSON parsing error" },
                Repro_Steps = new List<string>(),
                Environment = new Dictionary<string, string>(),
                Key_Evidence = new List<string> { ex.Message },
                Next_Steps = new List<string> { "Please review the issue details and resubmit" },
                Possible_Duplicates = new List<DuplicateReference>()
            };
        }
    }

    /// <summary>
    /// Regenerate engineer brief based on user feedback (Scenario 7).
    /// </summary>
    public async Task<EngineerBrief> RegenerateEngineerBriefAsync(
        string previousBrief,
        string userFeedback,
        Dictionary<string, string> extractedFields,
        string playbook,
        string category)
    {
        var prompt = Prompts.RegenerateEngineerBriefWithFeedback(
            previousBrief, userFeedback, extractedFields, playbook, category);

        var messages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = "You are an expert technical support engineer revising case briefs based on user feedback." },
            new ChatMessage { Role = "user", Content = prompt }
        };

        var content = await CallOpenAiApiAsync(messages, Schemas.EngineerBriefSchema, "engineer_brief");
        
        Console.WriteLine($"Regenerated Engineer Brief raw response:\n{content}\n");
        
        return JsonSerializer.Deserialize<EngineerBrief>(content) 
            ?? new EngineerBrief();
    }
}


/// <summary>
/// Message model for direct HTTP API calls
/// </summary>
public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

// Response models
public class CategoryClassificationResult
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";
}

public class FollowUpQuestionsResponse
{
    [JsonPropertyName("questions")]
    public List<FollowUpQuestion> Questions { get; set; } = new();
}

public class FollowUpQuestion
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("why_needed")]
    public string Why_Needed { get; set; } = "";
}

public class EngineerBrief
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("symptoms")]
    public List<string> Symptoms { get; set; } = new();

    [JsonPropertyName("repro_steps")]
    public List<string> Repro_Steps { get; set; } = new();

    [JsonPropertyName("environment")]
    public Dictionary<string, string> Environment { get; set; } = new();

    [JsonPropertyName("key_evidence")]
    public List<string> Key_Evidence { get; set; } = new();

    [JsonPropertyName("next_steps")]
    public List<string> Next_Steps { get; set; } = new();

    [JsonPropertyName("validation_confirmations")]
    public List<string> Validation_Confirmations { get; set; } = new();

    [JsonPropertyName("possible_duplicates")]
    public List<DuplicateReference> Possible_Duplicates { get; set; } = new();
}

public class DuplicateReference
{
    [JsonPropertyName("issue_number")]
    public int Issue_Number { get; set; }

    [JsonPropertyName("similarity_reason")]
    public string Similarity_Reason { get; set; } = "";
}
