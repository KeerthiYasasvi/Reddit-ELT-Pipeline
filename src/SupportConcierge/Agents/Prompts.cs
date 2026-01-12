namespace SupportConcierge.Agents;

public static class Prompts
{
    public static string CategoryClassification(string issueTitle, string issueBody, string categories)
    {
        return $@"You are a GitHub issue triage assistant. Classify this issue into one of the following categories:

{categories}

Issue Title: {issueTitle}

Issue Body:
{issueBody}

Return your classification with confidence score (0-1) and brief reasoning.";
    }

    public static string ExtractCasePacket(string issueBody, string comments, string requiredFields)
    {
        return $@"You are a precise information extraction assistant. Extract structured fields from this GitHub issue.

Required fields to extract (extract ONLY what is explicitly present, leave fields empty if not found):
{requiredFields}

Issue Body:
{issueBody}

Additional Comments:
{comments}

Extract the information into the provided schema. Use exact text from the issue/comments. If a field is not present or unclear, leave it as an empty string. Do not invent or infer information that isn't explicitly stated.";
    }

    public static string GenerateFollowUpQuestions(
        string issueBody, 
        string category,
        List<string> missingFields,
        List<string> askedBefore)
    {
        var askedList = askedBefore.Count > 0 
            ? $"\n\nFields already asked about (do NOT ask again): {string.Join(", ", askedBefore)}"
            : "";

        return $@"You are a helpful GitHub support bot. The user has submitted a {category} issue, but it's missing critical information.

Issue so far:
{issueBody}

Missing fields that need to be collected: {string.Join(", ", missingFields)}{askedList}

Generate up to 3 targeted, friendly follow-up questions to gather the missing information. Be specific about what format you need (e.g., ""full error message including stack trace"", ""exact version number""). Make questions concise and actionable.

IMPORTANT: 
- Never ask for passwords, API keys, tokens, or secrets
- Focus on diagnostic information only
- Be friendly and respectful";
    }

    public static string GenerateEngineerBrief(
        string issueBody,
        string comments,
        string category,
        Dictionary<string, string> extractedFields,
        string playbook,
        string repoDocs,
        string duplicateIssues)
    {
        var fieldsText = string.Join("\n", extractedFields.Select(kvp => $"- {kvp.Key}: {kvp.Value}"));
        var duplicatesSection = !string.IsNullOrEmpty(duplicateIssues)
            ? $@"

Potentially Related Issues:
{duplicateIssues}"
            : "";

        return $@"You are an expert technical support engineer. Create a concise, actionable brief for engineers to investigate this {category} issue.

Original Issue:
{issueBody}

Additional Information from Follow-ups:
{comments}

Extracted Fields:
{fieldsText}

Relevant Playbook Guidance:
{playbook}

Repository Documentation Context:
{repoDocs}{duplicatesSection}

Generate a comprehensive engineer brief with:
1. One-sentence summary
2. Key symptoms
3. Reproduction steps (if available)
4. Environment details
5. Critical evidence snippets (keep short - max 2-3 lines per snippet)
6. Suggested next steps (grounded in the playbook and repo docs - do NOT suggest steps that contradict repo documentation)

IMPORTANT:
- Base next_steps ONLY on the provided playbook and documentation
- Do NOT invent commands, file paths, or procedures not mentioned in the context
- If duplicate issues are provided, mention them in possible_duplicates
- Keep evidence snippets short and relevant
- Be factual and precise";
    }
}
