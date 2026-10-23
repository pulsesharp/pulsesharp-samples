using System.Collections.Concurrent;
using Shop.Contracts;

namespace Shop.Api.Receipts;

/// <summary>
/// A pretend receipt printer. Turn it off with <c>POST /admin/printer</c> to watch PulseSharp retry,
/// stop one stream after a few tries, and carry on once the printer works again.
/// </summary>
public sealed class ReceiptPrinter(ILogger<ReceiptPrinter> logger)
{
    private readonly ConcurrentQueue<string> _printed = new();

    public bool IsBroken { get; set; }

    public IReadOnlyCollection<string> Printed
    {
        get
        {
            return _printed;
        }
    }

    public void Print(SaleCompleted sale)
    {
        if (IsBroken)
        {
            throw new InvalidOperationException("The receipt printer is out of paper.");
        }

        var receipt = $"{sale.Till}: {string.Join(", ", sale.Lines.Select(line => $"{line.Quantity} x {line.Name}"))} = {sale.Total:0.00}";
        _printed.Enqueue(receipt);
        while (_printed.Count > 20)
        {
            _printed.TryDequeue(out _);
        }

        logger.LogInformation("Receipt printed. {Receipt}", receipt);
    }
}
