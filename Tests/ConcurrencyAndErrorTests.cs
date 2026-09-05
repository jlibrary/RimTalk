using System;
using System.Collections.Generic;
using System.Threading;
using RimTalk.Prompt;
using RimTalk.Util;
using Xunit;

namespace RimTalk.Tests;

public class ConcurrencyAndErrorTests
{
    [Fact]
    public void ErrorUtil_ExtractErrorMessage_HandlesStandardAndNonStandardErrorFormats()
    {
        // 1. OpenAI / OpenRouter standard wrapped: {"error": {"message": "Invalid API key provided", "code": 401}}
        string openAiErr = "{\"error\": {\"message\": \"Invalid API key provided\", \"code\": 401}}";
        string msg1 = ErrorUtil.ExtractErrorMessage(openAiErr);
        Assert.Equal("[401] Invalid API key provided", msg1);

        // 2. Flat ErrorDetail: {"message": "Rate limit exceeded", "code": 429}
        string flatErr = "{\"message\": \"Rate limit exceeded\", \"code\": 429}";
        string msg2 = ErrorUtil.ExtractErrorMessage(flatErr);
        Assert.Equal("[429] Rate limit exceeded", msg2);

        // 3. Array wrapped: [{"error": {"message": "Array error"}}]
        string arrayErr = "[{\"error\": {\"message\": \"Array error\"}}]";
        string msg3 = ErrorUtil.ExtractErrorMessage(arrayErr);
        Assert.Equal("Array error", msg3);

        // 4. String error from local proxies / Ollama: {"error": "model not found"}
        string stringErr = "{\"error\": \"model not found\"}";
        string msg4 = ErrorUtil.ExtractErrorMessage(stringErr);
        Assert.Equal("model not found", msg4);

        // 5. Detail string from FastAPI / custom proxies: {"detail": "Unauthorized request"}
        string detailErr = "{\"detail\": \"Unauthorized request\"}";
        string msg5 = ErrorUtil.ExtractErrorMessage(detailErr);
        Assert.Equal("Unauthorized request", msg5);
    }

    [Fact]
    public void SessionVariableStore_IsThreadSafeUnderConcurrentAccess()
    {
        SessionVariableStore.Reset();

        var threads = new List<Thread>();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        for (int t = 0; t < 10; t++)
        {
            int threadId = t;
            threads.Add(new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 200; i++)
                    {
                        string key = $"var_{threadId}_{i % 10}";
                        SessionVariableStore.SetVar(key, $"val_{threadId}_{i}");
                        var retrieved = SessionVariableStore.GetVar(key);
                        Assert.NotNull(retrieved);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join();

        Assert.Empty(exceptions);
    }

    [Fact]
    public void VariableStore_IsThreadSafeUnderConcurrentReadWrites()
    {
        var store = new VariableStore();
        var threads = new List<Thread>();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        for (int t = 0; t < 10; t++)
        {
            int threadId = t;
            threads.Add(new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 200; i++)
                    {
                        string key = $"key_{threadId}_{i % 5}";
                        store.SetVar(key, $"value_{i}");
                        _ = store.GetVar(key);
                        _ = store.HasVar(key);
                        _ = store.GetAllVariables();
                        _ = store.Count;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join();

        Assert.Empty(exceptions);
    }
}
