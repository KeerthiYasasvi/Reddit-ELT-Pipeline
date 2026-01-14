# ⚙️ Configuration Guide

This guide explains how to configure the GitHub Issues Support Bot to fit your project's needs.

---

## Configuration Files Overview

All bot configuration lives in `.github-bot/config/supportbot/`:

```
.github-bot/config/supportbot/
├── categories.yaml    ← Define issue categories
├── routing.yaml       ← Routing and assignment rules
├── checklists.yaml    ← Questions for each category
├── validators.yaml    ← Validation rules
└── playbooks/         ← Team response templates
    ├── build.md
    ├── docs.md
    └── runtime.md
```

---

## categories.yaml

Defines the issue categories your bot recognizes.

### Example

```yaml
categories:
  - id: "database_connection"
    name: "Database Connection Issues"
    keywords:
      - "connection"
      - "timeout"
      - "database"
      - "postgresql"
    priority: "high"
    assignee: "database-team"

  - id: "api_error"
    name: "API Error"
    keywords:
      - "api"
      - "request"
      - "endpoint"
      - "error"
    priority: "medium"
    assignee: "api-team"

  - id: "documentation"
    name: "Documentation Request"
    keywords:
      - "docs"
      - "documentation"
      - "example"
      - "tutorial"
    priority: "low"
    assignee: "docs-team"
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique category identifier |
| `name` | string | Human-readable category name |
| `keywords` | array | Keywords that match this category |
| `priority` | string | Priority level: `high`, `medium`, `low` |
| `assignee` | string | GitHub team or username to assign |

---

## checklists.yaml

Defines questions to ask for each category type.

### Example

```yaml
checklists:
  database_connection:
    name: "Database Connection Troubleshooting"
    questions:
      - field: "error_message"
        question: "What is the exact error message you received?"
        required: true
        priority: "high"

      - field: "connection_string"
        question: "What connection string are you using? (sanitize passwords)"
        required: true
        priority: "high"

      - field: "environment"
        question: "What is your environment (dev/staging/prod)?"
        required: true
        priority: "medium"

      - field: "database_version"
        question: "Which PostgreSQL version are you using?"
        required: false
        priority: "low"

  api_error:
    name: "API Error Troubleshooting"
    questions:
      - field: "endpoint"
        question: "Which API endpoint are you calling?"
        required: true
        priority: "high"

      - field: "http_method"
        question: "What HTTP method (GET, POST, etc)?"
        required: true
        priority: "high"

      - field: "error_response"
        question: "What was the error response?"
        required: true
        priority: "high"
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `field` | string | Unique field identifier for this question |
| `question` | string | The question to ask users |
| `required` | boolean | If this field is required for completeness |
| `priority` | string | Priority: `high`, `medium`, `low` |

---

## routing.yaml

Defines routing rules for automatic assignment and labeling.

### Example

```yaml
routing:
  labels:
    - pattern: "connection.*timeout"
      tags:
        - "component:database"
        - "type:bug"
        - "priority:high"

    - pattern: ".*api.*error"
      tags:
        - "component:api"
        - "type:bug"

    - pattern: "how.*to|tutorial|example"
      tags:
        - "component:docs"
        - "type:question"

  assignments:
    - pattern: "connection.*database"
      assignees:
        - "alice"          # Primary
        - "bob"            # Fallback
      rotate: true         # Round-robin assignment

    - pattern: "api.*error"
      assignees:
        - "carlos"

    - pattern: "documentation"
      team: "docs-team"
```

### Pattern Matching

Patterns use regex matching against:
- Issue title
- Issue body (first 500 chars)
- Issue labels (if already present)

### Round-Robin Assignment

When `rotate: true`, assignments cycle through users:
- Issue 1 → alice
- Issue 2 → bob
- Issue 3 → alice
- etc.

---

## validators.yaml

Defines validation rules for determining issue completeness.

### Example

```yaml
validators:
  required_fields:
    # At least one of these must be present
    error_indicators:
      - "error"
      - "exception"
      - "crash"
      - "failed"
    weight: 20

  environment_info:
    # Should include environment details
    patterns:
      - "os|windows|linux|mac"
      - "version|\\d+\\.\\d+"
      - "environment|dev|prod|staging"
    weight: 20

  reproduction_steps:
    # Should explain how to reproduce
    patterns:
      - "step|reproduce|follow|do|run"
    weight: 25

  logs_or_traces:
    # Should include error logs or stack trace
    patterns:
      - "stack trace|traceback|log|debug"
      - "at .*\\.\\w+|File.*line \\d+"
    weight: 15

  attempted_solutions:
    # Should mention what was already tried
    patterns:
      - "tried|attempted|already|checked"
    weight: 20

completeness_threshold: 70  # Minimum score to consider actionable
```

