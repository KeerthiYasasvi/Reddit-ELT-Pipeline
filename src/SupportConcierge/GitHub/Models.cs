using System.Text.Json.Serialization;

namespace SupportConcierge.GitHub;

public class GitHubIssue
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("user")]
    public GitHubUser User { get; set; } = new();

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("labels")]
    public List<GitHubLabel> Labels { get; set; } = new();

    [JsonPropertyName("assignees")]
    public List<GitHubUser> Assignees { get; set; } = new();

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
}

public class GitHubComment
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("user")]
    public GitHubUser User { get; set; } = new();

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
}

public class GitHubUser
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

public class GitHubLabel
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class GitHubRepository
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("owner")]
    public GitHubUser Owner { get; set; } = new();

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; set; } = "main";
}

public class CreateCommentRequest
{
    [JsonPropertyName("body")]
    public string Body { get; set; } = "";
}

public class UpdateIssueRequest
{
    [JsonPropertyName("labels")]
    public List<string>? Labels { get; set; }

    [JsonPropertyName("assignees")]
    public List<string>? Assignees { get; set; }
}
