# 🏗️ System Architecture

This document explains how the GitHub Issues Support Bot is architected.

---

## High-Level Flow

```
┌─────────────────────────────────────────────────────────┐
│ GitHub Event: Issue Opened or Comment Created           │
└────────────┬────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────┐
│ GitHub Actions Workflow Triggered                       │
│ (.github-bot/.github/workflows/support-bot.yml)        │
└────────────┬────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────┐
│ SupportConcierge Application (Program.cs)              │
│ ├─ Fetch Issue Context                                 │
│ ├─ Parse Issue Form                                    │
│ └─ Extract Previous State                              │
└────────────┬────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────┐
│ Orchestrator.cs - Main Logic                            │
│                                                          │
│ 1. Load State from HTML Comments                        │
│ 2. Analyze Issue Content                                │
│ 3. Score Completeness                                   │
│ 4. Generate Questions via OpenAI                        │
│ 5. Manage Follow-Up Loop                                │
│ 6. Finalize When Threshold Reached                      │
└────────────┬────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────┐
│ CommentComposer.cs - Format Response                    │
│ ├─ Build Markdown Response                              │
│ ├─ Embed State in HTML Comment                          │
│ └─ Apply Auto-Labels & Assignments                      │
└────────────┬────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────┐
│ GitHubApi.cs - Post Results                             │
│ ├─ Post Comment                                         │
│ ├─ Add Labels                                           │
│ └─ Assign Issue                                         │
└────────────┬────────────────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────────────────┐
│ GitHub Issue Updated                                    │
│ ├─ Bot Comment Posted                                   │
│ ├─ Labels Applied                                       │
│ └─ Assigned to Team Member                              │
└─────────────────────────────────────────────────────────┘
```

---

## Component Architecture

### 1. Program.cs (Entry Point)

**Responsibility:** Bootstrap and orchestrate the flow

```csharp
- Initialize services
- Load configuration
- Parse GitHub webhook
- Call Orchestrator
- Handle errors
```

**Key Classes:**
- `Program` - Entry point with Main()

---

### 2. Orchestrator.cs (Main Business Logic)

**Responsibility:** Coordinate all bot operations

```csharp
public class Orchestrator
{
    // Load previous state from issue comments
    - ExtractStateFromIssue()
    
    // Analyze issue completeness
    - AnalyzeIssueCompleteness()
    
    // Generate bot response
    - GenerateResponse()
    
    // Manage multi-round follow-ups
    - ManageFollowUpLoop()
    
    // Finalize issue
    - FinalizeIssue()
    
    // Save state back to issue
    - PersistState()
}
```

**Flow:**
1. Load state from last bot comment (if exists)
2. Analyze issue content with validators
3. Check if issue is complete enough
4. If complete: Generate final summary
5. If incomplete: Ask follow-up questions
6. Save new state in HTML comment

---

### 3. StateStore.cs (State Persistence)

**Responsibility:** Manage bot state lifecycle

**Key Features:**
- **Thread-local state**: Each issue has independent state
- **Auto-compression**: States >5KB automatically compressed with GZip
- **Size monitoring**: Warns when approaching GitHub's 65KB limit
- **Smart pruning**: Keeps only 20 most recent asked fields

```csharp
public class StateStore
{
    // Extract state from HTML comment
    - ExtractState(string htmlComment)
    
    // Compress large states
    - CompressString(string data)
    - DecompressString(string data)
    
    // Prune old questions
    - PruneState(BotState state)
    
    // Embed state back in comment
    - EmbedState(BotState state)
}
```

**State Format:**
```json
{
  "Category": "postgres_database",
  "LoopCount": 1,
  "AskedFields": ["stack_trace", "connection_string"],
  "LastUpdated": "2026-01-14T14:19:51Z",
  "IsActionable": true,
  "CompletenessScore": 85,
  "IssueAuthor": "username",
  "IsFinalized": false,
  "FinalizedAt": null,
  "EngineerBriefCommentId": null,
  "BriefIterationCount": 0
}
```

---

### 4. OpenAiClient.cs (LLM Integration)

**Responsibility:** Interact with OpenAI API

```csharp
public class OpenAiClient
{
    // Analyze issue content
    - AnalyzeIssue(string content)
    
    // Score completeness
    - ScoreCompleteness(Issue issue)
    
    // Generate questions
    - GenerateFollowUpQuestions()
    
    // Create final summary
    - GenerateFinalSummary()
}
```

