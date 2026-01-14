# 📚 Integration Example: Reddit ETL Pipeline

This document shows how this support bot was integrated into the Reddit ETL Pipeline project.

---

## Project Overview

**Reddit ETL Pipeline** is a data engineering project that:
- Extracts data from Reddit API
- Transforms data with DBT
- Loads into a data warehouse
- Uses Apache Airflow for orchestration

### Original Structure (Before Bot)

```
reddit-etl-pipeline/
├── dags/                  ← Airflow DAGs
├── dbt_project/           ← DBT models
├── etls/                  ← ETL scripts
├── README.md
└── LICENSE
```

---

## Integration Strategy

### Goal
Add support bot to handle GitHub issues without cluttering project directories.

### Solution
Create `.github-bot/` folder containing all bot infrastructure.

---

## New Structure (After Bot)

```
reddit-etl-pipeline/
├── dags/                  ← PROJECT CODE (unchanged)
├── dbt_project/           ← PROJECT CODE (unchanged)
├── etls/                  ← PROJECT CODE (unchanged)
│
├── .github-bot/           ← ALL BOT FILES HERE ← Can be deleted!
│   ├── README.md
│   ├── INSTALLATION.md
│   ├── REMOVAL.md
│   ├── .gitignore
│   ├── src/
│   │   └── SupportConcierge/
│   ├── evals/
│   │   └── EvalRunner/
│   ├── config/
│   │   └── supportbot/
│   │       ├── categories.yaml
│   │       ├── routing.yaml
│   │       └── validators.yaml
│   ├── .github/
│   │   └── workflows/
│   │       └── support-bot.yml
│   └── docs/
│
├── .github/
│   └── workflows/
│       ├── support-bot.yml    ← Wrapper (calls bot from .github-bot/)
│       └── [other project workflows]
│
├── README.md               ← PROJECT README
└── LICENSE
```

---

## Installation Steps for This Project

### Step 1: Project Was Already Public

Reddit ETL Pipeline is a public learning project, so:
- ✅ Can accept bot
- ✅ Issues can be public
- ✅ GitHub Actions available

### Step 2: Add as Git Submodule (Option A)

```bash
# In reddit-etl-pipeline root
git submodule add https://github.com/KeerthiYasasvi/github-issues-support .github-bot

# Initialize
git submodule update --init --recursive

# Set up workflow
cp .github-bot/.github/workflows/support-bot.yml .github/workflows/

# Commit
git add .gitmodules .github-bot .github/workflows/support-bot.yml
git commit -m "Add GitHub Issues Support Bot as submodule"
git push
```

### Step 3: Configure Bot for ETL Use Case

**Edit `.github-bot/config/supportbot/categories.yaml`:**

```yaml
categories:
  - id: "dag_error"
    name: "DAG/Airflow Error"
    keywords:
      - "dag"
      - "airflow"
      - "scheduler"
      - "task failed"
    priority: "high"
    assignee: "devops-team"

  - id: "dbt_error"
    name: "DBT Transformation Error"
    keywords:
      - "dbt"
      - "transform"
      - "model"
      - "compilation error"
    priority: "high"
    assignee: "data-team"

  - id: "etl_issue"
    name: "ETL Data Quality Issue"
    keywords:
      - "etl"
      - "data quality"
      - "duplicate"
      - "null values"
    priority: "medium"
    assignee: "data-team"

  - id: "documentation"
    name: "Documentation Request"
    keywords:
      - "docs"
      - "how to"
      - "example"
    priority: "low"
    assignee: "docs-team"
```

**Edit `.github-bot/config/supportbot/checklists.yaml`:**

```yaml
checklists:
  dag_error:
    name: "DAG Troubleshooting"
    questions:
      - field: "dag_name"
        question: "Which DAG is failing? (e.g., reddit_etl_dag)"
        required: true
        priority: "high"

      - field: "error_message"
        question: "What is the error message in Airflow UI?"
        required: true
        priority: "high"

      - field: "task_name"
        question: "Which task failed?"
        required: true
        priority: "medium"

      - field: "logs"
        question: "Can you share the task logs (first 500 chars)?"
        required: false
        priority: "medium"

  dbt_error:
    name: "DBT Transformation Troubleshooting"
    questions:
      - field: "model_name"
        question: "Which DBT model is failing?"
        required: true
        priority: "high"

      - field: "error_type"
        question: "Is it a compilation, runtime, or test error?"
        required: true
        priority: "high"

      - field: "error_message"
        question: "What is the exact error message?"
        required: true
        priority: "high"
```

### Step 4: Set GitHub Secrets

1. **Go to Settings → Secrets and variables → Actions**
2. **Add** `OPENAI_API_KEY`:
   - Get key from https://platform.openai.com/api-keys
   - Add as secret

### Step 5: Test Bot

Create a test issue:

