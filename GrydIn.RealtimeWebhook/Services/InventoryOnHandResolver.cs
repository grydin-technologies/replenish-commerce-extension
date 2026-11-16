namespace GrydIn.RealtimeWebhook.Services
{
    using Microsoft.Dynamics.Commerce.Runtime;
    using Microsoft.Dynamics.Commerce.Runtime.DataModel;

    /// <summary>
    /// Supplies the on-hand quantity (qtyOnHand) for a sold line.
    ///
    /// THIS METHOD MUST BE COMPLETED BEFORE GO-LIVE. The sale itself does not carry an on-hand
    /// quantity, so it has to come from your inventory source. Until Resolve returns a value,
    /// qtyOnHand is left out of the request and GrydIn will not accept the line, because the
    /// field is required.
    ///
    /// Things to decide with your Dynamics team:
    ///   - which inventory source to read (channel database availability, or another source),
    ///   - whether the number should be before or after this sale,
    ///   - how fresh that source is, since this call runs while the sale is being saved and
    ///     must stay fast. Return null rather than wait on a slow lookup.
    /// </summary>
    internal static class InventoryOnHandResolver
    {
        /// <summary>
        /// Returns the on-hand quantity for the line's item and variant at the sale's store,
        /// or null when it cannot be determined.
        /// </summary>
        public static int? Resolve(RequestContext context, SalesTransaction transaction, SalesLine line)
        {
            return null;
        }
    }
}
