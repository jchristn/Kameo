namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>An in-process HTTP handler so usage tests never contact a provider.</summary>
    internal sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _Respond;
        private readonly object _Gate = new object();

        internal FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _Respond = respond;

        internal int Calls { get; private set; }
        internal Uri? LastUri { get; private set; }
        internal HttpMethod? LastMethod { get; private set; }
        internal Dictionary<string, string> LastHeaders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_Gate)
            {
                Calls++;
                LastUri = request.RequestUri;
                LastMethod = request.Method;
                LastHeaders.Clear();
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers) LastHeaders[header.Key] = string.Join(",", header.Value);
            }
            // Responders run outside the lock so concurrency tests can overlap requests.
            return Task.FromResult(_Respond(request));
        }
    }
}
