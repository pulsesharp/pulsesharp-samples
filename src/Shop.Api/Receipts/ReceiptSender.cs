using PulseSharp;
using Shop.Contracts;

namespace Shop.Api.Receipts;

/// <summary>Prints a receipt for every sale. When the printer fails, PulseSharp retries this sale and leaves the others alone.</summary>
public sealed class ReceiptSender(ReceiptPrinter printer) : IEventHandler<SaleCompleted>
{
    public Task Handle(SaleCompleted @event, EventContext context, CancellationToken cancellationToken)
    {
        printer.Print(@event);
        return Task.CompletedTask;
    }
}
