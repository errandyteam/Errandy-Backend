using System.Text.RegularExpressions;

namespace Errandy.Application.Common.Utils;

/// <summary>
/// Hides phone numbers and bank account numbers in chat messages so customers and runners
/// can't move a deal or a payment off the platform (PRD: in-app chat required, contact
/// details masked).
/// </summary>
public static class PhoneNumberMasker
{
    // Must stay SHORTER than the shortest thing it replaces (10 characters).
    // That guarantees masking can never push a message over the 2000-character limit.
    public const string Replacement = "[hidden]";

    // Nigerian mobile numbers: 0 + [7/8/9] + [0/1] + 8 more digits, or +234 / 234 followed by the
    // same digits (with an optional extra 0). Spaces, dashes, dots and brackets between digits are allowed.
    // The last digit takes no trailing separator, so a full stop right after a number stays.
    private static readonly Regex NigerianPhone = new(
        @"(?<!\d)(?:\+?234[\s\-.()]*0?|0)[\s\-.()]*[789][\s\-.()]*[01][\s\-.()]*(?:\d[\s\-.()]*){7}\d(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    // Bank account numbers (NUBAN is 10 digits) and wallet apps like Opay and PalmPay, whose account
    // number is the phone number WITHOUT the leading 0 (also 10 digits). Any run of 10 to 16 digits,
    // optionally split by spaces or dashes, is hidden. This also covers BVN/NIN (11) and card numbers (16).
    // Known trade-off: a date and time written as digits, like "2026-11-20 18", can be hidden by mistake.
    private static readonly Regex DigitRun = new(
        @"(?<!\d)(?:\d[\s\-]*){9,15}\d(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>Returns the text with phone and account numbers replaced, and whether anything was hidden.</summary>
    public static string Mask(string content, out bool wasMasked)
    {
        var result = DigitRun.Replace(NigerianPhone.Replace(content, Replacement), Replacement);
        wasMasked = !string.Equals(result, content, StringComparison.Ordinal);
        return result;
    }
}