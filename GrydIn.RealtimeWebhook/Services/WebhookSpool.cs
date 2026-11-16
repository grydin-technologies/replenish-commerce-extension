namespace GrydIn.RealtimeWebhook.Services
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>One sale waiting to be delivered. The signing secret is never stored here.</summary>
    internal sealed class SpoolEntry
    {
        public string Url { get; set; }

        /// <summary>Exact request body that was signed.</summary>
        public string Body { get; set; }

        /// <summary>Signature computed when the sale was committed.</summary>
        public string Signature { get; set; }

        public DateTime CreatedUtc { get; set; }

        public int Attempts { get; set; }

        public DateTime NextAttemptUtc { get; set; }
    }

    /// <summary>
    /// Folder-based store for sales that have not been delivered yet. Every sale is written here
    /// before the first send and removed after a successful delivery, so a sale survives a
    /// failed send or a restart of the CSU process.
    ///
    ///   spool\*.json           waiting to be sent (retried automatically)
    ///   spool\failed\*.json    rejected by GrydIn with a non-retryable response; not retried
    ///
    /// To resend a file from the failed folder after the cause is fixed, move it back to spool\.
    /// </summary>
    internal static class WebhookSpool
    {
        private const string FailedFolder = "failed";

        /// <summary>Writes the entry and returns its path, or null when the folder cannot be written.</summary>
        public static string Save(string directory, SpoolEntry entry)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var name = $"{DateTime.UtcNow.Ticks:D19}_{Guid.NewGuid():N}.json";
                var path = Path.Combine(directory, name);
                WriteAtomic(path, WebhookJson.Serialize(entry));
                return path;
            }
            catch (Exception ex)
            {
                WebhookLogger.LogError("Could not write the sale to the local spool folder.", ex);
                return null;
            }
        }

        public static SpoolEntry Read(string path)
        {
            return WebhookJson.Deserialize<SpoolEntry>(File.ReadAllText(path, Encoding.UTF8));
        }

        public static bool Update(string path, SpoolEntry entry)
        {
            try
            {
                WriteAtomic(path, WebhookJson.Serialize(entry));
                return true;
            }
            catch (Exception ex)
            {
                WebhookLogger.LogError("Could not update a spooled sale.", ex);
                return false;
            }
        }

        public static void Delete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                WebhookLogger.LogWarning($"Could not delete spool file {path}: {ex.Message}");
            }
        }

        public static void MoveToFailed(string path, string reason)
        {
            try
            {
                var failedDirectory = Path.Combine(Path.GetDirectoryName(path), FailedFolder);
                Directory.CreateDirectory(failedDirectory);
                var target = Path.Combine(failedDirectory, Path.GetFileName(path));
                File.Move(path, target);
                File.WriteAllText(target + ".reason.txt", reason ?? string.Empty, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                WebhookLogger.LogError($"Could not move {path} to the failed folder.", ex);
            }
        }

        /// <summary>Files waiting to be sent, oldest first. Does not include the failed folder.</summary>
        public static string[] ListPending(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return new string[0];
            }

            var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        private static void WriteAtomic(string path, string content)
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }
    }
}
