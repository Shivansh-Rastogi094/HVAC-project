using System.Text.RegularExpressions;

namespace HvacConnect.Core.Validation;

/// <summary>Shared validation rules used by the console workflows.</summary>
public static class InputValidator
{
    public static (bool isValid, string errorMessage) ValidateRequiredText(string? value, string fieldName, int minLength, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (false, $"{fieldName} is required.");

        var text = value.Trim();
        if (text.Length < minLength || text.Length > maxLength)
            return (false, $"{fieldName} must be between {minLength} and {maxLength} characters.");

        return (true, string.Empty);
    }

    public static (bool isValid, string errorMessage) ValidateEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 254 ||
            !Regex.IsMatch(value.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant))
            return (false, "Enter a valid email address.");

        return (true, string.Empty);
    }

    public static (bool isValid, string errorMessage) ValidatePassword(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8 ||
            !value.Any(char.IsUpper) || !value.Any(char.IsLower) ||
            !value.Any(char.IsDigit) || !value.Any(ch => !char.IsLetterOrDigit(ch)))
            return (false, "Password must be at least 8 characters and include uppercase, lowercase, digit, and special character.");

        return (true, string.Empty);
    }

    public static (bool isValid, string errorMessage) ValidatePhone(string? value)
    {
        var digits = string.IsNullOrWhiteSpace(value) ? string.Empty : Regex.Replace(value, @"\D", string.Empty);
        if (digits.Length != 10)
            return (false, "Enter a phone number containing exactly 10 digits (formatting characters are allowed).");

        return (true, string.Empty);
    }

    public static (bool isValid, string errorMessage) ValidateAssetTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length is < 3 or > 50 ||
            !Regex.IsMatch(value.Trim(), @"^[A-Za-z0-9][A-Za-z0-9._/-]*$", RegexOptions.CultureInvariant))
            return (false, "Asset tag must be 3-50 characters and contain only letters, digits, '.', '_', '/', or '-'.");

        return (true, string.Empty);
    }
}
