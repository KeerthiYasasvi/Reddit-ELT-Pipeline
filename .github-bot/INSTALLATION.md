# 📦 Installation Guide

Choose one of three installation methods based on your needs.

---

## Option 1: Git Submodule (⭐ Recommended)

**Best for:** Users who want to stay synchronized with bot updates

### Step 1: Add as Submodule

```bash
cd your-project-root
git submodule add https://github.com/KeerthiYasasvi/github-issues-support .github-bot
```

### Step 2: Initialize & Fetch

```bash
git submodule update --init --recursive
```

### Step 3: Set Up Workflow

Copy the workflow to your project's GitHub Actions:

```bash
mkdir -p .github/workflows
cp .github-bot/.github/workflows/support-bot.yml .github/workflows/
```

### Step 4: Configure Secrets

In your GitHub repository settings:

1. Go to **Settings → Secrets and variables → Actions**
2. Click **New repository secret**
3. Add:
   - `OPENAI_API_KEY` = your OpenAI API key
   - `GITHUB_TOKEN` = (automatically provided, but can be customized)

### Step 5: Commit

```bash
git add .gitmodules .github-bot .github/workflows/support-bot.yml
git commit -m "Add GitHub Issues Support Bot as submodule"
git push
```

### 🔄 Updating the Bot

When the bot author releases updates:

```bash
cd .github-bot
git pull origin main
cd ..
git add .github-bot
git commit -m "Update support bot to latest version"
git push
```

### ❌ Removing the Bot

```bash
# Remove submodule reference
git submodule deinit -f .github-bot
git rm -f .github-bot

# Remove workflow
rm .github/workflows/support-bot.yml

# Remove secrets (manual in GitHub UI)

# Commit changes
git commit -m "Remove support bot"
git push
```

---

## Option 2: Direct Copy (Simplest)

**Best for:** One-off customization or simple integration

### Step 1: Copy Folder

```bash
cd your-project-root
cp -r /path/to/github-issues-support .github-bot
```

Or on Windows:
```powershell
Copy-Item -Path "C:\path\to\github-issues-support" -Destination ".github-bot" -Recurse
```

### Step 2: Set Up Workflow

```bash
mkdir -p .github/workflows
cp .github-bot/.github/workflows/support-bot.yml .github/workflows/
```

### Step 3: Configure Secrets

In GitHub repository settings, add:
- `OPENAI_API_KEY` = your OpenAI API key

### Step 4: Commit

```bash
git add .github-bot .github/workflows/support-bot.yml
git commit -m "Add GitHub Issues Support Bot"
git push
```

### ⚠️ Important: Update .gitignore

Make sure your `.gitignore` doesn't ignore the bot files:

```bash
# NOT in .gitignore:
# .github-bot/    # Don't exclude this!

# But DO ignore build artifacts:
.github-bot/**/bin/
.github-bot/**/obj/
.github-bot/**/.vs/
```

### ❌ Removing the Bot

```bash
rm -rf .github-bot
rm .github/workflows/support-bot.yml
git add -u
git commit -m "Remove support bot"
git push
```

---

## Option 3: Fork & Customize

**Best for:** Deep customization or maintaining your own version

### Step 1: Fork Repository

Go to https://github.com/KeerthiYasasvi/github-issues-support and click **Fork**

### Step 2: Add as Submodule

```bash
cd your-project-root
git submodule add https://github.com/YOUR-USERNAME/github-issues-support .github-bot
```

### Step 3-4: Same as Option 1

(Set up workflow and secrets)

### ✏️ Customize

Edit files in `.github-bot/` as needed:
- Modify `config/` files for bot behavior
- Update `src/` for logic changes
- Customize `evals/` for different metrics

### 🔄 Stay Synced With Upstream

```bash
cd .github-bot
git remote add upstream https://github.com/KeerthiYasasvi/github-issues-support
git pull upstream main
# Resolve any conflicts
git push
```

---

## ✅ Verification

After installation, verify the bot is working:

### 1. Check Workflow

Go to **Actions** tab in your GitHub repo. You should see:
- `Support Concierge Bot` workflow
- Status should be **enabled**

### 2. Open Test Issue

Create a test issue with content like:

```markdown
**Environment:**
- OS: Ubuntu 22.04
- Version: 5.2.1

**Problem:**
Database connection failing.

**Error:**
Connection timeout after 30 seconds.
```

### 3. Wait for Bot Response

Within 30 seconds, the bot should:
- Post a comment asking clarifying questions
- Auto-label the issue
- Optionally assign to team member

### 4. Reply to Bot

Reply to bot's questions with detailed answers. Bot should:
- Ask follow-up questions (up to 3 rounds)
- Score the issue
- Eventually finalize when completeness threshold reached

---

## 🔐 Security Notes

### Secrets Management

- **Never commit** API keys or secrets
- Use GitHub Secrets for sensitive values
- Secrets are encrypted and only visible during workflow runs
- Rotate API keys regularly

### Access Control

- Only repository **writers** can view Secrets
- Bot state in HTML comments is visible to anyone who can see issues
- Use **Private repositories** if you don't want public visibility

### Data Privacy

- Issue content is sent to OpenAI API (review their privacy policy)
- State stored in GitHub (standard GitHub issue comments)
- No data persisted externally

---

## 🛠️ Troubleshooting

### Bot Not Responding

1. Check **Actions** tab for workflow errors
2. Verify `OPENAI_API_KEY` is set in Secrets
3. Check workflow logs for error messages
4. See [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)

### Workflow Not Triggering

1. Ensure workflow file is in `.github/workflows/`
2. Check workflow is **enabled** (not disabled)
3. Verify `on:` triggers are correct
4. Check branch protection rules

### Configuration Issues

1. Verify `config/*.yaml` files are valid YAML
2. Check indentation (YAML is sensitive to spacing)
3. Review [docs/CONFIGURATION.md](docs/CONFIGURATION.md)

---

## 📞 Support

- **Questions?** See [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)
- **Found a bug?** Report at https://github.com/KeerthiYasasvi/github-issues-support/issues
- **Feature request?** Discuss at https://github.com/KeerthiYasasvi/github-issues-support/discussions

---

**Next Steps:**

1. ✅ Choose installation method (Submodule recommended)
2. ✅ Follow setup steps above
3. ✅ Configure bot behavior via `config/` files
4. ✅ Test with a sample issue
5. ✅ Monitor bot performance with EvalRunner

See **[../docs/CONFIGURATION.md](../docs/CONFIGURATION.md)** to customize bot behavior.
