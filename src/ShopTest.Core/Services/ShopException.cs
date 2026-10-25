namespace ShopTest.Core.Services;

/// <summary>A user-facing validation or business-rule failure.</summary>
public sealed class ShopException(string message) : Exception(message);
