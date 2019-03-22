﻿using System;

using Decos.Diagnostics.Trace;
using Decos.Diagnostics.Trace.Slack;

namespace Decos.Diagnostics
{
    /// <summary>
    /// Provides a set of static methods for registering a Slack trace listener
    /// with a log factory.
    /// </summary>
    public static class TraceSourceLogFactoryBuilderExtensions
    {
        /// <summary>
        /// Enables logging to Slack.
        /// </summary>
        /// <param name="builder">
        /// The log factory builder to register the trace listener with.
        /// </param>
        /// <param name="webhookAddress">
        /// The address of the Slack Incoming Webhook to post messages to.
        /// </param>
        /// <returns>A reference to the builder.</returns>
        public static TraceSourceLogFactoryBuilder AddSlack(this TraceSourceLogFactoryBuilder builder, string webhookAddress)
            => builder.AddSlack(new Uri(webhookAddress, UriKind.Absolute));

        /// <summary>
        /// Enables logging to Slack.
        /// </summary>
        /// <param name="builder">
        /// The log factory builder to register the trace listener with.
        /// </param>
        /// <param name="webhookAddress">
        /// The address of the Slack Incoming Webhook to post messages to.
        /// </param>
        /// <returns>A reference to the builder.</returns>
        public static TraceSourceLogFactoryBuilder AddSlack(this TraceSourceLogFactoryBuilder builder, Uri webhookAddress)
            => builder.AddTraceListener(new SlackTraceListener(webhookAddress));
    }
}