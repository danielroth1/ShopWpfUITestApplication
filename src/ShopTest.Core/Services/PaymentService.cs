using System.Globalization;

namespace ShopTest.Core.Services;

/// <summary>
/// Deterministic, offline payment simulator. No card data leaves the process and only the
/// brand plus last four digits are ever persisted.
/// </summary>
public sealed class PaymentService
{
    // Documented test cards (all Luhn valid).
    private static readonly Dictionary<string, string> DeclineRules = new(StringComparer.Ordinal)
    {
        ["0002"] = "Payment declined by the issuing bank.",
        ["0069"] = "The card has expired.",
        ["0119"] = "The payment processor returned an error.",
        ["9995"] = "Insufficient funds.",
    };

    public PaymentResult Charge(decimal amount, CardDetails card)
    {
        var digits = NormalizeDigits(card.Number);

        if (digits.Length is < 13 or > 19 || !PassesLuhn(digits))
        {
            throw new ShopException("Enter a valid card number.");
        }

        if (string.IsNullOrWhiteSpace(card.Holder))
        {
            throw new ShopException("Card holder name is required.");
        }

        if (!IsExpiryValid(card.Expiry, out var expiryError))
        {
            throw new ShopException(expiryError);
        }

        var cvc = (card.Cvc ?? string.Empty).Trim();
        if (cvc.Length is < 3 or > 4 || !cvc.All(char.IsDigit))
        {
            throw new ShopException("Enter a valid security code.");
        }

        var last4 = digits[^4..];
        var brand = DetectBrand(digits);
        var reference = $"PAY-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        if (DeclineRules.TryGetValue(last4, out var reason))
        {
            return new PaymentResult(false, reason, reference, brand, last4);
        }

        return new PaymentResult(true, null, reference, brand, last4);
    }

    public static string NormalizeDigits(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    public static bool PassesLuhn(string digits)
    {
        if (digits.Length < 2 || !digits.All(char.IsDigit))
        {
            return false;
        }

        var sum = 0;
        var alternate = false;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    public static string DetectBrand(string digits)
    {
        if (digits.StartsWith('4'))
        {
            return "Visa";
        }

        if (digits.StartsWith('5'))
        {
            return "Mastercard";
        }

        if (digits.StartsWith("34", StringComparison.Ordinal) || digits.StartsWith("37", StringComparison.Ordinal))
        {
            return "Amex";
        }

        return "Card";
    }

    private static bool IsExpiryValid(string? expiry, out string error)
    {
        error = string.Empty;
        var raw = (expiry ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (!DateTime.TryParseExact(raw, ["MM/yy", "MM/yyyy", "MM-yy", "MM-yyyy"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            error = "Enter the expiry date as MM/YY.";
            return false;
        }

        var expiresEndOfMonth = new DateTime(parsed.Year, parsed.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(1)
            .AddSeconds(-1);

        if (expiresEndOfMonth < DateTime.UtcNow)
        {
            error = "The card has expired.";
            return false;
        }

        return true;
    }
}
