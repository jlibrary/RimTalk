#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Client;
using Xunit;

namespace RimTalk.Tests;

/// <summary>
/// Tests for the OpenCode gateway session-header support.
///
/// Every expected token below was produced by running the reference implementation
/// (gausszhou/dsh-opencode-session-id, <c>lib/index.js</c> → <c>nanoidOf</c>) under node, so the C#
/// port is checked byte for byte against it for every token length the reference can produce.
/// </summary>
public class OpenCodeSessionTests
{
    [Theory]
    [InlineData("e820d21d-1c3a-4b7f-9c0e-a309f722a3bc", "uA3uHW33", "uA3uHW33d6ZV")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e", "yBLh7bZE", "yBLh7bZEF9YJ")]
    [InlineData("8f14e45fceea167a5a36dedd4bea2543", "QD3UXF5J", "QD3UXF5JUy9k")]
    [InlineData("abc", "ungWv48B", "ungWv48BzpBQ")]
    [InlineData("RimTalk-test-session-1", "D04P58fr", "D04P58frcZ2l")]
    public void NanoId_MatchesReferenceImplementation(string input, string expected8, string expected12)
    {
        Assert.Equal(expected8, OpenCodeSession.NanoId(input));
        Assert.Equal(expected12, OpenCodeSession.NanoId(input, 12));
    }

    [Fact]
    public void NanoId_MatchesReferenceImplementation_AtLongestLengthTheReferenceSupports()
    {
        // 42 characters (252 bits) is the longest token the reference emits before its own
        // re-hash branch throws (it assigns to a `const`), and the values still agree there.
        Assert.Equal(
            "D04P58frcZ2lzAKhjjq2qQLma1yIe7lKxEIvb9Y0v4",
            OpenCodeSession.NanoId("RimTalk-test-session-1", 42));
    }

    [Fact]
    public void NanoId_UrlSafeAlphabet_MatchesReferenceImplementation()
    {
        Assert.Equal(
            "uA-3uHW33d6ZVTXN",
            OpenCodeSession.NanoId("e820d21d-1c3a-4b7f-9c0e-a309f722a3bc", 16, OpenCodeSession.UrlsafeAlphabet));
    }

    [Fact]
    public void NanoId_ExtendsPastTheFirstDigestBlock_Deterministically()
    {
        // Past 42 characters the reference implementation throws; this port keeps hashing with a
        // counter instead, so the token stays well formed, deterministic, and still agrees with the
        // reference for the part the reference can produce.
        string token = OpenCodeSession.NanoId("RimTalk-test-session-1", 64);

        Assert.Equal(64, token.Length);
        Assert.Equal(token, OpenCodeSession.NanoId("RimTalk-test-session-1", 64));
        Assert.All(token, c => Assert.Contains(c, OpenCodeSession.AlphanumericAlphabet));
        Assert.Equal("D04P58frcZ2lzAKhjjq2qQLma1yIe7lKxEIvb9Y0v4", token.Substring(0, 42));
    }

    [Fact]
    public void NanoId_IsDeterministicAndAlphanumeric()
    {
        string first = OpenCodeSession.NanoId("seed-A", 8);
        string second = OpenCodeSession.NanoId("seed-A", 8);
        string other = OpenCodeSession.NanoId("seed-B", 8);

        Assert.Equal(8, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.All(first, c => Assert.Contains(c, OpenCodeSession.AlphanumericAlphabet));
    }

    [Fact]
    public void NanoId_RejectsDegenerateAlphabet()
    {
        Assert.Throws<ArgumentException>(() => OpenCodeSession.NanoId("seed", 8, "a"));
        Assert.Throws<ArgumentException>(() => OpenCodeSession.NanoId("seed", 8, ""));
    }

    [Fact]
    public void WireSessionId_StripsTheDshSessionPrefix()
    {
        const string uuid = "99f0ff66-c8da-4f3a-9ac9-abdc575372bd";

        Assert.Equal("0EVW1Lhg", OpenCodeSession.WireSessionId("session-" + uuid));
        Assert.Equal(OpenCodeSession.NanoId(uuid), OpenCodeSession.WireSessionId("session-" + uuid));
        Assert.Equal(OpenCodeSession.NanoId(uuid), OpenCodeSession.WireSessionId(uuid));
        // Only a leading label is stripped.
        Assert.Equal("a-session-b", OpenCodeSession.StripSessionPrefix("a-session-b"));
    }

    [Theory]
    [InlineData("https://opencode.ai/zen/go/v1/chat/completions", true)]
    [InlineData("https://opencode.ai/zen/v1/models", true)]
    [InlineData("https://opencode.ai", true)]
    [InlineData("https://sub.opencode.ai/x", true)]
    [InlineData("https://OpenCode.AI/zen/v1/models", true)]
    [InlineData("https://notopencode.ai/x", false)]
    [InlineData("https://opencode.ai.evil.com/x", false)]
    [InlineData("https://api.openai.com/v1/chat/completions", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not a url", false)]
    public void IsOpenCodeEndpoint_MatchesTheHostSuffixOnly(string url, bool expected)
    {
        Assert.Equal(expected, OpenCodeSession.IsOpenCodeEndpoint(url));
    }

    [Fact]
    public void HeaderNames_AreTheReferenceSet()
    {
        Assert.Equal(
            new[] { "x-opencode-session", "x-session-affinity", "x-client-request-id", "x-session-id" },
            OpenCodeSession.HeaderNames);
    }

    [Fact]
    public void BuildHeaders_AddsTheSessionFamily_ToOpenCodeEndpoints()
    {
        Dictionary<string, string>? headers = OpenCodeSession.BuildHeaders(
            "https://opencode.ai/zen/go/v1/chat/completions", "RimTalk-test-session-1", enabled: true);

        Assert.NotNull(headers);
        Assert.Equal(OpenCodeSession.HeaderNames.Length, headers!.Count);
        foreach (string name in OpenCodeSession.HeaderNames)
        {
            Assert.True(headers.ContainsKey(name), $"missing header {name}");
            Assert.Equal("D04P58fr", headers[name]);
        }
    }

    [Fact]
    public void BuildHeaders_IsNull_WhenTheUserDisabledTheFeature()
    {
        Assert.Null(OpenCodeSession.BuildHeaders(
            "https://opencode.ai/zen/go/v1/chat/completions", "seed", enabled: false));
    }

    [Fact]
    public void BuildHeaders_IsNull_ForEveryOtherHost()
    {
        Assert.Null(OpenCodeSession.BuildHeaders(
            "https://api.openai.com/v1/chat/completions", "seed", enabled: true));
        Assert.Null(OpenCodeSession.BuildHeaders(
            "https://openrouter.ai/api/v1/chat/completions", "seed", enabled: true));
    }

    [Fact]
    public void BuildHeaders_IsNull_WithoutASessionId()
    {
        Assert.Null(OpenCodeSession.BuildHeaders(
            "https://opencode.ai/zen/go/v1/chat/completions", "   ", enabled: true));
        Assert.Null(OpenCodeSession.BuildHeaders(
            "https://opencode.ai/zen/go/v1/chat/completions", null, enabled: true));
    }

    [Fact]
    public void BuildHeaders_UsesTheHostSuffixMatch_Only()
    {
        // A custom base URL that is not on opencode.ai never gets the headers; host matching is the
        // single gate, which is also why the feature cannot affect other providers.
        Assert.Null(OpenCodeSession.BuildHeaders(
            "http://127.0.0.1:8080/v1/chat/completions", "seed", enabled: true));
    }

    [Fact]
    public void DefaultSessionSeed_IsStableAndUsable()
    {
        string seed = OpenCodeSession.DefaultSessionSeed();

        Assert.False(string.IsNullOrWhiteSpace(seed));
        Assert.Equal(seed, OpenCodeSession.DefaultSessionSeed());
        Assert.Equal(OpenCodeSession.DefaultTokenLength, OpenCodeSession.WireSessionId(seed).Length);
    }
}

/// <summary>
/// The provider registry wiring the header injection depends on: both OpenCode entries must point at
/// the gateway host the injector matches.
/// </summary>
public class OpenCodeProviderRegistryTests
{
    [Fact]
    public void OpenCodeProviders_PointAtTheGatewayEndpoints()
    {
        Assert.Equal("OpenCode Zen", AIProvider.OpenCodeZen.GetLabel());
        Assert.Equal("https://opencode.ai/zen/v1/chat/completions", AIProvider.OpenCodeZen.GetEndpointUrl());
        Assert.Equal("https://opencode.ai/zen/v1/models", AIProvider.OpenCodeZen.GetListModelsUrl());

        Assert.Equal("OpenCode GO", AIProvider.OpenCodeGo.GetLabel());
        Assert.Equal("https://opencode.ai/zen/go/v1/chat/completions", AIProvider.OpenCodeGo.GetEndpointUrl());
        Assert.Equal("https://opencode.ai/zen/go/v1/models", AIProvider.OpenCodeGo.GetListModelsUrl());
    }

    [Fact]
    public void EveryOpenCodeProvider_LivesOnTheHostTheInjectorMatches()
    {
        foreach (var provider in new[] { AIProvider.OpenCodeZen, AIProvider.OpenCodeGo })
        {
            string endpoint = provider.GetEndpointUrl();
            string models = provider.GetListModelsUrl();

            Assert.True(OpenCodeSession.IsOpenCodeEndpoint(endpoint), $"{provider} endpoint {endpoint} is not on the OpenCode host");
            Assert.True(OpenCodeSession.IsOpenCodeEndpoint(models), $"{provider} models URL {models} is not on the OpenCode host");
        }
    }

    [Fact]
    public void NoOtherProvider_LivesOnTheOpenCodeHost()
    {
        foreach (var entry in AIProviderRegistry.Defs.Where(entry => entry.Key != AIProvider.OpenCodeZen && entry.Key != AIProvider.OpenCodeGo))
        {
            if (string.IsNullOrEmpty(entry.Value.EndpointUrl)) continue;

            Assert.False(
                OpenCodeSession.IsOpenCodeEndpoint(entry.Value.EndpointUrl),
                $"{entry.Key} unexpectedly points at the OpenCode host, so it would receive the session headers");
        }
    }
}
