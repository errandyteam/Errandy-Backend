using System.Text.RegularExpressions;

namespace Errandy.Application.Common.Utils;

/// <summary>
/// Hides Nigerian phone numbers in chat messages so customers and runners
/// can't move a deal off the platform (PRD: in-app chat required, phone
/// numbers masked).
/// </summary>
public static class PhoneNumberMasker
{
    // Must stay SHORTER than the shortest number it replaces (11 characters).
    // That guarantees masking can never push a message over the 2000-character limit.
    public const string Replacement = "[hidden]";

    // Local format: 0 + [7/8/9] + [0/1] + 8 more digits (070x, 080x, 081x, 090x, 091x).
    // International format: +234 or 234, an optional extra 0, then the same digits.
    // Spaces, dashes, dots and brackets between the digits are allowed.
    // The last digit takes no trailing separator, so a full stop or space
    // right after a number is left in the message.
    private static readonly Regex NigerianPhone = new(
        @"(?<!\d)(?:\+?234[\s\-.()]*0?|0)[\s\-.()]*[789][\s\-.()]*[01][\s\-.()]*(?:\d[\s\-.()]*){7}\d(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>Returns the text with phone numbers replaced, and whether anything was hidden.</summary>
    public static string Mask(string content, out bool wasMasked)
    {
        wasMasked = NigerianPhone.IsMatch(content);
        return wasMasked ? NigerianPhone.Replace(content, Replacement) : content;
    }
}