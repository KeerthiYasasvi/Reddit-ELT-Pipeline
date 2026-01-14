# 🗑️ Removal Guide

If you no longer want the bot, it's easy to remove completely. Choose the method that matches your installation.

---

## Removing Bot: Submodule Installation

If you added the bot using `git submodule add`:

### Step 1: Remove Submodule Reference

```bash
git submodule deinit -f .github-bot
git rm -f .github-bot
```

This removes:
- The submodule entry from `.gitmodules`
- The `.github-bot` folder from git tracking
- The submodule reference

### Step 2: Remove Workflow File

```bash
rm .github/workflows/support-bot.yml
```

### Step 3: Remove Secrets (Manual)

In GitHub repository settings:
1. Go to **Settings → Secrets and variables → Actions**
2. Delete `OPENAI_API_KEY`
3. (Keep `GITHUB_TOKEN` if you need it for other workflows)

### Step 4: Commit Changes

```bash
git commit -m "Remove GitHub Issues Support Bot submodule"
git push
```

### Step 5: Clean Local Cache

```bash
rm -rf .git/modules/.github-bot
```

---

## Removing Bot: Direct Copy Installation

If you copied the bot files directly:

### Step 1: Remove Bot Folder

```bash
rm -rf .github-bot
```

Or on Windows:
```powershell
Remove-Item -Path ".github-bot" -Recurse -Force
```

### Step 2: Remove Workflow File

```bash
rm .github/workflows/support-bot.yml
```

### Step 3: Remove Secrets (Manual)

In GitHub repository settings:
1. Go to **Settings → Secrets and variables → Actions**
2. Delete `OPENAI_API_KEY`

### Step 4: Commit Changes

```bash
git add -u
git commit -m "Remove GitHub Issues Support Bot"
git push
```

---

## Removing Bot: Fork Installation

If you forked and added as submodule:

### Same as Submodule Installation (Steps 1-5 above)

The removal process is identical to submodule installation.

---

## After Removal

### ✅ What Happens

- 🔴 **Workflow stops running** - No new bot comments on issues
- 📝 **Existing comments remain** - Previous bot comments stay on old issues
- 🏷️ **Labels remain** - Auto-applied labels don't get removed
- ✉️ **No notifications** - GitHub won't notify about bot removal

### 📋 Cleanup Checklist

After removing the bot:

- [ ] Confirm `.github-bot/` folder is deleted
- [ ] Confirm workflow file is deleted from `.github/workflows/`
- [ ] Check `git status` shows no `.github-bot` files
- [ ] Verify `OPENAI_API_KEY` removed from Secrets
- [ ] Commit and push all changes
- [ ] Check GitHub Actions shows no active bot workflows

### 🔍 Verification

To verify bot is completely removed:

```bash
# Should show nothing:
git ls-files | grep github-bot

# Should show no submodule entry:
cat .gitmodules

# Should not exist:
ls -la .github-bot
```

---

## Cleanup Existing Issues

If you want to clean up bot-created labels or comments from existing issues:

### Remove Bot Labels

```bash
# Script to remove bot-applied labels
# (This is a suggestion - implement based on your needs)

for issue in $(gh issue list --state all --json number -q '.[].number'); do
  gh issue edit $issue --remove-label "component:*" --remove-label "type:*"
done
```

### Remove Bot Comments

This requires more manual work:

1. Go to each issue
2. Find bot comments (author: `github-actions[bot]`)
3. Click **⋯** menu → **Delete**

**Note:** GitHub doesn't provide bulk operations for this.

### Archive Old Issues

If you have many bot-processed issues:

1. Search: `is:closed created:before:2024-01-01`
2. Select issues
3. Use GitHub CLI: `gh issue close` if needed

---

## Reinstalling Later

If you remove the bot and want to reinstall:

### Option 1: Re-add as Submodule

```bash
git submodule add https://github.com/KeerthiYasasvi/github-issues-support .github-bot
```

### Option 2: Copy Again

```bash
cp -r /path/to/github-issues-support .github-bot
```

### Then Follow Installation Steps

See [INSTALLATION.md](INSTALLATION.md) for complete setup.

---

## ❓ FAQ

**Q: Will removing the bot break existing issues?**
A: No. Old issues remain open with bot comments intact. Only new issues are unaffected.

**Q: Can I temporarily disable the bot?**
A: Yes! Instead of removing, you can:
1. Disable the workflow: **Actions → support-bot.yml → Disable workflow**
2. Or delete the workflow file but keep the code

**Q: What if I just want to pause it?**
A: Disable the workflow:
```bash
# Via GitHub UI: Actions → [workflow] → ... → Disable
# Via GitHub CLI:
gh workflow disable "support-bot.yml" -R your-org/your-repo
```

**Q: How do I remove only the bot workflow but keep the code?**
A: Delete the workflow file but keep `.github-bot/` folder:
```bash
rm .github/workflows/support-bot.yml
git add .github/workflows/support-bot.yml
git commit -m "Disable support bot workflow"
git push
```

**Q: Will GitHub still charge for API calls after removal?**
A: No. Once the workflow is deleted, no API calls are made.

---

## 🆘 Issues During Removal

### Submodule Removal Error

If you get errors removing the submodule:

```bash
# Try force removal
git rm --cached .github-bot
rm -rf .github-bot

# Remove from .git config
git config -f .git/config --remove-section submodule.github-bot
rm -rf .git/modules/.github-bot
```

### Workflow File Won't Delete

```bash
# Ensure file is removed from staging
git rm -f .github/workflows/support-bot.yml
git commit -m "Remove bot workflow"
```

### Still Seeing Bot Commits

Your git history may show bot commits. That's normal and expected. The bot won't process new issues once removed.

---

## 📞 Need Help?

- **General questions?** See [INSTALLATION.md](INSTALLATION.md)
- **Found an issue?** Report at https://github.com/KeerthiYasasvi/github-issues-support/issues
- **Configuration help?** See [docs/CONFIGURATION.md](../docs/CONFIGURATION.md)

---

**That's it!** Your bot is now completely removed. You can reinstall anytime by following [INSTALLATION.md](INSTALLATION.md).
