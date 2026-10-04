using System.Net.Mail;
using System.Text.RegularExpressions;
using Licensing.SharedKernel;

namespace Licensing.Domain.Common;

public sealed class Money : ValueObject
{
    private Money() { Currency = "USD"; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0) throw new DomainException(Error.Validation("VALIDATION_FAILED", "Amount cannot be negative."));
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Currency must be a 3-letter ISO code."));
        Amount = decimal.Round(amount, 2);
        Currency = currency.Trim().ToUpperInvariant();
    }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Amount; yield return Currency; }
    public override string ToString() => $"{Amount:0.00} {Currency}";
}

public sealed class DateRange : ValueObject
{
    public DateRange(DateTimeOffset start, DateTimeOffset? end)
    {
        if (end is not null && end <= start)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "End date must be after start date."));
        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }
    /// <summary>Null means open-ended (lifetime).</summary>
    public DateTimeOffset? End { get; }
    public bool IsLifetime => End is null;
    public bool Contains(DateTimeOffset at) => at >= Start && (End is null || at < End);

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Start; yield return End; }
}

public sealed class EmailAddress : ValueObject
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress Create(string? value)
    {
        var v = Guard.NotEmpty(value, "Email", 256).ToLowerInvariant();
        if (!MailAddress.TryCreate(v, out var parsed) || parsed.Address != v)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Email is not valid."));
        return new EmailAddress(v);
    }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}

/// <summary>A numeric limit where null means "unlimited".</summary>
public sealed class PlanLimit : ValueObject
{
    private PlanLimit(int? value) => Value = value;

    public int? Value { get; }
    public bool IsUnlimited => Value is null;

    public static PlanLimit Unlimited { get; } = new(null);

    public static PlanLimit Of(int value) => value < 1
        ? throw new DomainException(Error.Validation("VALIDATION_FAILED", "Limit must be at least 1 (or unlimited)."))
        : new PlanLimit(value);

    public static PlanLimit From(int? value) => value is null ? Unlimited : Of(value.Value);

    public bool Allows(int current) => IsUnlimited || current < Value;

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
}

public sealed partial class FeatureCode : ValueObject
{
    private FeatureCode(string value) => Value = value;

    public string Value { get; }

    public static FeatureCode Create(string? value)
    {
        var v = Guard.NotEmpty(value, "Feature code", 64).ToLowerInvariant();
        if (!Pattern().IsMatch(v))
            throw new DomainException(Error.Validation("VALIDATION_FAILED", $"Feature code '{v}' must be lowercase letters, digits, '.', '_' or '-'."));
        return new FeatureCode(v);
    }

    [GeneratedRegex("^[a-z][a-z0-9._-]*$")]
    private static partial Regex Pattern();

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value;
}
