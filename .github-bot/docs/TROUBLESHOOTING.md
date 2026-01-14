# 🆘 Troubleshooting Guide

Common issues and solutions.

---

## Bot Not Responding to Issues

### Problem
Created an issue but bot never responds.

### Diagnosis

1. **Check GitHub Actions**
   - Go to **Actions** tab in your repository
   - Look for "Support Concierge Bot" workflow
   - Check if it shows as "enabled"

2. **Check Workflow Logs**
   - Click the workflow
   - See if it ran (should show green checkmark)
   - Click latest run → Expand job logs
   - Look for errors

3. **Verify Secrets**
   - Settings → Secrets and variables → Actions
   - Check `OPENAI_API_KEY` exists
   - Value should not be empty

### Solutions

**If workflow is disabled:**
```bash
# Re-enable workflow
gh workflow enable "support-bot.yml" -R your-org/your-repo
```

**If OPENAI_API_KEY is missing:**
1. Get key from https://platform.openai.com/api-keys
2. Go to Settings → Secrets → New secret
3. Add `OPENAI_API_KEY = your_key_value`

**If workflow file is missing:**
```bash
cp .github-bot/.github/workflows/support-bot.yml .github/workflows/
git add .github/workflows/support-bot.yml
git commit -m "Add support bot workflow"
git push
```

---

## Bot Responds But Then Stops

### Problem
Bot replies to first issue, but then stops responding to new issues.

### Diagnosis

1. **Check GitHub Actions rate limiting**
   - Go to Settings → Developer settings → Personal access tokens
   - Verify token hasn't been revoked

2. **Check OpenAI API quota**
   - Go to https://platform.openai.com/account/billing/limits
   - Check monthly usage vs limit
   - Check if account is in good standing

3. **Review workflow logs**
   - Check for repeated error messages
   - Look for "rate limit" or "quota" mentions

### Solutions

**If OpenAI quota exceeded:**
- Upgrade plan at https://platform.openai.com/account/billing/overview
- Or reduce bot usage (disable for certain issue types)

**If GitHub token issue:**
```bash
# Create new Personal Access Token
# Settings → Developer settings → Personal access tokens → New token
# Select scopes: repo, workflow, write:packages
# Use as GITHUB_TOKEN in workflow
```

**If rate limited:**
- GitHub API limit: 5000 requests/hour (usually not an issue)
- OpenAI limit: Check your plan
- Wait 1 hour and retry

---

## Bot Error Messages

### "OPENAI_API_KEY not found"

**Cause**: Secret not set in GitHub

**Fix**:
1. Settings → Secrets and variables → Actions
2. Create `OPENAI_API_KEY`
3. Paste your OpenAI key
4. Re-run workflow

### "401 Unauthorized"

**Cause**: Invalid API key

**Fix**:
1. Verify key at https://platform.openai.com/api-keys
2. Generate new key if needed
3. Update secret in GitHub
4. Re-run workflow

### "Timeout after 30 seconds"

**Cause**: OpenAI API taking too long

**Fix**:
- Usually temporary
- Workflow will auto-retry
- If persistent, check OpenAI status page

### "Repository access denied"

**Cause**: GITHUB_TOKEN doesn't have required permissions

**Fix**:
```yaml
# In .github/workflows/support-bot.yml, add:
permissions:
  contents: read
  issues: write
  pull-requests: read
```

---

## Configuration Issues

### Bot Not Categorizing Issues Correctly

1. **Check config files**
   - `.github-bot/config/supportbot/categories.yaml`
   - Verify keywords are correct

2. **Test categorization**
   ```bash
   cd .github-bot/src/SupportConcierge
   dotnet run --test-categorize "your issue text here"
   ```

3. **Verify YAML syntax**
   - Check indentation (YAML is sensitive)
   - Use https://www.yamllint.com to validate

### Bot Scoring Issues Too Harshly/Leniently

1. **Review validators**
   - `.github-bot/config/supportbot/validators.yaml`
   - Adjust weights if needed

2. **Test scoring**
   ```bash
   dotnet run --test-score "issue description"
   ```

