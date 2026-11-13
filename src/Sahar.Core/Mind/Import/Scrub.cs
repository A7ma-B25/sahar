using System.Text.RegularExpressions;

namespace Sahar.Core.Mind.Import;

/// <summary>
/// Takes out what identifies anyone before a line is kept (spec §7.5): phone numbers, emails, links, IBAN and card-like
/// numbers, and @handles. Each becomes a plain marker, so his style stays and the data goes. Times, small numbers,
/// years and prices stay. Pure; never logs.
/// </summary>
public static partial class Scrub
{
    public const string Phone = "[رقم]", Email = "[إيميل]", Link = "[رابط]", Account = "[حساب]";

    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = EmailRx().Replace(text, Email);
        s = UrlRx().Replace(s, Link);
        s = DomainRx().Replace(s, Link);
        s = IbanRx().Replace(s, Account);
        s = HandleRx().Replace(s, Account);
        s = LongNumberRx().Replace(s, m => DigitCount(m.Value) >= 7 ? Phone : m.Value);
        return s;
    }

    private static int DigitCount(string s) => s.Count(char.IsDigit);

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}-]+(?:\.[\p{L}\p{N}-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRx();

    [GeneratedRegex(@"(?i)\b(?:https?://|www\.)[^\s<>«»""]+", RegexOptions.CultureInvariant)]
    private static partial Regex UrlRx();

    // A bare domain with a path or a well-known ending: youtube.com/watch, t.me/x, site.org.
    [GeneratedRegex(@"(?i)\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|net|org|io|me|sy|sa|ae|co|app|dev|ly|gl|be|tv|info|xyz)\b(?:/[^\s<>«»""]*)?", RegexOptions.CultureInvariant)]
    private static partial Regex DomainRx();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){2,7}(?:[ ]?[A-Z0-9]{1,4})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex IbanRx();

    [GeneratedRegex(@"(?<![\w@])@[A-Za-z0-9_.]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex HandleRx();

    // Digits (any script) with spaces, dashes, dots or brackets between them, an optional + or 00 in front; a phone
    // or a card when seven digits or more.
    [GeneratedRegex(@"(?:\+|\b00)?\(?\d[\d \-.() ]{5,}\d", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumberRx();
}
