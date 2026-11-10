namespace Sahar.Core.Senses;

/// <summary>Are you away? Only when every input source has been quiet for 5 minutes, or the session is locked. Away
/// never comes from her face (a song or a reminder showing hid "away" all night on the owner's machine), and typing
/// seen by the keyboard hook always counts as being here.</summary>
public static class Presence
{
    public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(5);

    public static bool Away(TimeSpan sinceSystemInput, TimeSpan sinceKeyboard, TimeSpan sincePointerMove, bool locked) =>
        locked || (sinceSystemInput >= AwayAfter && sinceKeyboard >= AwayAfter && sincePointerMove >= AwayAfter);
}
