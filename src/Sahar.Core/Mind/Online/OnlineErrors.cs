namespace Sahar.Core.Mind.Online;

/// <summary>
/// The online brain's own error codes (spec 0.17 §7), from the HTTP status or the transport, never from a provider's
/// body (bodies never reach a log). Each has a plain Syrian line for «جرّب المفتاح».
/// </summary>
public static class OnlineErrors
{
    public const string Ok = "ok", KeyBad = "key-bad", Blocked = "blocked", NoCredit = "no-credit", Quota = "quota", Server = "server",
        Timeout = "timeout", Offline = "offline", StreamError = "stream-error", BadOutput = "bad-output", LocalOnly = "local-only", NoKey = "no-key", ModelBad = "model-bad", NoConsent = "no-consent";

    /// <summary>The status code → our code (null for a 2xx).</summary>
    public static string? FromStatus(int status) => status switch
    {
        >= 200 and < 300 => null,
        401 => KeyBad,
        402 => NoCredit,
        403 => Blocked,
        404 => ModelBad,   // review: a model id the provider does not have
        408 => Timeout,
        429 => Quota,
        >= 500 => Server,
        _ => BadOutput,
    };

    /// <summary>Worth one more try after a short wait (spec §7: 429 and 5xx only).</summary>
    public static bool Retryable(string code) => code is Quota or Server;

    /// <summary>The key test's line, as he reads it.</summary>
    public static string Line(string code) => code switch
    {
        Ok => "يشتغل ✓",
        KeyBad => "المفتاح غلط",
        Quota => "خلصت طلبات اليوم",
        NoCredit => "الرصيد خلص عندهن",
        Server => "المزوّد واقع هلق، جرّب بعدين",
        LocalOnly => "«محلي فقط» شغّال: ما بطلع شي برّا",
        NoKey => "ما في مفتاح محفوظ",
        ModelBad => "اسم الموديل غلط",
        NoConsent => "وافق أول عالملاحظة فوق",
        _ => "محجوب أو ما في نت",
    };
}
