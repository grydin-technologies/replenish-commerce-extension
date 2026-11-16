namespace GrydIn.RealtimeWebhook.Services
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Signature sent in the x-signature header: HMAC-SHA256 over the exact UTF-8 request body,
    /// keyed with the store's secret, as a lowercase hex string with no prefix.
    /// Equivalent shell command:
    ///   printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //'
    /// </summary>
    public static class WebhookSignatureService
    {
        public static string ComputeSignature(string requestBody, string sharedSecret)
        {
            if (requestBody == null)
            {
                throw new ArgumentNullException(nameof(requestBody));
            }

            if (string.IsNullOrEmpty(sharedSecret))
            {
                throw new ArgumentException("Shared secret must not be empty.", nameof(sharedSecret));
            }

            var keyBytes = Encoding.UTF8.GetBytes(sharedSecret);
            var bodyBytes = Encoding.UTF8.GetBytes(requestBody);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                var hash = hmac.ComputeHash(bodyBytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
