# 🔍 WORKFLOW RUN #2 ERROR ANALYSIS & SOLUTION

**Date:** January 12, 2026  
**Time:** ~6 minutes ago  
**Status:** ❌ FAILED at "Run Support Concierge" step

---

## ❌ THE ERROR (What Happened)

The bot build succeeded ✅, but when running, it crashed with:

```
FATAL ERROR: HTTP 400 (invalid_request_error: )
you must provide a model parameter
```

**At line:** `OpenAiClient.cs:line 83` (during field extraction)

---

## 🔬 ROOT CAUSE ANALYSIS (Why It Happened)

The error comes from the OpenAI API and the message is clear: **"you must provide a model parameter"**

This happens in the `ExtractCasePacketAsync` method. Looking at the workflow:

1. ✅ Bot loaded configuration (9 categories, 9 checklists, 9 routing rules)
2. ✅ Bot classified issue as `airflow_dag`
3. ✅ Bot started extracting fields from issue
4. ❌ Bot called OpenAI API to extract fields
5. ❌ **OpenAI API rejected the request because NO MODEL WAS SPECIFIED**

### Deep Dive: Where's the Model Parameter?

The workflow environment variables show:

```yaml
env:
  OPENAI_MODEL: ${{ vars.OPENAI_MODEL || 'gpt-4o-2024-08-06' }}
```

**The Problem:** The code is trying to use the model from environment variable `OPENAI_MODEL`, but there are TWO possibilities:

**Option A: The environment variable isn't being passed to the C# code**
- The environment variable is set in the GitHub Actions workflow
- But the C# code might not be reading it correctly
- Or it's null/empty when the code tries to use it

**Option B: The environment variable IS being passed, but the C# code ISN'T using it**
- The `OpenAiClient` class is hardcoded somewhere
- OR the default model is being overridden with null
- OR the workflow variable isn't defined, so it falls back to `null` instead of the default

### Looking at the Code Flow:

From the stack trace:
```
OpenAiClient.cs:line 83 - ExtractCasePacketAsync is called
  ↓
ChatClient.CompleteChatAsync(...options) - ChatCompletionOptions created
  ↓
OpenAI API gets null model parameter
  ↓
API rejects: "you must provide a model parameter"
```

**The Issue:** When creating `ChatCompletionOptions`, the code likely isn't setting the model name, OR it's setting it to null.

---

## 🔧 THE FIX (How to Resolve It)

### **Solution #1: Ensure Environment Variable is Used Properly**

Check `OpenAiClient.cs` at the `ExtractCasePacketAsync` method (around line 83):

**Current (Wrong):**
```csharp
var options = new ChatCompletionOptions();  // No model set!
await _client.CompleteChatAsync(messages, options);
```

**Fixed:**
```csharp
var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-2024-08-06";
var options = new ChatCompletionOptions();
await _client.CompleteChatAsync(messages, options, model);  // Add model parameter
```

**OR** more cleanly, use the model in the ChatClient initialization:

```csharp
private readonly string _model;

public OpenAiClient(string? model = null)
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    _model = model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-2024-08-06";
    _client = new ChatClient(_model, apiKey);
}
```

### **Solution #2: Verify GitHub Actions Workflow**

The workflow uses:
```yaml
OPENAI_MODEL: ${{ vars.OPENAI_MODEL || 'gpt-4o-2024-08-06' }}
```

This should default to `gpt-4o-2024-08-06` if not set. But make sure:
1. The variable is actually being passed to the C# environment
2. The C# code reads it when initializing OpenAiClient

### **Solution #3: Check All LLM Calls**

All 4 LLM methods in OpenAiClient likely have this issue:
1. `ClassifyCategoryAsync` - line ~
2. `ExtractCasePacketAsync` - line 83 ❌ (FAILING)
3. `GenerateFollowUpQuestionsAsync` - line ~
4. `GenerateEngineerBriefAsync` - line ~

All need to ensure the model is passed when calling OpenAI API.

---

## 📋 STEP-BY-STEP FIX INSTRUCTIONS

### Step 1: Check OpenAiClient.cs

Navigate to: `src/SupportConcierge/Agents/OpenAiClient.cs`

Find the constructor and all methods that call `ChatClient.CompleteChatAsync()`

### Step 2: Ensure Model is Always Set

**Pattern to find:**
```csharp
await _client.CompleteChatAsync(messages, options);
```

**Pattern to replace with:**
```csharp
var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-2024-08-06";
await _client.CompleteChatAsync(messages, options, model);
```

OR better: Store model in field and reuse it:

```csharp
public class OpenAiClient
{
    private readonly string _model;
    private readonly ChatClient _client;
    
    public OpenAiClient()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-2024-08-06";
        
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY not set");
            
        _client = new ChatClient(_model, apiKey);
    }
    
    public async Task<string> ClassifyCategoryAsync(...)
    {
        var options = new ChatCompletionOptions();
        var response = await _client.CompleteChatAsync(messages, options);
        // ...
    }
}
```

### Step 3: Test Locally

Build locally with:
```powershell
dotnet build src/SupportConcierge/SupportConcierge.csproj --configuration Release
```

### Step 4: Push to GitHub

```powershell
cd D:\Projects\reddit\Reddit-ELT-Pipeline
# (Make changes to OpenAiClient.cs)
git add src/SupportConcierge/Agents/OpenAiClient.cs
git commit -m "Fix: Ensure OpenAI model parameter is always set"
git push origin main
```

### Step 5: Re-run Workflow

Create a new test issue or manually trigger the workflow. It should now work!

---

## 🎯 EXPECTED OUTCOME

After the fix:
- ✅ Build succeeds (it already does)
- ✅ Bot extracts fields using OpenAI API
- ✅ Bot scores completeness
- ✅ Bot posts engineer brief or follow-up questions
- ✅ Labels and assignee get added

**Success looks like:**
- Workflow status: ✅ GREEN
- Comment posted to issue with bot analysis
- Labels: `component: airflow`, `type: dag-failure`, `priority: high`
- Assigned to: `KeerthiYasasvi`

---

## 🔍 WHY THIS HAPPENED

The OpenAI API error message "you must provide a model parameter" tells us:

1. The API call WAS reaching OpenAI (network and auth work)
2. But the request was malformed (missing required field: `model`)
3. This is a common issue when the ChatClient doesn't know which model to use
4. The fix ensures we always tell it which model to use before making the request

---

## 📚 REFERENCE

**OpenAI .NET SDK ChatClient signature:**
```csharp
// Version 2.1.0 - Used in this project
ChatClient(string model, ApiKeyCredential apiKey)

// When calling methods:
await chatClient.CompleteChatAsync(messages, options);
// options can include ResponseFormat for structured outputs
```

The model is typically set during ChatClient initialization, but if it's null or not initialized properly, the API call will fail.

---

**Ready to fix?** Check the OpenAiClient.cs file and ensure the model parameter is set correctly in all LLM method calls.
