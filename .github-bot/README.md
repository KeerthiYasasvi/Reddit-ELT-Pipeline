# 🤖 GitHub Issues Support Bot

This folder contains the complete GitHub Issues Support Bot infrastructure. It's designed to be **independently usable** while being **integrated into this project**.

## 📋 What Is This?

The Support Bot is an **AI-powered GitHub issue triage and support system** that:

- 🎯 **Automatically routes issues** based on content analysis
- ❓ **Asks clarifying questions** to gather missing information
- 📊 **Scores issue completeness** (0-100 scale)
- ✅ **Determines if issues are actionable** for your team
- 💬 **Maintains conversation state** across multiple follow-ups
- 🏷️ **Auto-tags issues** with relevant labels
- 👤 **Assigns issues** to appropriate team members

## 📁 Folder Structure

```
.github-bot/
├── README.md                      ← You are here
├── INSTALLATION.md                ← How to use this bot
├── REMOVAL.md                     ← How to remove the bot
│
├── src/
│   └── SupportConcierge/          ← Bot source code (.NET 8)
│       ├── Program.cs             ← Entry point
│       ├── Agents/                ← OpenAI integration
│       ├── GitHub/                ← GitHub API client
│       ├── Orchestration/         ← State management
│       ├── Parsing/               ← Issue parsing
│       ├── Reporting/             ← Response generation
│       └── Scoring/               ← Issue analysis
│
├── evals/
│   ├── EvalRunner/                ← Evaluation framework
│   │   └── Program.cs             ← Metrics generator
│   └── scenarios/                 ← Test scenarios
│
├── config/
│   ├── categories.yaml            ← Issue categories
│   ├── routing.yaml               ← Routing rules
│   ├── checklists.yaml            ← Issue checklists
│   ├── validators.yaml            ← Validation rules
│   └── playbooks/                 ← Team playbooks
│
├── .github/
│   └── workflows/
│       └── support-bot.yml        ← Bot workflow (for reference)
│
├── docs/
│   ├── ARCHITECTURE.md            ← System design
│   ├── CONFIGURATION.md           ← How to configure
│   ├── INTEGRATION.md             ← Integration guide
│   └── TROUBLESHOOTING.md         ← Common issues
│
├── examples/
│   └── integration-setup.md       ← Example setup
│
└── .gitignore                     ← Ignore build artifacts
```

## 🚀 Quick Start

### **Option 1: Use as Submodule (Recommended for Multi-Project)**

```bash
git submodule add https://github.com/KeerthiYasasvi/github-issues-support .github-bot
git submodule update --init --recursive
```

### **Option 2: Direct Copy (Simplest)**

```bash
# Copy this entire folder into your project
cp -r .github-bot /path/to/your/project/
```

### **Option 3: Fork & Customize**

Fork this repository and modify for your needs.

---

For detailed setup instructions, see **[INSTALLATION.md](INSTALLATION.md)**.

## ⚙️ Configuration

The bot behavior is controlled by files in `config/`:

- **`categories.yaml`** - Define issue categories your bot understands
- **`routing.yaml`** - Rules for auto-assigning issues
- **`checklists.yaml`** - Questions to ask for different issue types
- **`validators.yaml`** - Validation rules for completeness
- **`playbooks/`** - Team-specific response templates

See **[docs/CONFIGURATION.md](docs/CONFIGURATION.md)** for details.

## 🔧 Tech Stack

- **Language:** C# (.NET 8)
- **LLM:** OpenAI GPT-4
- **Hosting:** GitHub Actions (fully serverless)
- **State:** GitHub issue comments (with auto-compression)
- **Storage:** No external database needed

## 📊 Key Features

### State Management
- ✅ Thread-local state persistence in HTML comments
- ✅ Automatic GZip compression for large states (>5KB)
- ✅ Smart pruning to prevent unbounded growth
- ✅ Transparent decompression on read

### Quality Metrics (EvalRunner)
- ✅ Tracks 5 key performance metrics
- ✅ Generates markdown + JSON reports
- ✅ A-F grading scale
- ✅ Dry-run mode for testing

### Conversation Management
- ✅ Multi-round follow-ups (up to 3 rounds)
- ✅ Prevents duplicate questions
- ✅ Tracks completeness score
- ✅ Auto-finalizes when threshold reached

## ❓ FAQ

**Q: Do I need an external database?**
A: No! State is stored in GitHub issue comments. No external services required (except OpenAI API).

**Q: Can I remove the bot easily?**
A: Yes! Simply delete this folder: `rm -rf .github-bot/`

**Q: Will this work on my existing issues?**
A: New issues only. The bot activates on `issues.opened` and `issue_comment.created` events.

**Q: How much does it cost?**
A: Only OpenAI API usage. Typically $0.01-0.05 per issue depending on conversation length.

## 📚 Documentation

- **[INSTALLATION.md](INSTALLATION.md)** - Setup guide (3 options)
- **[REMOVAL.md](REMOVAL.md)** - How to remove the bot
- **[docs/CONFIGURATION.md](docs/CONFIGURATION.md)** - Customize behavior
- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** - System design
- **[docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)** - Common issues

## 🤝 Contributing

This bot was designed to be reusable across multiple projects. If you:
- 🐛 Find bugs
- 💡 Have feature ideas
- 🎯 Improve configurations
- 📝 Enhance documentation

Consider contributing back to the main repository!

## 📄 License

Same as parent project. See `LICENSE` in repository root.

## 🔗 Resources

- **Repository:** https://github.com/KeerthiYasasvi/github-issues-support
- **Issues:** https://github.com/KeerthiYasasvi/github-issues-support/issues
- **Discussions:** https://github.com/KeerthiYasasvi/github-issues-support/discussions

---

**Questions?** Check the [Troubleshooting Guide](docs/TROUBLESHOOTING.md) or open an issue in the main repository.
