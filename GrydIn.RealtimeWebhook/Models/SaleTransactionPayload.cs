namespace GrydIn.RealtimeWebhook.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of the request sent to the GrydIn ingest endpoint for each completed sale.
    /// Serialized in camelCase: orderNumber, items[].lineNo / partNo / qtySold / qtyOnHand / size.
    /// GrydIn maps these names per store, so renaming a property here only requires the
    /// matching mapping change on the GrydIn side.
    /// </summary>
    public sealed class SaleTransactionPayload
    {
        public string OrderNumber { get; set; }

        public List<SaleLinePayload> Items { get; set; } = new List<SaleLinePayload>();
    }

    public sealed class SaleLinePayload
    {
        /// <summary>Line number within the order. Unique per order.</summary>
        public string LineNo { get; set; }

        /// <summary>Item identifier of the sold product.</summary>
        public string PartNo { get; set; }

        /// <summary>Quantity sold. Negative for returns.</summary>
        public int QtySold { get; set; }

        /// <summary>On-hand quantity. Omitted from the JSON when it cannot be resolved.</summary>
        public int? QtyOnHand { get; set; }

        public string Size { get; set; }
    }
}
