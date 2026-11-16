namespace GrydIn.RealtimeWebhook.Configuration
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using GrydIn.RealtimeWebhook.Services;
    using Microsoft.Dynamics.Commerce.Runtime;
    using Microsoft.Dynamics.Commerce.Runtime.Data;
    using Microsoft.Dynamics.Commerce.Runtime.DataModel;

    /// <summary>
    /// Resolves the webhook URL and secret for the channel (store) a sale was rung on.
    ///
    /// GrydIn issues one URL and one secret per store, and a single CSU can serve several
    /// stores, so one machine-level value is not enough. The values are read from two channel
    /// configuration extension properties, named exactly:
    ///
    ///     GRYDIN_WEBHOOK_URL      = https://{host}/v1/webhooks/{storeId}/ingest
    ///     GRYDIN_WEBHOOK_SECRET   = the secret issued for that store
    ///
    /// Add them for each store in scope in Commerce headquarters and run the channel
    /// configuration sync so they reach the channel database. When a channel has no values,
    /// the environment variables of the CSU host (see WebhookOptions) are used instead.
    ///
    /// Resolved values are cached per channel for the life of the CSU process so that no
    /// configuration lookup is added to the checkout path. After changing a URL or secret in
    /// headquarters, restart the CSU (or call Invalidate) to pick up the new value.
    ///
    /// The whole lookup is wrapped in a catch, so any problem falls back to the environment
    /// variables and never affects checkout. Built against SDK 9.53.26002.2, where
    /// RequestContext.GetChannelConfiguration() is synchronous.
    /// </summary>
    internal static class ChannelWebhookOptionsResolver
    {
        private const string IngestUrlPropertyName = "GRYDIN_WEBHOOK_URL";
        private const string SharedSecretPropertyName = "GRYDIN_WEBHOOK_SECRET";

        private static readonly ConcurrentDictionary<long, WebhookOptions> Cache =
            new ConcurrentDictionary<long, WebhookOptions>();

        private static readonly WebhookOptions EnvironmentFallback = WebhookOptions.Load();

        /// <summary>
        /// Returns the options for this request's channel. Never throws and never returns null.
        /// The result may be unconfigured, in which case the caller does nothing.
        /// </summary>
        public static WebhookOptions Resolve(RequestContext context)
        {
            if (context == null)
            {
                return EnvironmentFallback;
            }

            try
            {
                var channelId = context.GetPrincipal()?.ChannelId ?? 0;

                if (channelId == 0)
                {
                    return EnvironmentFallback;
                }

                if (Cache.TryGetValue(channelId, out var cached))
                {
                    return cached;
                }

                var resolved = BuildFromChannelConfiguration(context.GetChannelConfiguration()) ?? EnvironmentFallback;
                Cache.TryAdd(channelId, resolved);
                return resolved;
            }
            catch (Exception ex)
            {
                WebhookLogger.LogWarning(
                    $"Per-channel webhook configuration lookup failed, using environment variables: {ex.Message}");
                return EnvironmentFallback;
            }
        }

        /// <summary>Clears cached values so the next sale per channel re-reads the configuration.</summary>
        public static void Invalidate()
        {
            Cache.Clear();
        }

        /// <summary>
        /// Builds options from the channel's extension properties. Returns null unless BOTH
        /// values are present, so a half-configured channel falls back cleanly.
        /// </summary>
        private static WebhookOptions BuildFromChannelConfiguration(ChannelConfiguration channelConfiguration)
        {
            var extensionProperties = channelConfiguration?.ExtensionProperties;

            if (extensionProperties == null)
            {
                return null;
            }

            var ingestUrl = ReadStringProperty(extensionProperties, IngestUrlPropertyName);
            var sharedSecret = ReadStringProperty(extensionProperties, SharedSecretPropertyName);

            if (string.IsNullOrWhiteSpace(ingestUrl) || string.IsNullOrWhiteSpace(sharedSecret))
            {
                return null;
            }

            return WebhookOptions.CreateOverride(ingestUrl, sharedSecret, EnvironmentFallback);
        }

        private static string ReadStringProperty(
            System.Collections.Generic.IEnumerable<CommerceProperty> properties,
            string name)
        {
            var match = properties.FirstOrDefault(
                property => property != null &&
                            string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase));

            return match?.Value?.StringValue;
        }
    }
}