**LLM Calls:**
- Uses GPT-4 for analysis
- Structured JSON output with schema validation
- Temperature: 0.7 (balanced creativity)
- Token limit: ~8K context window

---

### 5. IssueFormParser.cs (Issue Parsing)

**Responsibility:** Extract structured data from GitHub issues

```csharp
public class IssueFormParser
{
    // Parse GitHub issue form fields
    - ParseIssueForm(Issue issue)
    
    // Extract environment info
    - ExtractEnvironment()
    
    // Extract error details
    - ExtractErrorDetails()
    
    // Extract reproduction steps
    - ExtractReproductionSteps()
}
```

**Handles:**
- YAML issue forms
- Markdown free-form issues
- GitHub issue templates
- Edge cases and malformed input

---

### 6. CommentComposer.cs (Response Formatting)

**Responsibility:** Format and compose bot responses

```csharp
public class CommentComposer
{
    // Build markdown response
    - ComposeFollowUpComment()
    - ComposeFinalComment()
    
    // Add state embedding
    - EmbedState(BotState state)
    
    // Suggest labels and assignments
    - SuggestLabels()
    - SuggestAssignee()
}
```

**Response Includes:**
- Formatted questions (markdown)
- Embedded state in HTML comment
- Quick commands (/stop, /diagnose)
- Suggested labels and assignment

---

### 7. CompletenessScorer.cs (Issue Analysis)

**Responsibility:** Score issue quality

```csharp
public class CompletenessScorer
{
    // Check required fields
    - CheckRequiredFields()
    
    // Validate field content
    - ValidateFieldContent()
    
    // Calculate score
    - CalculateScore(Issue issue)
    
    // Determine if actionable
    - IsActionable(Issue issue)
}
```

**Scoring Factors:**
- Environment details (20%)
- Error message (30%)
- Reproduction steps (25%)
- Attempted solutions (15%)
- Additional context (10%)

**Thresholds:**
- Actionable: ≥70/100
- Finalized: ≥70/100 + 1 follow-up round

---

### 8. SecretRedactor.cs (Security)

**Responsibility:** Redact sensitive data

```csharp
public class SecretRedactor
{
    // Detect and redact secrets
    - RedactSecrets(string content)
    
    // Pattern matching for:
    // - API keys
    // - Database passwords
    // - Private tokens
    // - Email addresses
    // - IP addresses
}
```

**Redaction:**
- Logs warnings about detected secrets
- Never processes secrets through LLM
- Suggests users sanitize sensitive info

---

### 9. GitHubApi.cs (GitHub Integration)

**Responsibility:** Interact with GitHub API

```csharp
public class GitHubApi
{
    // Fetch issue details
    - GetIssue(int number)
    
    // Post comment
    - PostComment(int number, string body)
    
    // Update issue
    - UpdateIssue(int number, IssueUpdate update)
    
    // Add labels
    - AddLabel(int number, string[] labels)
    
    // Assign issue
    - AssignIssue(int number, string username)
}
```

**Uses:**
- Octokit library (.NET GitHub API client)
- GitHub API v3
- GITHUB_TOKEN for authentication

---

## Data Flow: From Issue to Resolution

### Round 1: Initial Analysis

```
GitHub Issue Created
    ↓
Fetch Issue Content
    ↓
Parse Form Fields
    ↓
Load Previous State (none on first run)
    ↓
Analyze with OpenAI
    ├─ Category Detection
    ├─ Completeness Scoring
    ├─ Actionability Check
    └─ Required Fields Analysis
    ↓
Score < 70?
    ├─ YES → Generate Follow-Up Questions
    │          └─ State: LoopCount=1, AskedFields=[...]
    └─ NO → Generate Final Summary
             └─ State: IsFinalized=true
    ↓
Post Comment with Embedded State
    ↓
Add Labels & Assign
```

### Round 2+: Follow-Up Processing

```
User Replies to Bot
    ↓
Fetch Issue & All Comments
    ↓
Extract Previous State from Last Bot Comment
    ├─ LoopCount = 1
    └─ AskedFields = [previous questions]
    ↓
Parse User's Answers
    ↓
Re-analyze with New Information
    ├─ Check which fields now have answers
    ├─ Recalculate completeness score
    └─ Check if threshold reached
    ↓
Score ≥ 70?
    ├─ YES & LoopCount ≥ 3 → Finalize Issue
    │                         └─ State: IsFinalized=true
    ├─ YES & LoopCount < 3 → Ask More Questions
    │                         └─ State: LoopCount=2
    └─ NO → Ask More Questions
             └─ State: LoopCount++
    ↓
Prune State (keep recent 20 asked fields)
    ↓
Post Comment with Updated State
```