### Scoring

Each validator is weighted. Completeness score = sum of matched weights.

**Example Calculation:**
- Has error message ✓ (20 pts)
- Has environment ✓ (20 pts)
- Has reproduction steps ✓ (25 pts)
- Has stack trace ✓ (15 pts)
- No attempted solutions ✗ (0 pts)
- **Total: 80/100** → Above threshold (70), actionable!

---

## playbooks/ (Team Response Templates)

Playbooks provide pre-written responses for common scenarios.

### Example: build.md

```markdown
# Build Issues Troubleshooting

## Common Causes

1. **Dependency Not Found**
   - Missing package in package.json
   - Old lockfile (package-lock.json)
   - Corrupted node_modules

2. **Compilation Errors**
   - TypeScript version mismatch
   - Node version too old
   - Missing environment variables

## Solutions

### Solution 1: Clean Install

```bash
rm -rf node_modules package-lock.json
npm install
npm run build
```

### Solution 2: Check Node Version

```bash
node --version  # Should be v18+
npm --version   # Should be v8+
```

### Solution 3: Check Environment Variables

```bash
echo $NODE_ENV
echo $API_URL
```

## Next Steps

If the above doesn't work:
- Share build logs (first 500 characters)
- Confirm Node.js version
- Try on a different machine
```

---

## Configuration Best Practices

### 1. Keep It Simple Initially

Start with:
- 3-5 main categories
- 3-5 questions per category
- Basic validation rules

Then expand based on issues received.

### 2. Make Questions Specific

❌ **Bad:**
```yaml
question: "Tell me more about the issue"
```

✅ **Good:**
```yaml
question: "What is the exact error message you see in the console?"
```

### 3. Prioritize Questions

Put high-priority questions first:
- Error message ← Users should provide
- Environment details ← Diagnostic info
- Screenshots ← Optional but helpful

### 4. Use Keywords Wisely

**Good keywords:**
- Specific: "postgresql", "connection", "timeout"
- Not: "problem", "help", "issue" (too generic)

### 5. Regular Updates

Review configuration quarterly:
- Are assignments working?
- Are new issue patterns emerging?
- Should we add new categories?

---

## Testing Configuration

### Test Category Matching

```bash
cd .github-bot/src/SupportConcierge
dotnet run --test-categorize "My database connection is timing out"
```

### Test Completeness Scoring

```bash
dotnet run --test-score "Error: timeout. Using PostgreSQL 14.5. Ubuntu 22.04."
```

### Dry Run

```bash
dotnet run --dry-run
```

---

## Configuration Examples

### For an ETL Pipeline Project

```yaml
categories:
  - id: "etl_pipeline_error"
    keywords:
      - "dag"
      - "etl"
      - "airflow"
      - "pipeline"
    priority: "high"
```

### For a Data Platform

```yaml
categories:
  - id: "data_quality"
    keywords:
      - "data quality"
      - "null values"
      - "duplicates"
    priority: "high"
```

### For a Library Project

```yaml
categories:
  - id: "usage_question"
    keywords:
      - "how to"
      - "example"
      - "documentation"
    priority: "low"
```

---

## Troubleshooting Configuration

### Issue: Bot Not Categorizing Correctly

1. Check `categories.yaml` keywords
2. Verify keyword matches issue title/body
3. Ensure pattern syntax is correct

### Issue: Wrong Assignee

1. Check `routing.yaml` patterns
2. Verify GitHub username is correct
3. Confirm user has permission

### Issue: Low Completeness Scores

1. Review `validators.yaml` weights
2. Adjust threshold if needed
3. Consider adding more helpful questions

---

## Next Steps

1. **Copy example configs**: Already in `.github-bot/config/supportbot/`
2. **Customize for your project**: Edit the YAML files
3. **Test locally**: Use `dotnet run --dry-run`
4. **Deploy**: Push changes to GitHub
5. **Monitor**: Review issues created in first week

For more help, see [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
