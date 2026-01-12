using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SupportConcierge.GitHub;

public class GitHubApi
{
    private readonly HttpClient _httpClient;
    private readonly string _token;

    public GitHubApi(string token)
    {
        _token = token;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/")
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitHubSupportBot", "1.0"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<List<GitHubComment>> GetIssueCommentsAsync(string owner, string repo, int issueNumber)
    {
        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/comments";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<GitHubComment>>(json) ?? new List<GitHubComment>();
    }

    public async Task<GitHubComment> PostCommentAsync(string owner, string repo, int issueNumber, string body)
    {
        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/comments";
        var request = new CreateCommentRequest { Body = body };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GitHubComment>(responseJson) ?? new GitHubComment();
    }

    public async Task AddLabelsAsync(string owner, string repo, int issueNumber, List<string> labels)
    {
        if (labels.Count == 0) return;

        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/labels";
        var json = JsonSerializer.Serialize(new { labels });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddAssigneesAsync(string owner, string repo, int issueNumber, List<string> assignees)
    {
        if (assignees.Count == 0) return;

        var url = $"repos/{owner}/{repo}/issues/{issueNumber}/assignees";
        var json = JsonSerializer.Serialize(new { assignees });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> GetFileContentAsync(string owner, string repo, string path, string? branch = null)
    {
        try
        {
            var url = $"repos/{owner}/{repo}/contents/{path}";
            if (!string.IsNullOrEmpty(branch))
            {
                url += $"?ref={branch}";
            }

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return "";
            }

            var json = await response.Content.ReadAsStringAsync();
            var fileData = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (fileData.TryGetProperty("content", out var contentElement))
            {
                var base64Content = contentElement.GetString() ?? "";
                // Remove newlines from base64
                base64Content = base64Content.Replace("\n", "").Replace("\r", "");
                var bytes = Convert.FromBase64String(base64Content);
                return Encoding.UTF8.GetString(bytes);
            }

            return "";
        }
        catch
        {
            return "";
        }
    }

    public async Task<List<GitHubIssue>> SearchIssuesAsync(string owner, string repo, string query, int maxResults = 5)
    {
        try
        {
            var searchQuery = $"repo:{owner}/{repo} is:issue {query}";
            var encodedQuery = Uri.EscapeDataString(searchQuery);
            var url = $"search/issues?q={encodedQuery}&per_page={maxResults}&sort=created&order=desc";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return new List<GitHubIssue>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var searchResult = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (searchResult.TryGetProperty("items", out var itemsElement))
            {
                return JsonSerializer.Deserialize<List<GitHubIssue>>(itemsElement.GetRawText()) ?? new List<GitHubIssue>();
            }

            return new List<GitHubIssue>();
        }
        catch
        {
            return new List<GitHubIssue>();
        }
    }
}
