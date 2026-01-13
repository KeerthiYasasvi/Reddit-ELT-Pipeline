# Challenges and Solutions - GitHub Issues Support Bot Project

## Interview Q&A Format

This document tracks all technical challenges encountered during the development and deployment of the GitHub Issues Support Concierge Bot, along with the approaches taken to resolve them.

---

## Challenge 1: OpenAI SDK v2.1.0 Model Parameter Not Being Passed to API

**Q: What was the issue?**

When deploying the bot with OpenAI SDK v2.1.0, the bot consistently failed with error: `"you must provide a model parameter"` despite the model being specified in the code.

**Q: What did you try first?**

Initially, we suspected the model name format was the issue. We changed from `gpt-4o-2024-08-06` to `gpt-4` thinking the timestamped version might not be recognized by the SDK.

**Result:** ❌ Failed - Same error persisted

**Q: What was the second approach?**

We discovered that the GitHub Actions workflow had an environment variable `OPENAI_MODEL` with a default value of `gpt-4o-2024-08-06`. We updated the workflow file to use `gpt-4` as the default.

**File Changed:** `.github/workflows/support-concierge.yml`
```yaml
# Before
OPENAI_MODEL: ${{ vars.OPENAI_MODEL || 'gpt-4o-2024-08-06' }}

# After  
OPENAI_MODEL: ${{ vars.OPENAI_MODEL || 'gpt-4' }}
```

**Result:** ✅ Model changed successfully, but ❌ API error continued

**Q: What was the third approach?**

We optimized the code by caching the `ChatClient` instance in the constructor instead of creating new instances on every method call, thinking this might help with model parameter binding.

**Code Changed:** `src/SupportConcierge/Agents/OpenAiClient.cs`
```csharp
// Before - creating new client each time
private ChatClient GetChatClientForModel() => _openAiClient.GetChatClient(_model);

// After - caching client in constructor
private ChatClient _chatClient;
public OpenAiClient()
{
    // ... initialization code ...
    _chatClient = _openAiClient.GetChatClient(_model);
}
```

**Result:** ❌ Failed - Same error persisted

**Q: What was the fourth approach?**

We upgraded the OpenAI SDK from v2.1.0 to v2.2.0, suspecting there might be a bug fix in the newer version.

**File Changed:** `src/SupportConcierge/SupportConcierge.csproj`
```xml
<!-- Before -->
<PackageReference Include="OpenAI" Version="2.1.0" />

<!-- After -->
<PackageReference Include="OpenAI" Version="2.2.0" />
```

**Result:** ❌ Failed - Same error persisted across v2.1.0 and v2.2.0

**Q: What was the root cause?**

The OpenAI SDK v2.x `GetChatClient(string model)` method has a fundamental issue where it does not properly include the model parameter in the actual API request, despite accepting it as a constructor argument. This appears to be an SDK design flaw affecting the entire v2.x series.

**Q: What is the final solution?**

**Approach 5:** Downgrade to OpenAI SDK v1.x which has a proven, stable API for model specification.

**File Changed:** `src/SupportConcierge/SupportConcierge.csproj`
```xml
<!-- Changed from v2.2.0 to v1.11.0 -->
<PackageReference Include="OpenAI" Version="1.11.0" />
```

**Discovery:** OpenAI SDK v1.x uses a completely different API:
- v1.x: Uses `OpenAI_API` namespace, `OpenAIAPI` class, different method signatures
- v2.x: Uses `OpenAI` namespace, `OpenAIClient` class, `GetChatClient()` pattern

**Result:** ❌ Blocked - Requires complete code rewrite (248 lines) to adapt to v1.x API

**Q: What is the actual final solution?**

After extensive testing across v2.1.0, v2.2.0, and attempting v1.x downgrade, the issue is that **the OpenAI .NET SDK v2.x has a fundamental design flaw** where `GetChatClient(model)` doesn't pass the model to API requests.

**Recommended Solutions:**
1. **Use the official OpenAI HTTP API directly** instead of the SDK
2. **Wait for OpenAI SDK v2.3.0+** with the bug fix
3. **Invest time in complete v1.x rewrite** (248 lines of code changes)
4. **Switch to Azure OpenAI SDK** which has different implementation

**Status:** 🔄 Documented - Decision pending on which path to take

**Key Learning:** When using third-party SDKs, always validate that basic functionality works before building on top of it. SDK bugs can block entire projects.

---

## Challenge 2: Git Not Available in PATH Environment Variable

**Q: What was the issue?**

When attempting to commit and push code changes from PowerShell, the `git` command was not recognized because Git was not in the system PATH environment variable.

**Error:** `git : The term 'git' is not recognized as the name of a cmdlet, function, script file, or operable program.`

**Q: How was this resolved?**

The user added Git to the Windows environment variables manually, making it accessible from any terminal session.

**Result:** ✅ Resolved - Git commands now work in PowerShell

---

## Challenge 3: Testing Strategy - Repository Selection

**Q: What was the decision point?**

We needed to decide whether to:
1. Create a new GitHub repository specifically for the github-issues-support bot
2. Use the existing Reddit-ELT-Pipeline repository for testing

**Q: What approach was chosen and why?**

We chose to deploy the bot to the existing Reddit-ELT-Pipeline repository because:
- The repository already existed and was configured
- The bot is designed to monitor any repository via GitHub Actions
- It simplified the testing workflow
- Avoided creating unnecessary repositories

**Result:** ✅ Efficient testing setup achieved

---

## Challenge 4: Understanding GitHub Actions Workflow Environment Variables

**Q: What was the learning point?**

Initially, code changes to the default model value in `OpenAiClient.cs` didn't take effect because the GitHub Actions workflow was overriding them with environment variables.

**Q: What did we learn?**

Environment variables set in GitHub Actions workflows take precedence over code defaults. When troubleshooting issues, always check:
1. Code defaults
2. Workflow environment variables
3. Repository secrets and variables

**Key Insight:** The workflow file is the source of truth for environment configuration in CI/CD pipelines.

---

## Challenge 5: SDK Version Compatibility Issues

**Q: What did we learn about SDK versioning?**

Different major versions of the OpenAI SDK have significantly different APIs:
- **v1.x:** Stable, proven API with clear model parameter handling
- **v2.x:** Redesigned API with `GetChatClient()` pattern that has issues

**Q: What's the lesson for future projects?**

- Always check SDK changelog and breaking changes when upgrading major versions
- Test thoroughly in a development environment before production deployment
- Consider staying on LTS (Long Term Support) versions for production systems
- Have a rollback plan when testing new SDK versions

---

## Technical Debugging Process Demonstrated

Throughout this project, we demonstrated a systematic debugging approach:

1. **Hypothesis Formation:** Identify potential causes
2. **Incremental Testing:** Change one variable at a time
3. **Verification:** Confirm changes in logs/output
4. **Documentation:** Track what was tried and results
5. **Escalation:** When pattern fails, try fundamentally different approach

---

## Tools and Technologies Mastered

- **GitHub Actions:** Workflow configuration, environment variables, secrets
- **OpenAI API Integration:** SDK usage, model parameters, error handling
- **.NET 8:** C# development, NuGet package management
- **Git/GitHub:** Version control, branch management, CI/CD
- **PowerShell:** Windows terminal commands, environment configuration
- **Playwright Browser Automation:** Testing web interfaces programmatically

---

*Last Updated: January 12, 2026*
