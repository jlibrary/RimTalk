using System;
using RimTalk.Util;
using UnityEngine.Networking;

namespace RimTalk.Client;

/// <summary>
/// Applies <see cref="OpenCodeSession"/> headers to RimTalk's outgoing HTTP requests.
/// Kept apart from the pure header logic so unit tests can run without Unity assemblies.
///
/// <para>
/// Requests are issued from thread-pool threads (<c>TalkService</c> uses <c>Task.Run</c>), so
/// this class only reads the settings object and never touches Unity APIs, never writes
/// settings, and never lets a failure escape: header injection must not be able to break a
/// dialogue request.
/// </para>
/// </summary>
public static class OpenCodeSessionInjector
{
    private static string _sessionSeed;

    /// <summary>Stable per-installation seed the wire token is derived from.</summary>
    private static string SessionSeed => _sessionSeed ??= OpenCodeSession.DefaultSessionSeed();

    /// <summary>
    /// The token currently put on the wire; shown in the settings UI for verification. Never throws,
    /// so a platform quirk can at worst hide the label instead of breaking the settings window.
    /// </summary>
    public static string CurrentToken
    {
        get
        {
            try
            {
                return OpenCodeSession.WireSessionId(SessionSeed);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not derive the OpenCode session token: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Adds the OpenCode session headers to <paramref name="request"/> when the user enabled the
    /// feature and the request targets an OpenCode endpoint. Only request headers are touched.
    /// </summary>
    /// <param name="request">Request being prepared.</param>
    /// <param name="url">Its target URL.</param>
    /// <returns>The injected token, or null when no header was added.</returns>
    public static string Apply(UnityWebRequest request, string url)
    {
        if (request == null) return null;

        try
        {
            var settings = Settings.Get();
            if (settings == null || !settings.OpenCodeSessionHeaders) return null;

            var headers = OpenCodeSession.BuildHeaders(url, SessionSeed, true);
            if (headers == null) return null;

            foreach (var header in headers)
            {
                // Never clobber a header an explicit configuration already set.
                if (HasHeader(request, header.Key)) continue;
                request.SetRequestHeader(header.Key, header.Value);
            }

            string token = headers[OpenCodeSession.HeaderNames[0]];
            Logger.Debug($"OpenCode session headers added ({OpenCodeSession.HeaderNames[0]}={token}) for {url}");
            return token;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Could not add OpenCode session headers: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// UnityWebRequest.GetRequestHeader throws for headers it does not know, so a missing header
    /// is reported as "not present" instead of letting the exception escape.
    /// </summary>
    private static bool HasHeader(UnityWebRequest request, string name)
    {
        try
        {
            return !string.IsNullOrEmpty(request.GetRequestHeader(name));
        }
        catch
        {
            return false;
        }
    }
}
