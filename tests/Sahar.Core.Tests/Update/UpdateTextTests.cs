using Sahar.Core.Settings;
using Sahar.Core.Update;

namespace Sahar.Core.Tests.Update;

/// <summary>0.15.0 T6: the owner's log said <c>update: check failed (timeout)</c> and «تحديث» showed nothing. Every
/// step now has a short line (no emoji) and every failure kind its own plain reason; network kinds are retried.</summary>
public sealed class UpdateTextTests
{
    private static readonly SemVersion V = new(0, 16, 0, null);

    private static bool HasEmoji(string s)
    {
        foreach (var rune in s.EnumerateRunes())
            if (rune.Value >= 0x1F000 || rune.Value is >= 0x2600 and <= 0x27BF || rune.Value is 0x2728 or 0x23F3 or 0xFE0F) return true;
        return false;
    }

    public static TheoryData<string> EveryLine()
    {
        var d = new TheoryData<string>
        {
            UpdateText.Checking, UpdateText.StillChecking, UpdateText.Retrying, UpdateText.UpToDate,
            UpdateText.Available(V), UpdateText.ListTitle(V), UpdateText.Yes, UpdateText.Later,
            UpdateText.Downloading(null), UpdateText.Downloading(42), UpdateText.Ready, UpdateText.ReadyPortable,
            UpdateText.NotStarted, UpdateText.SetupFailed(2), UpdateRules.MovedLine,
        };
        foreach (var f in Enum.GetValues<UpdateFailure>()) { d.Add(UpdateText.Failed(f, download: false)); d.Add(UpdateText.Failed(f, download: true)); }
        return d;
    }

    [Theory]
    [MemberData(nameof(EveryLine))]
    public void EveryLine_IsShort(string line)   // 0.15.0 owner correction: emoji are welcome in her speech
    {
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.True(line.Length <= 100, $"{line.Length}: {line}");
    }

    [Fact]
    public void TheMovedLine_KeepsItsHome() => Assert.True(HasEmoji(UpdateRules.MovedLine) && UpdateRules.MovedLine.Contains("🏠", StringComparison.Ordinal), UpdateRules.MovedLine);

    [Fact]
    public void Available_NamesTheVersion_AndKeepsTheWordsTheStageLooksFor()
    {
        Assert.Contains("0.16.0", UpdateText.Available(V));
        Assert.Contains("نسخة جديدة", UpdateText.Available(V));
        Assert.Contains("0.16.0", UpdateText.ListTitle(V));
        Assert.Contains("قائمة ابدأ", UpdateText.ReadyPortable);
        Assert.Contains("ما زبط التحديث", UpdateText.SetupFailed(2));
        Assert.Contains("2", UpdateText.SetupFailed(2));
    }

    [Fact]
    public void Downloading_ShowsThePercent_InHerDigits()
    {
        Assert.Contains("٤٢٪", UpdateText.Downloading(42));
        Assert.DoesNotContain("٪", UpdateText.Downloading(null));
    }

    [Theory]
    [InlineData(UpdateFailure.Network)]
    [InlineData(UpdateFailure.Timeout)]
    public void NetworkKinds_HintTheVpn(UpdateFailure f)
    {
        Assert.Contains(UpdateText.VpnHint, UpdateText.Failed(f, download: false));
        Assert.Contains(UpdateText.VpnHint, UpdateText.Failed(f, download: true));
        Assert.Equal("جرّب تطفي الـVPN أو تشغّله", UpdateText.VpnHint);
    }

    [Fact]
    public void TheVpnHint_ToAWoman()
    {
        var text = Address.For(Gender.Female, UpdateText.Failed(UpdateFailure.Timeout, download: false));
        Assert.Contains("جرّبي تطفي الـVPN أو تشغّليه", text);
    }

    [Theory]
    [InlineData(UpdateFailure.Server)]
    [InlineData(UpdateFailure.Signature)]
    [InlineData(UpdateFailure.Checksum)]
    [InlineData(UpdateFailure.DiskFull)]
    [InlineData(UpdateFailure.Other)]
    public void OtherKinds_DoNotBlameTheVpn(UpdateFailure f)
    {
        Assert.DoesNotContain("VPN", UpdateText.Failed(f, download: false));
        Assert.DoesNotContain("VPN", UpdateText.Failed(f, download: true));
    }

    [Fact]
    public void EachKind_HasItsOwnReason()
    {
        var check = Enum.GetValues<UpdateFailure>().Where(f => f != UpdateFailure.Timeout).Select(f => UpdateText.Failed(f, false)).ToList();
        Assert.Equal(check.Count, check.Distinct().Count());
        Assert.Contains("مكان", UpdateText.Failed(UpdateFailure.DiskFull, true));
        Assert.Contains("موقّع", UpdateText.Failed(UpdateFailure.Signature, false));
        Assert.Contains("خربان", UpdateText.Failed(UpdateFailure.Checksum, true));
    }

