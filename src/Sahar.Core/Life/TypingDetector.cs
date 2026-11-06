namespace Sahar.Core.Life;

/// <summary>
/// Are you typing? From the time of the last real key press (the engine gets it from Raw Input, timestamps
/// only, never which key). It counts as typing until three seconds after the last key. Mouse clicks and
/// stops never count, so poking, hovering over and dropping her are never mistaken for typing.
/// </summary>
public sealed class TypingDetector
{
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);

    public bool Update(DateTimeOffset now, TimeSpan sinceKeyboard) => sinceKeyboard <= Hold;
}
