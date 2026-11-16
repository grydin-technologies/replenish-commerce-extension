namespace GrydIn.RealtimeWebhook.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using GrydIn.RealtimeWebhook.Configuration;

    /// <summary>
    /// Delivers signed sale events to GrydIn without ever holding up the sale.
    ///
    /// Enqueue returns immediately. For every sale it:
    ///   1. writes the sale to the spool folder (so it cannot be lost),
    ///   2. sends it on a background task, retrying a few times with backoff,
    ///   3. deletes the file once GrydIn answers with a 2xx.
    ///
    /// If the immediate attempts fail, the file stays in the spool and a timer retries it later
    /// with growing delays (capped at 15 minutes) until it is delivered. Responses that retrying
    /// cannot fix (4xx other than 408 and 429) move the file to spool\failed instead.
    /// Delivery is at-least-once; GrydIn ignores repeated deliveries of the same order and line.
    /// </summary>
    public static class WebhookDispatcher
    {
        private const int MaxFilesPerReplay = 50;
        private static readonly TimeSpan MaxReplayDelay = TimeSpan.FromMinutes(15);

        private static readonly HttpClient Http = new HttpClient();
        private static readonly WebhookOptions Host = WebhookOptions.Load();
        private static readonly ConcurrentDictionary<string, byte> InFlight = new ConcurrentDictionary<string, byte>();

        private static int started;
        private static int replayRunning;
        private static Timer replayTimer;

        private enum Outcome
        {
            Delivered,
            Retry,
            Permanent,
        }

        /// <summary>Starts the timer that retries spooled sales. Safe to call repeatedly; never throws.</summary>
        public static void Start()
        {
            try
            {
                if (Interlocked.Exchange(ref started, 1) == 1)
                {
                    return;
                }

                var firstRun = Host.ReplayInterval < TimeSpan.FromSeconds(15) ? Host.ReplayInterval : TimeSpan.FromSeconds(15);
                replayTimer = new Timer(_ => { Task.Run(ReplayOnceAsync); }, null, firstRun, Host.ReplayInterval);
            }
            catch (Exception ex)
            {
                WebhookLogger.LogError("Could not start the spool replay timer.", ex);
            }
        }

        /// <summary>Queues one sale for delivery and returns immediately. Never throws.</summary>
        public static void Enqueue(string url, string body, string signature)
        {
            try
            {
                Start();

                var entry = new SpoolEntry
                {
                    Url = url,
                    Body = body,
                    Signature = signature,
                    CreatedUtc = DateTime.UtcNow,
                    NextAttemptUtc = DateTime.UtcNow,
                };

                var path = WebhookSpool.Save(Host.SpoolDirectory, entry);
                if (path != null)
                {
                    InFlight[path] = 0;
                }

                Task.Run(() => DeliverAsync(entry, path));
            }
            catch (Exception ex)
            {
                WebhookLogger.LogError("Could not queue a sale for delivery.", ex);
            }
        }

        private static async Task DeliverAsync(SpoolEntry entry, string path)
        {
            try
            {
                for (var attempt = 1; attempt <= Host.MaxRetryAttempts; attempt++)
                {
                    var outcome = await SendOnceAsync(entry).ConfigureAwait(false);
                    entry.Attempts = attempt;

                    if (outcome == Outcome.Delivered)
                    {
                        if (path != null)
                        {
                            WebhookSpool.Delete(path);
                        }

                        return;
                    }

                    if (outcome == Outcome.Permanent)
                    {
                        if (path != null)
                        {
                            WebhookSpool.MoveToFailed(path, "Rejected by the endpoint with a non-retryable response.");
                        }
                        else
                        {
                            WebhookLogger.LogError("Sale rejected by the endpoint and could not be stored for review.", null);
                        }

                        return;
                    }

                    if (attempt < Host.MaxRetryAttempts)
                    {
                        var delay = TimeSpan.FromMilliseconds(Host.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                        await Task.Delay(delay).ConfigureAwait(false);
                    }
                }

                // Immediate attempts used up: leave the sale in the spool for the replay timer.
                entry.NextAttemptUtc = DateTime.UtcNow + ReplayDelay(entry.Attempts);
                if (path != null && WebhookSpool.Update(path, entry))
                {
                    WebhookLogger.LogWarning($"Sale not delivered after {entry.Attempts} attempts; kept in the spool and will be retried.");
                }
                else
                {
                    WebhookLogger.LogError("Sale not delivered and NOT stored locally; it will not be retried.", null);
                }
            }
            catch (Exception ex)
            {
                WebhookLogger.LogError("Unexpected error while delivering a sale.", ex);
            }
            finally
            {
                if (path != null)
                {
                    InFlight.TryRemove(path, out _);
                }
            }
        }

        /// <summary>Retries sales waiting in the spool. Called by the timer; also callable from tests.</summary>
        internal static async Task ReplayOnceAsync()
        {
            if (Interlocked.Exchange(ref replayRunning, 1) == 1)
            {
                return;
            }

            try
            {
                var processed = 0;

                foreach (var path in WebhookSpool.ListPending(Host.SpoolDirectory))
                {
                    if (processed >= MaxFilesPerReplay)
                    {
                        break;
                    }

                    if (InFlight.ContainsKey(path))
                    {
                        continue;
                    }

                    SpoolEntry entry;
                    try
                    {
                        entry = WebhookSpool.Read(path);
                    }
                    catch (Exception ex)
                    {
                        WebhookLogger.LogError($"Spool file {path} is unreadable.", ex);
                        WebhookSpool.MoveToFailed(path, "Unreadable spool file.");
                        continue;
                    }

                    if (entry == null || string.IsNullOrWhiteSpace(entry.Url) || entry.Body == null || string.IsNullOrEmpty(entry.Signature))
                    {
                        WebhookSpool.MoveToFailed(path, "Incomplete spool file.");
                        continue;
                    }

                    if (entry.NextAttemptUtc > DateTime.UtcNow)
                    {
                        continue;
                    }

                    processed++;
                    var outcome = await SendOnceAsync(entry).ConfigureAwait(false);

                    if (outcome == Outcome.Delivered)
                    {
                        WebhookSpool.Delete(path);
                    }
                    else if (outcome == Outcome.Permanent)
                    {
                        WebhookSpool.MoveToFailed(path, "Rejected by the endpoint with a non-retryable response.");
                    }
                    else
                    {
                        entry.Attempts++;
                        entry.NextAttemptUtc = DateTime.UtcNow + ReplayDelay(entry.Attempts);
                        WebhookSpool.Update(path, entry);
                    }
                }
            }
            catch (Exception ex)
            {
                WebhookLogger.LogError("Unexpected error while retrying spooled sales.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref replayRunning, 0);
            }
        }

        private static async Task<Outcome> SendOnceAsync(SpoolEntry entry)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, entry.Url))
                {
                    request.Content = new StringContent(entry.Body, Encoding.UTF8, "application/json");
                    request.Headers.Add("x-signature", entry.Signature);

                    using (var cts = new CancellationTokenSource(Host.RequestTimeout))
                    using (var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false))
                    {
                        var code = (int)response.StatusCode;

                        if (code >= 200 && code < 300)
                        {
                            return Outcome.Delivered;
                        }

                        WebhookLogger.LogWarning($"Webhook returned HTTP {code}.");

                        // Timeouts, throttling and server errors can succeed later. Other 4xx
                        // answers (bad request, bad signature, unknown store) will not.
                        return (code == 408 || code == 429 || code >= 500) ? Outcome.Retry : Outcome.Permanent;
                    }
                }
            }
            catch (Exception ex)
            {
                WebhookLogger.LogWarning($"Webhook call failed: {ex.Message}");
                return Outcome.Retry;
            }
        }

        /// <summary>Delay before the next retry from the spool: grows with each failed replay, capped.</summary>
        private static TimeSpan ReplayDelay(int attempts)
        {
            var exponent = Math.Min(Math.Max(attempts - Host.MaxRetryAttempts, 0), 10);
            var ticks = Host.ReplayInterval.Ticks * (1L << exponent);
            return TimeSpan.FromTicks(Math.Min(ticks, MaxReplayDelay.Ticks));
        }
    }
}
