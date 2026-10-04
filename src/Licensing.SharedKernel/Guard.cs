namespace Licensing.SharedKernel;

public static class Guard
{
    public static string NotEmpty(string? value, string field, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(Error.Validation("VALIDATION_FAILED", $"{field} is required."));
        value = value.Trim();
        if (value.Length > maxLength)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", $"{field} must be at most {maxLength} characters."));
        return value;
    }
}
