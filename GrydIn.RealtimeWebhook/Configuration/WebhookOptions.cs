namespace GrydIn.RealtimeWebhook.Configuration
{
    using System;
    using System.IO;

    /// <summary>
    /// Settings for the GrydIn ingest webhook. Nothing is hardcoded: values come from
    /// environment variables of the CSU host, or per channel (see ChannelWebhookOptionsResolver).
    ///
    ///   GRYDIN_WEBHOOK_URL            Full ingest URL, https://{host}/v1/webhooks/{storeId}/ingest
    ///   GRYDIN_WEBHOOK_SECRET         Per-store signing secret issued by GrydIn
    ///   GRYDIN_WEBHOOK_MAX_RETRIES    Immediate delivery attempts per sale (default 3)
    ///   GRYDIN_WEBHOOK_SPOOL_DIR      Folder for sales that could not be delivered yet
    ///                                 (default: a GrydIn.RealtimeWebhook\spool folder under the temp path;
    ///                                 set this to a persistent folder in production)
    ///   GRYDIN_WEBHOOK_REPLAY_SECONDS How often the spool is retried (default 60)
    /// </summary>
    public sealed class WebhookOptions
    {
        /// <summary>Full ingest URL.</summary>
        public string IngestUrl { get; private set; }

        /// <summary>Shared HMAC secret used to sign the request body.</summary>
        public string SharedSecret { get; private set; }

        /// <summary>Immediate delivery attempts before the sale is left in the spool for later retries.</summary>
        public int MaxRetryAttempts { get; private set; } = 3;

        /// <summary>Base delay for exponential backoff between immediate attempts.</summary>
        public TimeSpan RetryBaseDelay { get; private set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>Timeout of a single HTTP call.</summary>
        public TimeSpan RequestTimeout { get; private set; } = TimeSpan.FromSeconds(5);

        /// <summary>Folder where undelivered sales are kept until they are sent.</summary>
        public string SpoolDirectory { get; private set; } = DefaultSpoolDirectory();

        /// <summary>How often the spool folder is retried. Also the base of the retry backoff.</summary>
        public TimeSpan ReplayInterval { get; private set; } = TimeSpan.FromSeconds(60);

        private const string IngestUrlEnvVar = "GRYDIN_WEBHOOK_URL";
        private const string SharedSecretEnvVar = "GRYDIN_WEBHOOK_SECRET";
        private const string MaxRetryEnvVar = "GRYDIN_WEBHOOK_MAX_RETRIES";
        private const string SpoolDirEnvVar = "GRYDIN_WEBHOOK_SPOOL_DIR";
        private const string ReplaySecondsEnvVar = "GRYDIN_WEBHOOK_REPLAY_SECONDS";

        public static WebhookOptions Load()
        {
            var options = new WebhookOptions
            {
                IngestUrl = Environment.GetEnvironmentVariable(IngestUrlEnvVar),
                SharedSecret = Environment.GetEnvironmentVariable(SharedSecretEnvVar),
            };

            if (int.TryParse(Environment.GetEnvironmentVariable(MaxRetryEnvVar), out var maxRetry) && maxRetry > 0)
            {
                options.MaxRetryAttempts = maxRetry;
            }

            var spoolDir = Environment.GetEnvironmentVariable(SpoolDirEnvVar);
            if (!string.IsNullOrWhiteSpace(spoolDir))
            {
                options.SpoolDirectory = spoolDir;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable(ReplaySecondsEnvVar), out var replaySeconds) && replaySeconds > 0)
            {
                options.ReplayInterval = TimeSpan.FromSeconds(replaySeconds);
            }

            return options;
        }

        /// <summary>
        /// Options for one channel: URL and secret come from that channel, everything else
        /// is inherited from the host-level configuration.
        /// </summary>
        internal static WebhookOptions CreateOverride(string ingestUrl, string sharedSecret, WebhookOptions inheritFrom)
        {
            var options = new WebhookOptions
            {
                IngestUrl = ingestUrl,
                SharedSecret = sharedSecret,
            };

            if (inheritFrom != null)
            {
                options.MaxRetryAttempts = inheritFrom.MaxRetryAttempts;
                options.RetryBaseDelay = inheritFrom.RetryBaseDelay;
                options.RequestTimeout = inheritFrom.RequestTimeout;
                options.SpoolDirectory = inheritFrom.SpoolDirectory;
                options.ReplayInterval = inheritFrom.ReplayInterval;
            }

            return options;
        }

        /// <summary>
        /// True only when both URL and secret are configured. When false the extension does
        /// nothing, so an unconfigured environment is silent.
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(this.IngestUrl) && !string.IsNullOrWhiteSpace(this.SharedSecret);

        private static string DefaultSpoolDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "GrydIn.RealtimeWebhook", "spool");
        }
    }
}