```markdown
**DAG:** reddit_etl_dag

**Problem:**
Task 'extract_reddit_data' failed with timeout after 5 minutes.

**Environment:**
- Airflow 2.7
- Python 3.10
- Ubuntu 22.04

**Error:**
```
ConnectionError: Failed to connect to Reddit API
Timeout: 300 seconds exceeded
```

**Logs:**
```
2026-01-14 08:45:23 - ERROR - Reddit API connection failed
2026-01-14 08:45:23 - ERROR - Retried 3 times, giving up
```
```

**Expected**: Bot should respond within 30 seconds with follow-up questions.

---

## Bot Configuration for Reddit ETL Pipeline

### Categories Detected

```
dag_error      ← DAG/Airflow issues
dbt_error      ← DBT transform issues
etl_issue      ← Data quality issues
documentation  ← Help/how-to questions
```

### Auto-Tagging

Issues are automatically labeled with:
- `component:airflow` (if DAG-related)
- `component:dbt` (if DBT-related)
- `component:data-quality` (if data issue)
- `type:bug` (if error)
- `type:question` (if help needed)

### Auto-Assignment

Based on issue category:
- DAG errors → `devops-team`
- DBT errors → `data-team`
- Data quality → `data-team`
- Documentation → `docs-team`

---

## Using the Bot

### For Users Creating Issues

1. **Describe the problem clearly**
2. **Include environment details** (Python version, Airflow version, etc.)
3. **Paste error messages and logs**
4. **Answer bot's follow-up questions**

Example issue that bot can help with:

```markdown
## DAG Failing

My `reddit_etl_dag` is failing with connection timeout.

**Environment:**
- Airflow: 2.7.0
- Python: 3.10
- OS: Ubuntu 22.04

**Error:**
ConnectionError: Reddit API timeout

**Steps to reproduce:**
1. Trigger DAG manually
2. Wait 5 minutes
3. See failure
```

### For Maintainers

Bot automatically:
1. ✅ Asks clarifying questions
2. ✅ Labels the issue
3. ✅ Assigns to appropriate team
4. ✅ Scores completeness
5. ✅ Finalizes when enough info provided

---

## Removing the Bot (If Not Needed)

### Option A: Submodule Removal

```bash
git submodule deinit -f .github-bot
git rm -f .github-bot
rm .github/workflows/support-bot.yml
git commit -m "Remove support bot"
git push
```

### Option B: Disable Temporarily

```bash
# In GitHub Actions: Disable workflow without deleting
gh workflow disable "support-bot.yml"
```

---

## Customization Ideas

### For Your Project

1. **Add project-specific categories**
   - Reddit API changes
   - Warehouse schema updates
   - Deployment issues

2. **Customize questions**
   - Ask for DAG run ID
   - Ask for dbt debug output
   - Ask for data row counts

3. **Add playbooks**
   - Common DAG failures
   - Common dbt errors
   - Data quality checks

4. **Integrate with team**
   - Assign to maintainers
   - Use GitHub teams
   - Add code owners

---

## Cost Considerations

For Reddit ETL Pipeline with typical issue volume (10-20 issues/month):

- **OpenAI API**: ~$0.20-1.00/month
- **GitHub Actions**: Free tier covers this
- **Total**: Minimal cost for automated support

---

## Monitoring

### Check Bot Performance

```bash
# View recent workflow runs
gh run list --repo KeerthiYasasvi/Reddit-ELT-Pipeline --limit 10

# Check latest run logs
gh run view <run-id> --log
```

### Metrics to Monitor

- Number of issues created per month
- Average completeness score
- How many issues finalized vs ongoing
- Bot response time
- OpenAI API usage

---

## Benefits for Reddit ETL Pipeline

✅ **Better Issue Quality**: Users provide complete information  
✅ **Faster Triage**: Bot categorizes and assigns automatically  
✅ **Reduced Maintenance**: Less back-and-forth questions  
✅ **Learning Resource**: Bot helps guide users toward best practices  
✅ **Community Support**: Bot makes project approachable  

---

## Future Enhancements

### Potential Additions

1. **Automated Testing**
   - Run dbt on failed query
   - Test API connectivity

2. **Documentation Links**
   - Link to setup guides
   - Link to troubleshooting docs

3. **Historical Context**
   - Reference previous similar issues
   - Suggest solutions from past issues

4. **Metrics Dashboard**
   - Track issue trends
   - Visualize bot performance

---

## Resources

- **Bot Repository**: https://github.com/KeerthiYasasvi/github-issues-support
- **Reddit ETL Pipeline**: https://github.com/KeerthiYasasvi/Reddit-ELT-Pipeline
- **Setup Guide**: See [../INSTALLATION.md](../INSTALLATION.md)
- **Configuration**: See [CONFIGURATION.md](CONFIGURATION.md)

---

**Questions?** Check [TROUBLESHOOTING.md](TROUBLESHOOTING.md) or open an issue in the bot repository.
