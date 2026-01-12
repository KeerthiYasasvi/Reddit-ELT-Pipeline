using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

namespace SupportConcierge.Agents;

public class OpenAiClient
{
    private readonly ChatClient _client;
    private readonly string _model;

    public OpenAiClient()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? throw new InvalidOperationException("OPENAI_API_KEY not set");
        
        _model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-2024-08-06";
        
        var openAiClient = new OpenAIClient(apiKey);
        _client = openAiClient.GetChatClient(_model);
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

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "category_classification",
                BinaryData.FromString(Schemas.CategoryClassificationSchema),
                jsonSchemaIsStrict: true
            )
        };

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a precise issue classification assistant. Always respond with valid JSON matching the schema."),
            new UserChatMessage(prompt)
        };

        var response = await _client.CompleteChatAsync(messages, options);
        var content = response.Value.Content[0].Text;
        
        return JsonSerializer.Deserialize<CategoryClassificationResult>(content) 
            ?? new CategoryClassificationResult { Category = categoryNames[0], Confidence = 0.5 };
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

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "case_packet",
                BinaryData.FromString(Schemas.CasePacketExtractionSchema),
                jsonSchemaIsStrict: true
            )
        };

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a precise information extraction assistant. Extract only explicitly stated information."),
            new UserChatMessage(prompt)
        };

        var response = await _client.CompleteChatAsync(messages, options);
        var content = response.Value.Content[0].Text;
        
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

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "follow_up_questions",
                BinaryData.FromString(Schemas.FollowUpQuestionsSchema),
                jsonSchemaIsStrict: true
            )
        };

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful support bot that asks clear, targeted questions."),
            new UserChatMessage(prompt)
        };

        var response = await _client.CompleteChatAsync(messages, options);
        var content = response.Value.Content[0].Text;
        
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

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "engineer_brief",
                BinaryData.FromString(Schemas.EngineerBriefSchema),
                jsonSchemaIsStrict: true
            )
        };

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an expert technical support engineer creating actionable case briefs."),
            new UserChatMessage(prompt)
        };

        var response = await _client.CompleteChatAsync(messages, options);
        var content = response.Value.Content[0].Text;
        
        return JsonSerializer.Deserialize<EngineerBrief>(content) 
            ?? new EngineerBrief();
    }
}

// Response models
public class CategoryClassificationResult
{
    public string Category { get; set; } = "";
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = "";
}

public class FollowUpQuestionsResponse
{
    public List<FollowUpQuestion> Questions { get; set; } = new();
}

public class FollowUpQuestion
{
    public string Field { get; set; } = "";
    public string Question { get; set; } = "";
    public string Why_Needed { get; set; } = "";
}

public class EngineerBrief
{
    public string Summary { get; set; } = "";
    public List<string> Symptoms { get; set; } = new();
    public List<string> Repro_Steps { get; set; } = new();
    public Dictionary<string, string> Environment { get; set; } = new();
    public List<string> Key_Evidence { get; set; } = new();
    public List<string> Next_Steps { get; set; } = new();
    public List<DuplicateReference> Possible_Duplicates { get; set; } = new();
}

public class DuplicateReference
{
    public int Issue_Number { get; set; }
    public string Similarity_Reason { get; set; } = "";
}
