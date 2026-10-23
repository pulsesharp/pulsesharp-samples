using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using PulseSharp;
using PulseSharp.Sync;
using Shop.Contracts;
using Till.Desktop.Data;

namespace Till.Desktop.ViewModels;

public sealed class ProductViewModel(MenuItem item, Action<MenuItem> add) : ViewModelBase
{
    private static readonly string[] Colors =
    ["#0B8A63", "#B87400", "#3B6FD4", "#8A4FD1", "#D4503B", "#1D8FA3", "#C2417A", "#5B7F2B", "#6A55C8", "#A8541F"];

    public string Name { get; } = item.Name;

    public string Initial { get; } = item.Name[..1].ToUpperInvariant();

    public IBrush Accent { get; } = new SolidColorBrush(Color.Parse(Colors[ColorIndex(item.Sku)]));

    public bool IsLow { get; } = item.LowLeft is not null;

    private static int ColorIndex(string sku)
    {
        var hash = 17u;
        foreach (var letter in sku)
        {
            hash = unchecked((hash * 131) + letter);
        }

        return (int)(hash % (uint)Colors.Length);
    }

    public string Price { get; } = item.Price.ToString("0.00");

    public string Note { get; } = item.LowLeft is { } left ? $"Low: {left} left" : string.Empty;

    public RelayCommand Add { get; } = new(() => add(item));
}

public sealed record CartLineViewModel(string Name, int Quantity, string LineTotal);

public sealed record RecentSaleViewModel(string Summary, string Total, string Time);

public sealed class MainViewModel : ViewModelBase
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#0B8A63"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#B87400"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#5D6762"));

    private readonly TillOptions _options;
    private readonly TillDatabase _database;
    private readonly IPulse _pulse;
    private readonly IPulseSync _sync;
    private readonly List<(MenuItem Item, int Quantity)> _cart = [];
    private string _total = "0.00";
    private string _payText = "Pay";
    private bool _cartIsEmpty = true;
    private string _statusText = "Starting";
    private string _statusDetail = string.Empty;
    private IBrush _statusBrush = Grey;
    private string _warning = string.Empty;

    public MainViewModel(TillOptions options, TillDatabase database, IPulse pulse, IPulseSync sync)
    {
        _options = options;
        _database = database;
        _pulse = pulse;
        _sync = sync;

        Pay = new RelayCommand(PayAsync);
        Clear = new RelayCommand(ClearCart);
        SyncNow = new RelayCommand(SyncNowAsync);

        TillHost.ScreenDataChanged += () => Dispatcher.UIThread.Post(Reload);

        // IPulseSync has no "changed" event yet, so the status bar looks at it once a second.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => RefreshStatus();
        timer.Start();

        Reload();
        RefreshStatus();
    }

    public string Title
    {
        get
        {
            return $"Corner Coffee  -  {_options.Name}";
        }
    }

    public string ShopName
    {
        get
        {
            return "Corner Coffee";
        }
    }

    public string TillName
    {
        get
        {
            return _options.Name;
        }
    }

    public string PayText
    {
        get { return _payText; }
        private set { Set(ref _payText, value); }
    }

    public bool CartIsEmpty
    {
        get { return _cartIsEmpty; }
        private set { Set(ref _cartIsEmpty, value); }
    }

    public ObservableCollection<ProductViewModel> Menu { get; } = [];

    public ObservableCollection<CartLineViewModel> Cart { get; } = [];

    public ObservableCollection<RecentSaleViewModel> RecentSales { get; } = [];

    public RelayCommand Pay { get; }

    public RelayCommand Clear { get; }

    public RelayCommand SyncNow { get; }

    public string Total
    {
        get { return _total; }
        private set { Set(ref _total, value); }
    }

    public string StatusText
    {
        get { return _statusText; }
        private set { Set(ref _statusText, value); }
    }

    public string StatusDetail
    {
        get { return _statusDetail; }
        private set { Set(ref _statusDetail, value); }
    }

    public IBrush StatusBrush
    {
        get { return _statusBrush; }
        private set { Set(ref _statusBrush, value); }
    }

    public string Warning
    {
        get { return _warning; }
        private set { Set(ref _warning, value); }
    }

    private void Reload()
    {
        var items = _database.LoadMenu();
        Menu.Clear();
        foreach (var item in items)
        {
            Menu.Add(new ProductViewModel(item, AddToCart));
        }

        RecentSales.Clear();
        foreach (var sale in _database.LoadRecentSales(6))
        {
            RecentSales.Add(new RecentSaleViewModel(sale.Summary, sale.Total.ToString("0.00"), sale.SoldAt.ToLocalTime().ToString("HH:mm:ss")));
        }

        var low = items.Where(item => item.LowLeft is not null).Select(item => $"{item.Name}: {item.LowLeft} left").ToList();
        Warning = low.Count == 0 ? string.Empty : "Running low (from the cloud): " + string.Join(", ", low);
        RefreshStatus();
    }

    private void AddToCart(MenuItem item)
    {
        var index = _cart.FindIndex(line => line.Item.Sku == item.Sku);
        if (index >= 0)
        {
            _cart[index] = (item, _cart[index].Quantity + 1);
        }
        else
        {
            _cart.Add((item, 1));
        }

        ShowCart();
    }

    private void ClearCart()
    {
        _cart.Clear();
        ShowCart();
    }

    private void ShowCart()
    {
        Cart.Clear();
        foreach (var (item, quantity) in _cart)
        {
            Cart.Add(new CartLineViewModel(item.Name, quantity, (item.Price * quantity).ToString("0.00")));
        }

        Total = _cart.Sum(line => line.Item.Price * line.Quantity).ToString("0.00");
        CartIsEmpty = _cart.Count == 0;
        PayText = _cart.Count == 0 ? "Pay" : $"Pay {Total}";
    }

    private async Task PayAsync()
    {
        if (_cart.Count == 0)
        {
            return;
        }

        var lines = _cart.Select(line => new SaleLine(line.Item.Sku, line.Item.Name, line.Quantity, line.Item.Price)).ToList();
        var sale = new SaleCompleted(Guid.NewGuid(), _options.Name, lines, lines.Sum(line => line.Quantity * line.UnitPrice), DateTimeOffset.UtcNow);

        // The sale is saved in the till's own database first, so it counts even with no internet.
        await _pulse.Emit(Streams.Sales(_options.Name, DateOnly.FromDateTime(sale.SoldAt.UtcDateTime)), sale);
        ClearCart();
    }

    private async Task SyncNowAsync()
    {
        StatusText = "Syncing";
        await _sync.SyncNow();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = _sync.Status;
        var waiting = _database.CountSalesSince(status.LastSyncedAt);

        if (status.Online)
        {
            StatusBrush = Green;
            StatusText = waiting == 0 ? "Online" : "Sending";
            StatusDetail = waiting == 0 ? $"Last synced {status.LastSyncedAt?.ToLocalTime():HH:mm:ss}" : $"{waiting} sale(s) go out at the next sync";
        }
        else if (status.LastError is null)
        {
            StatusBrush = Grey;
            StatusText = "Connecting";
            StatusDetail = "Looking for the cloud at " + _options.CloudAddress;
        }
        else
        {
            StatusBrush = Amber;
            StatusText = "Offline";
            StatusDetail = (waiting == 0 ? "Nothing is waiting" : $"{waiting} sale(s) are waiting") + " - selling still works. It tries again by itself.";
        }
    }
}
