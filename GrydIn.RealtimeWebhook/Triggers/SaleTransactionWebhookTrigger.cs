namespace GrydIn.RealtimeWebhook.Triggers
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using GrydIn.RealtimeWebhook.Configuration;
    using GrydIn.RealtimeWebhook.Services;
    using Microsoft.Dynamics.Commerce.Runtime;
    using Microsoft.Dynamics.Commerce.Runtime.DataServices.Messages;
    using Microsoft.Dynamics.Commerce.Runtime.Messages;

    /// <summary>
    /// Sends the sale to GrydIn at the moment the Commerce runtime saves it.
    ///
    /// Implemented as a post-trigger on SaveSalesTransactionDataRequest, so the runtime's own
    /// save logic runs untouched and this code only observes a transaction that has already
    /// been saved. The trigger builds the request, then hands it to WebhookDispatcher, which
    /// writes it to a local spool folder and delivers it on a background task. The trigger
    /// does not wait for the network, so a slow or unreachable endpoint cannot slow down or
    /// fail a sale, and an unexpected error is caught here and never reaches the runtime.
    /// </summary>
    public class SaleTransactionWebhookTrigger : IRequestTriggerAsync
    {
        static SaleTransactionWebhookTrigger()
        {
            // Starts the timer that retries sales still waiting in the spool, for example
            // after a restart. Never throws.
            WebhookDispatcher.Start();
        }

        public IEnumerable<Type> SupportedRequestTypes
        {
            get
            {
                return new[] { typeof(SaveSalesTransactionDataRequest) };
            }
        }

        /// <summary>Nothing to do before the save; required by the interface.</summary>
        public Task OnExecuting(Request request)
        {
            return Task.CompletedTask;
        }

        public Task OnExecuted(Request request, Response response)
        {
            try
            {
                if (!(request is SaveSalesTransactionDataRequest saveRequest) || saveRequest.SalesTransaction == null)
                {
                    return Task.CompletedTask;
                }

                var options = ChannelWebhookOptionsResolver.Resolve(request.RequestContext);

                if (!options.IsConfigured)
                {
                    return Task.CompletedTask;
                }

                var payload = SaleTransactionMapper.Map(saveRequest.SalesTransaction, request.RequestContext);

                if (payload.Items.Count == 0)
                {
                    return Task.CompletedTask;
                }

                var body = WebhookJson.Serialize(payload);
                var signature = WebhookSignatureService.ComputeSignature(body, options.SharedSecret);

                WebhookDispatcher.Enqueue(options.IngestUrl, body, signature);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"[GrydIn.RealtimeWebhook] Unexpected error in trigger: {ex}");
            }

            return Task.CompletedTask;
        }
    }
}
