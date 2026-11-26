using System.Globalization;
using Sahar.Core.Text;

namespace Sahar.Core.Update;

/// <summary>Why a check or a download did not work, as she tells you (the log keeps the exact kind).</summary>
public enum UpdateFailure { Network, Timeout, Server, Signature, Checksum, DiskFull, Other }

/// <summary>
/// 0.15.0 T6: what kind a failure is, and whether she tries again by herself. The owner's log said
/// <c>update: check failed (timeout)</c> (a VPN on or off decides whether GitHub answers): network kinds are tried
/// twice more, a little later each time; anything else is reported at once (a bad signature never gets better).
/// </summary>
public static class UpdateFailures
{
    /// <summary>ERROR_DISK_FULL and ERROR_HANDLE_DISK_FULL as an IOException's HResult.</summary>
    public const int DiskFull = unchecked((int)0x80070070), HandleDiskFull = unchecked((int)0x80070027);

    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];

    /// <param name="kind">The engine's log kind: "timeout", an exception's type name, or a refusal's reason.</param>
    /// <param name="hresult">The exception's HResult (disk full is an IOException like any other).</param>
    public static UpdateFailure Classify(string kind, int hresult = 0)
    {
        if (hresult is DiskFull or HandleDiskFull) return UpdateFailure.DiskFull;
        return kind switch
        {
            "timeout" => UpdateFailure.Timeout,
            "HttpRequestException" or "SocketException" or "HttpIOException" or "AuthenticationException" => UpdateFailure.Network,
            "checksum mismatch" => UpdateFailure.Checksum,
            _ when kind.StartsWith("manifest refused", StringComparison.Ordinal) => UpdateFailure.Signature,
            _ when kind.StartsWith("HTTP ", StringComparison.Ordinal) => UpdateFailure.Server,
            "too many redirects" or "redirect not allowed" or "address not allowed" or "answer too big" or "setup too big" => UpdateFailure.Server,
            _ => UpdateFailure.Other,
        };
    }

    public static bool Retryable(UpdateFailure f) => f is UpdateFailure.Network or UpdateFailure.Timeout;

    /// <summary>After the <paramref name="failures"/>-th failure in a row: how long until she tries again, or null.</summary>
    public static TimeSpan? RetryAfter(UpdateFailure f, int failures) =>
        Retryable(f) && failures >= 1 && failures <= Backoff.Length ? Backoff[failures - 1] : null;
}

/// <summary>
/// 0.15.0 T6: her words for every step of an update, short Syrian Arabic, no emoji, written to a man (Arabic's
/// default; <see cref="Settings.Address"/> turns them to a woman). The tray's tooltip carries the same line, so a
/// click on «دوّر على تحديث» always shows something, even while she is hidden.
/// </summary>
public static class UpdateText
{
    public const string TrayName = "سَحَر · Sahar";
    public const string Checking = "لحظة، عم دوّر على تحديث…";
    public const string StillChecking = "لسا عم دوّر، لحظة…";
    public const string Retrying = "النت تقيل شوي، عم حاول كمان مرة…";
    public const string UpToDate = "أنا آخر نسخة";
    public const string Yes = "إي، ثبّتها هلق";
    public const string Later = "بعدين";
    /// <summary>Downloaded and checked: the silent setup closes her and starts her again.</summary>
    public const string Ready = "نزّلتها وتأكّدت منها. عم ثبّت، ورح ارجع بعد شوي";
    /// <summary>A portable her: the wizard, and where she lives afterwards.</summary>
    public const string ReadyPortable = "رح يطلعلك المثبّت. بعدو بسكن بقائمة ابدأ: «سحر»";
    public const string NotStarted = "ما قدرت شغّل المثبّت هلق، بجرّب بكرا";
    public const string VpnHint = "جرّب تطفي الـVPN أو تشغّله";

    public static string Available(SemVersion v) => $"في نسخة جديدة مني {v}، بدك ثبّتها؟";

    public static string ListTitle(SemVersion v) => $"سَحَر {v}";

    public static string Downloading(int? percent) =>
        percent is { } p ? $"عم نزّل النسخة الجديدة… {Digits.ToArabicIndic(p.ToString(CultureInfo.InvariantCulture))}٪" : "عم نزّل النسخة الجديدة…";

    public static string SetupFailed(int code) => $"ما زبط التحديث (رمز {code.ToString(CultureInfo.InvariantCulture)})، ضليت متل ما كنت";

    /// <summary>The plain reason for a failed check (<paramref name="download"/> false) or download.</summary>
    public static string Failed(UpdateFailure f, bool download) => f switch
    {
        UpdateFailure.Network or UpdateFailure.Timeout => (download ? "انقطع تنزيل التحديث. " : "ما قدرت وصل لصفحة التحديث. ") + VpnHint,
        UpdateFailure.Server => "صفحة التحديث ما عم تجاوب هلق، بجرّب بعدين",
        UpdateFailure.Signature => "التحديث اللي وصلني مو موقّع منّا، فما رح ثبّتو",
        UpdateFailure.Checksum => "ملف التحديث وصل خربان، فمسحتو وما ثبّتّو",
        UpdateFailure.DiskFull => "ما في مكان كفاية عالجهاز لنزّل التحديث",
        _ => download ? "ما قدرت نزّل التحديث هلق، بجرّب بعدين" : "ما قدرت دوّر على تحديث هلق، بجرّب بعدين",
    };

    /// <summary>The tray tooltip: her name, then the update's last line; Windows keeps 127 characters.</summary>
    public static string Tip(string? line)
    {
        if (string.IsNullOrEmpty(line)) return TrayName;
        var tip = TrayName + "\n" + line;
        return tip.Length <= 127 ? tip : tip[..126] + "…";
    }

    /// <summary>How much of the setup is here (0–100), or null when the server gave no length.</summary>
    public static int? Percent(long done, long? total) =>
        total is > 0 ? (int)Math.Clamp(done * 100 / total.Value, 0, 100) : null;

    /// <summary>The quarter (25, 50, 75, 100) the download just reached, once each: her bubble and the log say it.</summary>
    public static int? Milestone(int? before, int? now)
    {
        if (now is not { } n) return null;
        int was = (before ?? 0) / 25, reached = n / 25;
        return reached > was ? reached * 25 : null;
    }
}