---

## State Lifecycle

### 1. Creation (First Response)
```json
{
  "LoopCount": 1,
  "AskedFields": ["field1", "field2"],
  "IsFinalized": false
}
```

### 2. Evolution (Follow-Up Rounds)
```json
{
  "LoopCount": 2,
  "AskedFields": ["field1", "field2", "field3", "field4"],
  "CompletenessScore": 85,
  "IsFinalized": false
}
```

### 3. Finalization (Threshold Reached)
```json
{
  "LoopCount": 2,
  "AskedFields": ["field1", "field2", "field3"],
  "CompletenessScore": 92,
  "IsFinalized": true,
  "FinalizedAt": "2026-01-14T14:26:24Z"
}
```

### 4. Compression (State >5KB)
```
Original: {"LoopCount":1,"AskedFields":[...]}
    ↓ (GZip + Base64)
Compressed: compressed:H4sIAAAAAAAAA...AAQAA
```

---

## Concurrency & Safety

### GitHub Actions Concurrency

- **Issue Operations**: Sequential (GitHub locks during processing)
- **Multiple Issues**: Parallel (each GitHub Actions job independent)
- **Rate Limiting**: Respects GitHub API rate limits (5000 req/hour)

### State Safety

- **Optimistic Locking**: Each state includes timestamp
- **Last-Write-Wins**: Last bot comment's state wins
- **Idempotent Operations**: Safe to retry failed runs

### API Safety

- **Retries**: 3 retries on transient failures
- **Timeouts**: 30 second timeout per API call
- **Error Handling**: Graceful degradation on failures

---

## Performance Characteristics

### Response Time

- **Initial Comment**: ~15-30 seconds
- **Follow-Up Comment**: ~10-20 seconds
- (Includes GitHub Actions startup + OpenAI API latency)

### State Size

- **Typical State**: 300-500 bytes
- **With 3 Rounds**: 1-2 KB
- **Compression Threshold**: >5 KB
- **Compression Ratio**: 60-70% reduction

### API Costs

- **Per Issue**: $0.01-0.05 (depending on conversation length)
- **Per 100 Issues**: $1-5
- **Monthly (1000 issues)**: $10-50

---

## Deployment

### GitHub Actions Execution

```yaml
# Triggered on:
on:
  issues:
    types: [opened, reopened]
  issue_comment:
    types: [created]

# Runs in:
- Ubuntu Latest
- .NET 8 runtime
- 15-minute timeout per run
```

### Environment Variables

```
GITHUB_TOKEN = auto-provided by GitHub
OPENAI_API_KEY = set in repository secrets
GITHUB_REPOSITORY = auto-provided by GitHub
GITHUB_EVENT_PATH = webhook payload
```

### Artifact Handling

- No persistent storage needed
- State stored in GitHub issues
- Logs retained by GitHub Actions (90 days default)
- No external databases required

---

## Security Considerations

### Data Privacy

- ✅ Issue content sent to OpenAI (check their privacy policy)
- ✅ State stored in GitHub (encrypted at rest)
- ✅ No data persisted externally
- ❌ Don't include passwords or secrets in issues

### Access Control

- ✅ GITHUB_TOKEN scoped to repository
- ✅ OPENAI_API_KEY stored in GitHub Secrets
- ✅ Only repository writers can see Secrets
- ✅ Workflow logs private to collaborators

### Secret Detection

- ✅ Bot detects common secret patterns
- ✅ Logs warnings for detected secrets
- ✅ Never sends secrets to LLM
- ✅ Recommends user sanitization

---

## Extensibility Points

### Custom Configurations

- **config/categories.yaml** - Add issue types
- **config/routing.yaml** - Add routing rules
- **config/checklists.yaml** - Add questions

### Custom Code

- **OpenAiClient.cs** - Swap LLM provider
- **Validators.cs** - Add custom validation
- **CommentComposer.cs** - Customize response format

### Integration Points

- **GitHub API** - Already integrated
- **External Services** - Add via Orchestrator
- **Monitoring** - Add via Program.cs logging

---

For more details, see [CONFIGURATION.md](CONFIGURATION.md) or [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
