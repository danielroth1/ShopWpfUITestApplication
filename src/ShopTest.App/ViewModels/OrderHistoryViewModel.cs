using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopTest.App.Services;
using ShopTest.Core.Domain;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class OrderHistoryViewModel : ViewModelBase
{
    private static readonly CardDetails DemoCard = new("4242 4242 4242 4242", "Demo Client", "12/30", "123");

    private readonly OrderService _orders;
    private readonly SessionService _session;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool isEmpty = true;

    public OrderHistoryViewModel(OrderService orders, SessionService session)
    {
        _orders = orders;
        _session = session;
    }

    public override string Title => "Order history";

    public ObservableCollection<OrderRowViewModel> Orders { get; } = new();

    public override async Task LoadAsync() => await ReloadAsync();

    [RelayCommand]
    private async Task RefreshAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        var orders = await _orders.GetHistoryAsync(_session.UserId);

        Orders.Clear();
        foreach (var order in orders)
        {
            Orders.Add(new OrderRowViewModel(order, CancelAsync, RetryAsync));
        }

        IsEmpty = Orders.Count == 0;
    }

    private async Task CancelAsync(OrderDto order)
    {
        try
        {
            await _orders.CancelAsync(order.Id, _session.UserId);
            StatusMessage = $"Order #{order.Id} cancelled.";
        }
        catch (ShopException ex)
        {
            StatusMessage = ex.Message;
        }

        await ReloadAsync();
    }

    private async Task RetryAsync(OrderDto order)
    {
        try
        {
            var updated = await _orders.RetryPaymentAsync(order.Id, _session.UserId, DemoCard);
            StatusMessage = updated.Status == OrderStatus.Paid
                ? $"Order #{order.Id} paid with the demo card."
                : "The payment was declined again.";
        }
        catch (ShopException ex)
        {
            StatusMessage = ex.Message;
        }

        await ReloadAsync();
    }
}

public sealed partial class OrderRowViewModel : ObservableObject
{
    private readonly Func<OrderDto, Task> _cancel;
    private readonly Func<OrderDto, Task> _retry;

    public OrderRowViewModel(OrderDto order, Func<OrderDto, Task> cancel, Func<OrderDto, Task> retry)
    {
        Order = order;
        _cancel = cancel;
        _retry = retry;
    }

    public OrderDto Order { get; }

    public string Header => $"Order #{Order.Id}";

    public string PlacedText => Order.PlacedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm");

    public string StatusText => OrderService.StatusLabel(Order.Status);

    public string TotalText => Order.Total.ToString("C");

    public string PaymentText => Order.PaymentStatus.ToString();

    public IReadOnlyList<OrderLineDto> Lines => Order.Lines;

    public IReadOnlyList<string> Timeline => Order.History
        .Select(h => $"{h.ChangedAt.ToLocalTime():dd MMM HH:mm} · {OrderService.StatusLabel(h.To)} — {h.Reason}")
        .ToList();

    public bool CanCancel => Order.Status is OrderStatus.PendingPayment or OrderStatus.PaymentFailed or OrderStatus.Paid;

    public bool CanRetry => Order.Status == OrderStatus.PaymentFailed;

    [RelayCommand]
    private Task CancelOrderAsync() => _cancel(Order);

    [RelayCommand]
    private Task RetryPaymentAsync() => _retry(Order);
}
