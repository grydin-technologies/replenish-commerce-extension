namespace GrydIn.RealtimeWebhook.Services
{
    using System;
    using System.Globalization;
    using GrydIn.RealtimeWebhook.Models;
    using Microsoft.Dynamics.Commerce.Runtime;
    using Microsoft.Dynamics.Commerce.Runtime.DataModel;

    /// <summary>
    /// Maps a CRT SalesTransaction to the payload sent to GrydIn. This is the only file that
    /// reads the SalesTransaction; if a field comes from somewhere else in your environment
    /// (for example a different item identifier), change it here.
    ///
    /// Members read from the SDK, to be confirmed against the SDK version you build with:
    ///   SalesTransaction.Id, SalesTransaction.SalesLines
    ///   SalesLine.LineNumber, SalesLine.ItemId, SalesLine.Quantity, SalesLine.IsVoided
    ///   SalesLine.Variant (Size, SizeId)
    /// </summary>
    public static class SaleTransactionMapper
    {
        public static SaleTransactionPayload Map(SalesTransaction transaction, RequestContext context)
        {
            if (transaction == null)
            {
                throw new ArgumentNullException(nameof(transaction));
            }

            var payload = new SaleTransactionPayload
            {
                OrderNumber = transaction.Id,
            };

            if (transaction.SalesLines == null)
            {
                return payload;
            }

            foreach (var line in transaction.SalesLines)
            {
                // A line removed by the cashier before checkout is not a sale.
                if (line.IsVoided)
                {
                    continue;
                }

                payload.Items.Add(new SaleLinePayload
                {
                    LineNo = FormatLineNumber(line.LineNumber),
                    PartNo = line.ItemId,
                    QtySold = ToWholeQuantity(line.Quantity),
                    QtyOnHand = InventoryOnHandResolver.Resolve(context, transaction, line),
                    Size = ResolveSize(line),
                });
            }

            return payload;
        }

        private static string FormatLineNumber(decimal lineNumber)
        {
            return decimal.Truncate(lineNumber).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Whole units, sign preserved (returns are negative).</summary>
        private static int ToWholeQuantity(decimal quantity)
        {
            return decimal.ToInt32(decimal.Round(quantity, MidpointRounding.AwayFromZero));
        }

        private static string ResolveSize(SalesLine line)
        {
            var variant = line.Variant;
            if (variant == null)
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(variant.Size) ? variant.Size : variant.SizeId;
        }
    }
}