    [Fact]
    public void HerOwnWords_StayHers_ToAWoman()
    {
        // "عم حاول" / "بجرّب" are about herself: the feminine table must leave them alone.
        Assert.Equal(UpdateText.Retrying, Address.For(Gender.Female, UpdateText.Retrying));
        Assert.Equal(UpdateText.Failed(UpdateFailure.Server, false), Address.For(Gender.Female, UpdateText.Failed(UpdateFailure.Server, false)));
    }

    [Fact]
    public void Tip_IsTheTrayName_ThenTheLine_WithinWindowsLimit()
    {
        Assert.Equal(UpdateText.TrayName, UpdateText.Tip(null));
        Assert.Equal(UpdateText.TrayName + "\n" + UpdateText.UpToDate, UpdateText.Tip(UpdateText.UpToDate));
        var longTip = UpdateText.Tip(new string('ا', 300));
        Assert.True(longTip.Length <= 127, longTip.Length.ToString());
        Assert.EndsWith("…", longTip);
    }

    [Theory]
    [InlineData(0L, 100L, 0)]
    [InlineData(50L, 100L, 50)]
    [InlineData(999L, 1000L, 99)]
    [InlineData(1000L, 1000L, 100)]
    [InlineData(2000L, 1000L, 100)]
    public void Percent_FromBytes(long done, long total, int want) => Assert.Equal(want, UpdateText.Percent(done, total));

    [Fact]
    public void Percent_UnknownWithoutALength()
    {
        Assert.Null(UpdateText.Percent(10, null));
        Assert.Null(UpdateText.Percent(10, 0));
    }

    [Theory]
    [InlineData(null, 10, null)]
    [InlineData(null, 25, 25)]
    [InlineData(10, 30, 25)]
    [InlineData(25, 49, null)]
    [InlineData(49, 80, 75)]
    [InlineData(75, 100, 100)]
    [InlineData(100, 100, null)]
    [InlineData(10, null, null)]
    public void Milestone_EachQuarterOnce(int? before, int? now, int? want) => Assert.Equal(want, UpdateText.Milestone(before, now));
}

public sealed class UpdateFailuresTests
{
    [Theory]
    [InlineData("timeout", 0, UpdateFailure.Timeout)]
    [InlineData("HttpRequestException", 0, UpdateFailure.Network)]
    [InlineData("SocketException", 0, UpdateFailure.Network)]
    [InlineData("HttpIOException", 0, UpdateFailure.Network)]
    [InlineData("AuthenticationException", 0, UpdateFailure.Network)]
    [InlineData("HTTP 404", 0, UpdateFailure.Server)]
    [InlineData("HTTP 503", 0, UpdateFailure.Server)]
    [InlineData("too many redirects", 0, UpdateFailure.Server)]
    [InlineData("redirect not allowed", 0, UpdateFailure.Server)]
    [InlineData("answer too big", 0, UpdateFailure.Server)]
    [InlineData("setup too big", 0, UpdateFailure.Server)]
    [InlineData("manifest refused: signature or fields", 0, UpdateFailure.Signature)]
    [InlineData("checksum mismatch", 0, UpdateFailure.Checksum)]
    [InlineData("IOException", unchecked((int)0x80070070), UpdateFailure.DiskFull)]
    [InlineData("IOException", unchecked((int)0x80070027), UpdateFailure.DiskFull)]
    [InlineData("IOException", 0, UpdateFailure.Other)]
    [InlineData("UnauthorizedAccessException", 0, UpdateFailure.Other)]
    [InlineData("", 0, UpdateFailure.Other)]
    public void Classify(string kind, int hresult, UpdateFailure want) => Assert.Equal(want, UpdateFailures.Classify(kind, hresult));

    [Theory]
    [InlineData(UpdateFailure.Network, true)]
    [InlineData(UpdateFailure.Timeout, true)]
    [InlineData(UpdateFailure.Server, false)]
    [InlineData(UpdateFailure.Signature, false)]
    [InlineData(UpdateFailure.Checksum, false)]
    [InlineData(UpdateFailure.DiskFull, false)]
    [InlineData(UpdateFailure.Other, false)]
    public void OnlyNetworkKinds_AreRetried(UpdateFailure f, bool retried) => Assert.Equal(retried, UpdateFailures.RetryAfter(f, 1) is not null);

    [Fact]
    public void Retry_BacksOff_ThenGivesUp()
    {
        var first = UpdateFailures.RetryAfter(UpdateFailure.Timeout, 1);
        var second = UpdateFailures.RetryAfter(UpdateFailure.Timeout, 2);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(second > first);
        Assert.Null(UpdateFailures.RetryAfter(UpdateFailure.Timeout, 3));
        Assert.Null(UpdateFailures.RetryAfter(UpdateFailure.Network, 0));
    }

    [Fact]
    public void Retry_NeverMakesAManualCheckWaitLong()
    {
        var total = TimeSpan.Zero;
        for (int i = 1; UpdateFailures.RetryAfter(UpdateFailure.Network, i) is { } d; i++) total += d;
        Assert.True(total <= TimeSpan.FromSeconds(30), total.ToString());
    }
}
