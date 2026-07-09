using System;
using System.Text;

namespace Apeiron.Services;

public static class OfflineUsernameHelper
{
    public const string Default = "Player";
    public const int MinLength = 3;
    public const int MaxLength = 16;

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        if (name.Length < MinLength || name.Length > MaxLength)
            return false;

        foreach (var c in name)
        {
            var isAsciiLetter = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            var isDigit = c is >= '0' and <= '9';
            if (!isAsciiLetter && !isDigit && c != '_')
                return false;
        }

        return true;
    }

    public static string NormalizeInput(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var builder = new StringBuilder(MaxLength);
        foreach (var c in name.Trim())
        {
            var isAsciiLetter = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            var isDigit = c is >= '0' and <= '9';
            if (isAsciiLetter || isDigit || c == '_')
                builder.Append(c);

            if (builder.Length >= MaxLength)
                break;
        }

        return builder.ToString();
    }

    public static string Sanitize(string? name)
    {
        var normalized = NormalizeInput(name);
        return IsValid(normalized) ? normalized : Default;
    }

    public static OfflineUsernameValidation Validate(string? name)
    {
        var normalized = NormalizeInput(name);
        if (string.IsNullOrEmpty(normalized))
            return OfflineUsernameValidation.Empty;

        if (normalized.Length < MinLength)
            return OfflineUsernameValidation.TooShort;

        if (normalized.Length > MaxLength)
            return OfflineUsernameValidation.TooLong;

        return IsValid(normalized) ? OfflineUsernameValidation.Valid : OfflineUsernameValidation.InvalidCharacters;
    }
}

public enum OfflineUsernameValidation
{
    Valid,
    Empty,
    TooShort,
    TooLong,
    InvalidCharacters
}
