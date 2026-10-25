using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopTest.App.Services;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class CheckoutViewModel : ViewModelBase
{
    private readonly CartService _cart;
    private readonly OrderService _orders;
    private readonly SessionService _session;
    private readonly IViewNavigator _navigator;
    private readonly string _idempotencyKey = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string shippingAddress = "12 Harbour Road, 2000 Sydney";

    [ObservableProperty]
    private string cardNumber = "4242 4242 4242 4242";

    [ObservableProperty]
    private string cardHolder = "Demo Client";

    [ObservableProperty]
    private string expiry = "12/30";

    [ObservableProperty]
    private string cvc = "123";

    [ObservableProperty]
    private decimal total;

    [ObservableProperty]
    private int itemCount;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    public CheckoutViewModel(CartService cart, OrderService orders, SessionService session, IViewNavigator navigator)
    {
        _cart = cart;
        _orders = orders;
        _session = session;
        _navigator = navigator;
    }

    public override string Title => "Checkout";

    public override async Task LoadAsync()
    {
        var cart = await _cart.GetAsync(_session.UserId);
        var totals = OrderTotals.From(cart.Subtotal);
        Total = totals.Total;
        ItemCount = cart.ItemCount;
    }

    [RelayCommand]
    private async Task PayAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var order = await _orders.PlaceOrderAsync(
                _session.UserId,
                ShippingAddress,
                new CardDetails(CardNumber, CardHolder, Expiry, Cvc),
                _idempotencyKey);

            if (order.Status == Core.Domain.OrderStatus.PaymentFailed)
            {
                ErrorMessage = "The payment was declined. Your cart was kept so you can try again.";
                return;
            }

            await _navigator.ShowConfirmationAsync(order);
        }
        catch (ShopException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BackToCartAsync() => await _navigator.ShowCartAsync();
}
