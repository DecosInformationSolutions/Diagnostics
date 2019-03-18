﻿using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Jil;

namespace Decos.Diagnostics
{
    /// <summary>
    /// Represents a client of sending data to a Logstash HTTP input plugin.
    /// </summary>
    public class HttpLogstashClient
    {
        private static readonly HttpClient httpClient = new HttpClient();

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpLogstashClient"/> class with the specified endpoint.
        /// </summary>
        /// <param name="logstashEndpoint">The URI at which an Logstash HTTP input plugin is listening for POST requests.</param>
        public HttpLogstashClient(Uri logstashEndpoint)
        {
            Endpoint = logstashEndpoint;
        }

        /// <summary>
        /// Gets the URI at which an Logstash HTTP input plugin is listening for POST requests.
        /// </summary>
        public Uri Endpoint { get; }

        /// <summary>
        /// Sends a log entry to Logstash.
        /// </summary>
        /// <param name="logEntry">The log entry to send.</param>
        public Task LogAsync(LogEntry logEntry)
            => LogAsync(logEntry, CancellationToken.None);

        /// <summary>
        /// Sends a log entry to Logstash.
        /// </summary>
        /// <param name="logEntry">The log entry to send.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task LogAsync(LogEntry logEntry, CancellationToken cancellationToken)
        {
            var json = JSON.Serialize(logEntry, Options.ISO8601CamelCase);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await httpClient.PostAsync(Endpoint, content, cancellationToken).ConfigureAwait(false);
        }
    }
}
