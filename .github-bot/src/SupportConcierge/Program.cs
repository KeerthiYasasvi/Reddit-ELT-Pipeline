using SupportConcierge.Orchestration;
using System.Text.Json;

namespace SupportConcierge;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.WriteLine("=== GitHub Issues Support Concierge Bot ===");
            Console.WriteLine($"Started at: {DateTime.UtcNow:u}");

            // Validate required environment variables
            var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            var openaiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");

            if (string.IsNullOrEmpty(githubToken))
            {
                Console.WriteLine("ERROR: GITHUB_TOKEN environment variable is required");
                return 1;
            }

            if (string.IsNullOrEmpty(openaiApiKey))
            {
                Console.WriteLine("ERROR: OPENAI_API_KEY environment variable is required");
                return 1;
            }

            // For local testing, allow event path override via args
            if (args.Length > 0)
            {
                eventPath = args[0];
            }

            if (string.IsNullOrEmpty(eventPath) || !File.Exists(eventPath))
            {
                Console.WriteLine("ERROR: GITHUB_EVENT_PATH not found or invalid");
                return 1;
            }

            // Load GitHub event payload
            Console.WriteLine($"Loading event from: {eventPath}");
            var eventJson = await File.ReadAllTextAsync(eventPath);
            var eventPayload = JsonSerializer.Deserialize<JsonElement>(eventJson);

            // Extract event type
            var eventName = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME");
            Console.WriteLine($"Event type: {eventName}");

            // Initialize and run orchestrator
            var orchestrator = new Orchestrator();
            await orchestrator.ProcessEventAsync(eventName, eventPayload);

            Console.WriteLine($"Completed at: {DateTime.UtcNow:u}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
