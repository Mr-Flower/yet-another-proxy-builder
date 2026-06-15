using System.Net;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// App-wide HTTP infrastructure. Every service shares ONE connection pool (a single
    /// <see cref="SocketsHttpHandler"/>) instead of newing its own <see cref="HttpClient"/>: many
    /// short-lived clients each open their own pool and exhaust sockets under load (a deck import fires
    /// dozens of parallel image downloads). Per-service clients wrap the shared handler with
    /// disposeHandler:false so each can still carry its own default headers without clobbering the others.
    /// </summary>
    public static class SharedHttp
    {
        // Scryfall asks for a descriptive User-Agent with a contact URL; a missing/default one can be
        // rejected. The same UA is fine for every other endpoint we hit.
        public const string UserAgent =
            "YetAnotherProxyBuilder/1.0 (+https://github.com/Mr-Flower/yet-another-proxy-builder)";

        // One handler == one connection pool for the whole process.
        private static readonly SocketsHttpHandler Handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 16,
            AutomaticDecompression = DecompressionMethods.All,
        };

        /// <summary>A client over the shared pool with the standard User-Agent. The caller may add its
        /// own default headers (e.g. Accept) freely — each client owns its own header collection.</summary>
        public static HttpClient CreateClient()
        {
            // disposeHandler:false — the shared Handler must outlive any individual client.
            var client = new HttpClient(Handler, disposeHandler: false);
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            return client;
        }

        /// <summary>A client for the Scryfall API: shared pool plus request throttling and a single 429
        /// retry, per https://scryfall.com/docs/api (≥50–100 ms between requests, honour Retry-After).</summary>
        public static HttpClient CreateScryfallClient()
        {
            var throttle = new ThrottlingHandler(TimeSpan.FromMilliseconds(100)) { InnerHandler = Handler };
            var client = new HttpClient(throttle, disposeHandler: false);
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            return client;
        }
    }

    /// <summary>
    /// Spaces outgoing requests by at least <c>minInterval</c> and retries once on HTTP 429, waiting for
    /// the server's Retry-After when present. Serialises requests through a gate so the spacing holds even
    /// when many calls run concurrently. The request body is buffered up front so the retry can re-send it
    /// (an <see cref="HttpRequestMessage"/> can only be sent once).
    /// </summary>
    internal sealed class ThrottlingHandler : DelegatingHandler
    {
        private readonly TimeSpan _minInterval;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _lastRequestUtc = DateTime.MinValue;

        public ThrottlingHandler(TimeSpan minInterval) => _minInterval = minInterval;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            byte[]? body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);

            await _gate.WaitAsync(ct);
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    var since = DateTime.UtcNow - _lastRequestUtc;
                    if (since < _minInterval)
                        await Task.Delay(_minInterval - since, ct);

                    var response = await base.SendAsync(Clone(request, body), ct);
                    _lastRequestUtc = DateTime.UtcNow;

                    if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 1)
                        return response;

                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    response.Dispose();
                    await Task.Delay(wait, ct);
                }
            }
            finally { _gate.Release(); }
        }

        private static HttpRequestMessage Clone(HttpRequestMessage req, byte[]? body)
        {
            var clone = new HttpRequestMessage(req.Method, req.RequestUri) { Version = req.Version };
            foreach (var h in req.Headers)
                clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
            if (body != null)
            {
                clone.Content = new ByteArrayContent(body);
                if (req.Content != null)
                    foreach (var h in req.Content.Headers)
                        clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            return clone;
        }
    }
}
