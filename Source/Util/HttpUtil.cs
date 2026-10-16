using System;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.Service;
using Verse;

namespace RimTalk.Util;

// Shared HttpClient. Replaces UnityWebRequest, which blocks plain http to non-localhost hosts,
// rejects self-signed certificates, and is unavailable for streaming in RimWorld 1.7.
public static class HttpUtil
{
    // Single instance so sockets are reused instead of exhausted.
    public static readonly HttpClient Client = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            // Mono's CookieContainer reads the machine domain name, which throws "Illegal byte sequence"
            // on non-ASCII computer names. API calls don't need cookies.
            UseCookies = false,
#if !V1_5
            // Self-signed certificates are accepted only for local and LAN hosts.
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
                errors == SslPolicyErrors.None || LocalHost.IsLocal(msg.RequestUri?.DnsSafeHost)
#endif
        };
#if V1_5
        AcceptLocalCertificates();
#endif
        // Timeouts are enforced per request through CancellationTokenSource.
        return new HttpClient(new LoopbackHandler(handler)) { Timeout = Timeout.InfiniteTimeSpan };
    }

#if V1_5
    // RimWorld 1.5's Mono throws NotImplementedException from HttpClientHandler.ServerCertificateCustomValidationCallback.
    // Its handler goes through HttpWebRequest, which honors this global callback instead; chain to any existing one.
    private static void AcceptLocalCertificates()
    {
        var previous = System.Net.ServicePointManager.ServerCertificateValidationCallback;
        System.Net.ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
            errors == SslPolicyErrors.None
            || (sender is System.Net.HttpWebRequest req && LocalHost.IsLocal(req.RequestUri.DnsSafeHost))
            || (previous != null && previous(sender, cert, chain, errors));
    }
#endif

    // Mono fails to resolve "localhost" (NameResolutionFailure) on non-ASCII computer names; an IP literal skips DNS.
    private sealed class LoopbackHandler : DelegatingHandler
    {
        public LoopbackHandler(HttpMessageHandler inner) : base(inner) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri;
            if (uri != null && uri.IsAbsoluteUri && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                request.RequestUri = new UriBuilder(uri) { Host = "127.0.0.1" }.Uri;
            return base.SendAsync(request, ct);
        }
    }

    /// <summary>
    /// Sends a request, honoring game unload, fast-track cancellation and a connect timeout.
    /// Returns null if the game was unloaded.
    /// </summary>
    public static async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage request,
        float connectTimeout,
        CancellationTokenSource timeoutCts,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        if (Current.Game == null) return null;
        if (AIService.IsCancellationRequested())
            throw new OperationCanceledException("Request canceled by fast-track request.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, AIService.CurrentCancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(connectTimeout));

        try
        {
            return await Client.SendAsync(request, completionOption, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (Current.Game == null) return null;
            if (AIService.IsCancellationRequested())
                throw new OperationCanceledException("Request canceled by fast-track request.");
            if (timeoutCts.IsCancellationRequested)
                throw new TimeoutException($"Connection timed out ({connectTimeout}s)");
            throw;
        }
    }

    /// <summary>
    /// Reads a response stream chunk by chunk, failing if no data arrives within readTimeout.
    /// </summary>
    public static async Task ReadStreamWithTimeoutAsync(
        Stream stream,
        float readTimeout,
        CancellationTokenSource timeoutCts,
        Action<byte[], int> onData)
    {
        byte[] buffer = new byte[4096];
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, AIService.CurrentCancellationToken);

        while (true)
        {
            if (Current.Game == null) return;
            if (AIService.IsCancellationRequested())
                throw new OperationCanceledException("Request canceled by fast-track request.");

            // Restart the stall timer before every read.
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(readTimeout));
            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (Current.Game == null) return;
                if (AIService.IsCancellationRequested())
                    throw new OperationCanceledException("Request canceled by fast-track request.");
                if (timeoutCts.IsCancellationRequested)
                    throw new TimeoutException($"Read timed out ({readTimeout}s)");
                throw;
            }

            if (bytesRead <= 0) break;
            onData(buffer, bytesRead);
        }
    }
}
