namespace GrydIn.RealtimeWebhook.Services
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Logging seam. Writes to System.Diagnostics.Trace. Replace the bodies with the Commerce
    /// runtime's own logging if your team prefers; nothing else needs to change.
    /// </summary>
    internal static class WebhookLogger
    {
        public static void LogInfo(string message)
        {
            Trace.TraceInformation($"[GrydIn.RealtimeWebhook] {message}");
        }

        public static void LogWarning(string message)
        {
            Trace.TraceWarning($"[GrydIn.RealtimeWebhook] {message}");
        }

        public static void LogError(string message, Exception ex)
        {
            Trace.TraceError($"[GrydIn.RealtimeWebhook] {message} {ex}");
        }
    }
}
