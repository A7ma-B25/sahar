using Sahar.Core.Settings;
using Sahar.Core.Tools;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;
using Windows.System;
using A = Sahar.Core.Settings.Address;

namespace Sahar.Engine.Surfaces;

/// <summary>
/// The input cards and ask bubbles that replaced the white EDIT / LISTBOX popups (V15Inputs, V15Lists): the timer, the
/// city (weather and prayer), her first hello (name, then «شب ولا بنت؟»), and a yes/no ask. What he types is never logged
/// (lengths only).
/// </summary>
internal static class Cards
{
    private const string EscHint = "Esc لسكّر";

    private static PanelSpec TimerSpec(string? value = null, string? error = null) => new()
    {
        Kind = "input", Name = "timer", Title = A.ToYou("قديش بدك المؤقت؟"), FieldIcon = "clock", Placeholder = "دقيقة", Suffix = value is null ? null : "دقيقة",
        Value = value, Numeric = true, Max = 40, Helper = "من دقيقة لـ ١٠ ساعات", Error = error, Primary = new PickAction("go", A.ToYou("شغّل")), FootHint = EscHint,
    };

    /// <summary>«قديش بدك المؤقت؟»: minutes (Arabic or Latin digits, 1–600). Not understood: the error under the field,
    /// his text kept, no timer.</summary>
    public static void Timer(Panel panel, Action<int> start, Action notUnderstood)
    {
        panel.Show(TimerSpec(), e =>
        {
            if (e.Kind != "submit") return;
            var text = e.Value ?? "";
            if (TimerInput.ParseMinutes(text) is { } minutes)
            {
                panel.Done();
                start(minutes);
                return;
            }
            Log.Write($"timer: typed time not understood ({text.Length} characters)");   // never what was typed (spec §14)
            notUnderstood();
            panel.Update(TimerSpec(text, A.ToYou("ما فهمتها. اكتب الدقايق رقم، مثلاً ٩٠.")));
        });
    }

    private static PanelSpec CitySpec(string why, string? value, IReadOnlyList<PickRow>? suggestions, string? error) => new()
    {
        Kind = "input", Name = "city", Title = A.ToYou("شو مدينتك؟"), Sub = A.ToYou(why), FieldIcon = "pin", Placeholder = A.ToYou("اسم مدينتك"), Value = value,
        Live = error is null, Suggestions = suggestions ?? [], Error = error, Max = 80,
        Primary = new PickAction("go", error is null ? "تمام" : A.ToYou("جرّب كمان مرة")),
        Secondary = error is null ? null : new PickAction("later", "بعدين"), FootHint = EscHint,
    };

    /// <summary>«شو مدينتك؟» with suggestions while he types (300 ms after the last key); offline gives the reason and
    /// «جرّب كمان مرة». The city reaches only the geocoder, never the log.</summary>
    public static void City(Panel panel, DispatcherQueue ui, string why, Func<string, Task<(GeoResult? Place, string? Error)>> find, Action<GeoResult, string> done)
    {
        GeoResult? found = null;
        string? foundFor = null;
        int asked = 0;
        bool submitting = false;
        void Search(string text, bool submit)
        {
            if (submitting && !submit) return;   // his Enter wins over a late suggestion search
            submitting |= submit;
            int n = ++asked;
            find(text).ContinueWith(t =>
            {
                var (place, error) = t.IsCompletedSuccessfully ? t.Result : (null, "ما في نت هلق، فما قدرت دوّر. جرّب بعد شوي.");
                ui.TryEnqueue(() =>
                {
                    if (n != asked || panel.Name != "city") return;
                    if (place is not null)
                    {
                        found = place;
                        foundFor = text;
                        if (submit) { panel.Done(); done(place, text); return; }
                        panel.Update(CitySpec(why, text, [new PickRow("found", place.Name ?? text.Trim()) { Icon = "pin" }], null));
                        return;
                    }
                    if (submit) { submitting = false; panel.Update(CitySpec(why, text, null, A.ToYou(error ?? "ما لقيت هالمدينة. جرّب اسم تاني."))); }
                    else panel.Update(CitySpec(why, text, null, null));
                });
            });
        }
        panel.Show(CitySpec(why, null, null, null), e =>
        {
            switch (e.Kind)
            {
                case "input" when (e.Value ?? "").Trim().Length >= 2:
                    Search(e.Value!, submit: false);
                    break;
                case "pick" when found is not null:
                    panel.Done();
                    done(found, foundFor ?? "");
                    break;
                case "submit" when (e.Value ?? "").Trim().Length > 0:
                    if (found is not null && foundFor == e.Value) { panel.Done(); done(found, e.Value!); }
                    else Search(e.Value!, submit: true);
                    break;
                case "secondary":
                    panel.Done();
                    break;
            }
        });
    }

    /// <summary>Her first hello (V15Inputs «أول مرة»): the name card (optional), then «شب ولا بنت؟» as an ask bubble.
    /// Closing the name card ends it (asked again next start); a skipped name is fine.</summary>
    public static void Hello(Panel panel, Action<string?, Gender> done)
    {
        bool answered = false;
        panel.Show(new PanelSpec
        {
            Kind = "input", Name = "name", Title = "أهلين! أنا سَحَر.", Sub = "شو بدك ناديك؟", FieldIcon = "user", Placeholder = "اسمك أو شو بتحب",
            Helper = "بس أنا بشوفه، وبتغيّره من الإعدادات.", Max = 40, Primary = new PickAction("go", "تمام"), Secondary = new PickAction("skip", "تخطّى"),
        }, e =>
        {
            if (e.Kind is not ("submit" or "secondary") || answered) return;
            answered = true;
            var name = e.Kind == "submit" ? Address.CleanName(e.Value ?? "") : null;
            panel.Done();
            panel.Show(new PanelSpec
            {
                Kind = "ask", Name = "gender", Title = "إنت شب ولا بنت؟ لحتى احكي معك صح.",
                Buttons = [new PickAction("male", "شب"), new PickAction("female", "بنت"), new PickAction("unknown", "ما بدي قول")],
            }, g =>
            {
                if (g.Kind is not ("pick" or "closed")) return;
                if (g.Kind == "pick") panel.Done();
                done(name, g.Value switch { "male" => Gender.Male, "female" => Gender.Female, _ => Gender.Unknown });
            }, mode: "above");
        });
    }

    /// <summary>A yes/no question in a bubble above her (V15Lists «عقلها بيسأل»): 1 / Enter = yes, 2 / Esc / closing = no.</summary>
    /// <summary>The chat window is open: a yes/no goes into it inline (never a bubble over it); false when it is closed.</summary>
    public static Func<string, Action<bool>, bool>? InChat { get; set; }

    public static void Ask(Panel panel, string name, string question, string yes, string no, Action<bool> answer, string? yesIcon = null)
    {
        if (InChat?.Invoke(question, answer) == true) return;
        bool done = false;
        panel.Show(new PanelSpec
        {
            Kind = "ask", Name = name, Title = question, Buttons = [new PickAction("yes", yes, yesIcon), new PickAction("no", no)],
        }, e =>
        {
            if (done || e.Kind is not ("pick" or "closed" or "failed")) return;
            done = true;
            if (e.Kind == "pick") panel.Done();
            answer(e.Kind == "pick" && e.Value == "yes");
        }, mode: "above");
    }
}