3. **Update threshold**
   - Default: 70/100
   - Adjust `completeness_threshold` in validators.yaml

---

## Workflow Issues

### Workflow File Not Found

```
Error: Could not find workflow 'support-bot.yml'
```

**Fix**:
```bash
# Verify file exists
ls -la .github/workflows/support-bot.yml

# If missing, copy from bot folder
cp .github-bot/.github/workflows/support-bot.yml .github/workflows/

# Commit and push
git add .github/workflows/support-bot.yml
git commit -m "Add bot workflow"
git push
```

### YAML Syntax Error

```
Error: The workflow is not valid
```

**Fix**:
1. Copy fresh workflow: `cp .github-bot/.github/workflows/support-bot.yml .github/workflows/support-bot.yml`
2. Check for tabs (must be spaces in YAML)
3. Validate at https://codebeautify.org/yaml-validator

### Workflow Timeout

```
Workflow did not complete within 15 minutes
```

**Causes**:
- Slow OpenAI API
- GitHub Actions queue
- Network issues

**Fix**:
- Workflow will auto-retry
- Usually resolves on next run
- Check GitHub status page

---

## Performance Issues

### Bot Takes Too Long to Respond

**Expected**: 15-30 seconds for first comment

**If longer**:
1. Check GitHub Actions queue
2. Check OpenAI API status
3. Verify network connectivity

### Too Many API Calls

**Monitor usage**:
```bash
# Check GitHub Actions log
gh run view --log -R your-org/your-repo <run-id>
```

**Reduce usage**:
- Disable bot for certain labels
- Only run on specific issue types
- Batch issues to test mode first

---

## Specific Errors

### "Invalid JSON in response"

**Cause**: OpenAI returned malformed response

**Fix**:
- Usually temporary
- Workflow retries automatically
- Report if persistent

### "State too large (>65KB)"

**Cause**: Issue has too many follow-ups

**Fix**:
- Auto-compression kicks in at 5KB
- Auto-pruning keeps state bounded
- If still occurring, close issue and create new one

### "Duplicate label detected"

**Cause**: Label already exists on issue

**Fix**:
- Remove duplicate label manually
- Or ignore in code (won't cause failure)

---

## Getting Help

### Debug Mode

Enable verbose logging:

```bash
# In workflow, add:
env:
  DEBUG: "true"
```

Then check logs for detailed output.

### Local Testing

```bash
cd .github-bot/src/SupportConcierge

# Dry run (no API calls)
dotnet run -- --dry-run

# Test categorization
dotnet run -- --test-categorize "issue text"

# Test scoring
dotnet run -- --test-score "issue text"
```

### Check Logs

```bash
# Latest workflow run
gh run list --repo your-org/your-repo --limit 1

# Full logs for a run
gh run view <run-id> --log --repo your-org/your-repo
```

---

## Common Questions

**Q: Why is the bot slow?**
A: OpenAI API + GitHub Actions startup time. Typically 15-30 seconds. If much slower, check network/API status.

**Q: Can I disable the bot temporarily?**
A: Yes! Disable workflow in Actions tab without deleting files.

**Q: Can I customize responses?**
A: Yes! Edit templates in `.github-bot/config/supportbot/playbooks/`

**Q: How much does this cost?**
A: Only OpenAI API. ~$0.01-0.05 per issue. Check https://platform.openai.com/account/billing

**Q: Can I use a different LLM?**
A: Yes! Modify `.github-bot/src/SupportConcierge/Agents/OpenAiClient.cs`

---

## Still Having Issues?

1. **Check this guide** - Most common issues covered
2. **Check logs** - GitHub Actions shows detailed errors
3. **Test locally** - Use dry-run to debug
4. **Review config** - Validator rules might be wrong
5. **Report issue** - https://github.com/KeerthiYasasvi/github-issues-support/issues

---

For more info, see:
- [INSTALLATION.md](../INSTALLATION.md) - Setup guide
- [CONFIGURATION.md](CONFIGURATION.md) - Configuration options
- [ARCHITECTURE.md](ARCHITECTURE.md) - How it works
