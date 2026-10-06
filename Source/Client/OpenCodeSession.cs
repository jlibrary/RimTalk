using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace RimTalk.Client;

/// <summary>
/// OpenCode gateway session headers — pure logic, free of Unity and Verse types so the
/// unit tests can link this file directly.
///
/// <para>
/// Why this exists: the OpenCode gateway behind the "opencode" (Zen) and "opencode-go"
/// providers rejects chat-completion requests that do not carry
/// <c>x-opencode-session</c> with HTTP 400 <c>{"type":"MissingSessionID"}</c>. opencode's
/// own client also sends the session-affinity family (<c>x-session-affinity</c>,
/// <c>x-client-request-id</c>, <c>x-session-id</c>). RimTalk sent none of them, so an
/// OpenCode endpoint could never answer a dialogue request.
/// </para>
///
/// <para>
/// The wire token mirrors the dsh plugin
/// <see href="https://github.com/gausszhou/dsh-opencode-session-id"/>: the session id is
/// SHA-256 hashed into an opencode-style alphanumeric nanoid(8) before it is sent, so the
/// token is deterministic, one-way, and shaped like the token opencode itself uses.
/// </para>
/// </summary>
public static class OpenCodeSession
{
    /// <summary>Host suffix whose requests carry the session headers (subdomains included).</summary>
    public const string HostSuffix = "opencode.ai";

    /// <summary>Label dsh session ids carry; stripped before hashing, exactly like the reference plugin.</summary>
    public const string SessionPrefix = "session-";

    /// <summary>opencode's own session token length (its web token looks like "QBgzdhtO").</summary>
    public const int DefaultTokenLength = 8;

    /// <summary>Pure alphanumeric alphabet (62 chars) — no "_" or "-" a gateway regex might reject.</summary>
    public const string AlphanumericAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>nanoid's classic URL-safe alphabet (64 chars, used only if a caller asks for it).</summary>
    public const string UrlsafeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";

    /// <summary>
    /// Headers opencode itself emits: <c>x-opencode-session</c> on the gateway branch (the one the
    /// gateway keys sessions on) plus the affinity family sent on every other provider branch.
    /// </summary>
    public static readonly string[] HeaderNames =
    [
        "x-opencode-session",
        "x-session-affinity",
        "x-client-request-id",
        "x-session-id"
    ];

    /// <summary>
    /// True when <paramref name="url"/> targets the OpenCode gateway: the bare host or any
    /// subdomain of it. Mirrors the reference plugin's host-suffix match, so
    /// "opencode.ai.evil.com" and "notopencode.ai" do not match.
    /// </summary>
    public static bool IsOpenCodeEndpoint(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;

        string host = uri.Host;
        if (string.IsNullOrEmpty(host)) return false;

        host = host.ToLowerInvariant();
        return host == HostSuffix || host.EndsWith("." + HostSuffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Strips the dsh "session-" label so only the id itself is hashed. Ids without the
    /// prefix pass through unchanged.
    /// </summary>
    public static string StripSessionPrefix(string sessionId)
    {
        if (sessionId == null) return null;
        return sessionId.StartsWith(SessionPrefix, StringComparison.Ordinal)
            ? sessionId.Substring(SessionPrefix.Length)
            : sessionId;
    }

    /// <summary>The value that goes on the wire for a raw session id.</summary>
    public static string WireSessionId(string rawSessionId)
    {
        return NanoId(StripSessionPrefix(rawSessionId) ?? string.Empty);
    }

    /// <summary>
    /// Deterministic SHA-256 → nanoid conversion: walk the digest 6 bits at a time and emit
    /// <paramref name="length"/> characters (default 8). The same input always maps to the same
    /// token, so a gateway that keys on the token can attribute a RimTalk installation. Byte for
    /// byte equivalent to the reference plugin's <c>nanoidOf()</c> (including its rejection
    /// sampling and its re-hash-with-counter when a digest block runs out).
    /// </summary>
    public static string NanoId(string input, int length = DefaultTokenLength, string alphabet = AlphanumericAlphabet)
    {
        if (length < 1) length = 1;
        if (string.IsNullOrEmpty(alphabet) || alphabet.Length < 2)
            throw new ArgumentException("Alphabet needs at least two characters.", nameof(alphabet));

        string source = input ?? string.Empty;
        byte[] digest = Sha256(source);
        int size = alphabet.Length;
        var token = new StringBuilder(length);
        int bit = 0;

        while (token.Length < length)
        {
            if (bit + 6 > digest.Length * 8)
            {
                // Ran past this digest block (reachable only through heavy rejection
                // sampling): hash again with a counter for fresh bytes.
                digest = Sha256(source + "#" + token.Length);
                bit = 0;
            }

            int byteIndex = bit >> 3;
            int bitOffset = bit & 7;
            int value;
            if (bitOffset <= 2)
            {
                // The 6 bits fit inside one digest byte.
                value = (digest[byteIndex] >> (2 - bitOffset)) & 0x3f;
            }
            else
            {
                // The 6 bits span two digest bytes.
                int next = byteIndex + 1 < digest.Length ? digest[byteIndex + 1] : 0;
                value = ((digest[byteIndex] & ((1 << (8 - bitOffset)) - 1)) << (bitOffset - 2)) | (next >> (10 - bitOffset));
            }

            value &= 0x3f;
            bit += 6;
            if (value >= size) continue; // rejection sampling keeps the alphabet uniform
            token.Append(alphabet[value]);
        }

        return token.ToString();
    }

    /// <summary>
    /// The headers to add for one request, or null when nothing should be sent. Called from the
    /// request path, so it never throws and is free of Unity/Verse types.
    /// </summary>
    /// <param name="url">Request target; matched against <see cref="HostSuffix"/>.</param>
    /// <param name="rawSessionId">Session id to derive the wire token from.</param>
    /// <param name="enabled">User setting: the feature is opt-out, never implicit.</param>
    public static Dictionary<string, string> BuildHeaders(string url, string rawSessionId, bool enabled)
    {
        if (!enabled) return null;
        if (string.IsNullOrWhiteSpace(rawSessionId)) return null;
        if (!IsOpenCodeEndpoint(url)) return null;

        string token = WireSessionId(rawSessionId.Trim());
        if (token.Length == 0) return null;

        var headers = new Dictionary<string, string>(HeaderNames.Length);
        foreach (string name in HeaderNames) headers[name] = token;
        return headers;
    }

    /// <summary>
    /// Stable, private seed for installations that never stored an id: the machine and user
    /// name. It is hashed one-way before it reaches the wire, so the gateway sees only an
    /// opaque opencode-shaped token and never the raw seed.
    /// </summary>
    public static string DefaultSessionSeed()
    {
        string machine;
        string user;
        try { machine = Environment.MachineName; } catch { machine = "unknown"; }
        try { user = Environment.UserName; } catch { user = "unknown"; }
        return $"rimtalk:{machine}|{user}";
    }

    private static byte[] Sha256(string input)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(input));
    }
}
