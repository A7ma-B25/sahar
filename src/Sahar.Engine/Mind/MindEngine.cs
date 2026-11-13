using Sahar.Core.Chat;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Settings;
using Sahar.Engine.Activities;
using Sahar.Engine.Games;
using Sahar.Engine.Host;
using Sahar.Engine.Menu;
using Sahar.Engine.Motion;
using Sahar.Engine.Pipe;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;
using Sahar.Engine.Tools;
using Windows.System;

namespace Sahar.Engine.Mind;

/// <summary>
/// SEAM (0.14, three streams): the engine side of her brain. App.cs builds this once with the parts she already has
/// and calls <see cref="Attach"/>; each stream fills its own partial file and touches nothing else in App.cs:
/// <c>MindEngine.Runtime.cs</c> (A: start Sahar.Mind.exe, the private pipe, warm/unload, download and model pipe
/// commands, the progress bubble), <c>MindEngine.Chat.cs</c> (C: chat routing, the tools mapped to her abilities,
/// the Ask drill, the action log, initiative), <c>MindEngine.Memory.cs</c> (B: music learning, forget/erase hooks).
/// Runs on the UI thread.
/// </summary>
internal sealed partial class MindEngine
{
    public required DispatcherQueue Ui { get; init; }
    public required PipeServer Pipe { get; init; }
    public required bool TestMode { get; init; }
    public required Func<SaharSettings> Settings { get; init; }
    public required Action<Func<SaharSettings, SaharSettings>> Save { get; init; }
    public required MenuCard Menu { get; init; }
    public required SpeechBubble Speech { get; init; }
    public required TextBubble Bubble { get; init; }
    public required ListBubble ListBubble { get; init; }
    public required TimerTool Timer { get; init; }
    public required DndTool Dnd { get; init; }
    public required NoteTool Notes { get; init; }
    public required Director Director { get; init; }
    public required GamePlayer Games { get; init; }
    public required MotionController Motion { get; init; }
    public required SoulLayer Soul { get; init; }
    public required PrayerTool Prayer { get; init; }
    public required MusicTool Music { get; init; }
    public required QuranTool Quran { get; init; }
    public required Media Media { get; init; }
    public required Idle Idle { get; init; }
    public required CameraMic CameraMic { get; init; }
    public required ActivityHub Activities { get; init; }
    public required Action<string, bool> Express { get; init; }
    /// <summary>Time since his last key (typing hard closes the initiative gate).</summary>
    public Func<TimeSpan> SinceKeyboard { get; init; } = () => TimeSpan.MaxValue;

    /// <summary>Set by stream A: sends a line to the Mind (starting it if needed); false when there is no Mind.</summary>
    public Func<MindMsg, bool> Send { get; set; } = _ => false;
    /// <summary>Set by stream A: a model is installed, verified and the Mind is enabled.</summary>
    public Func<bool> Ready { get; set; } = () => false;
    /// <summary>Every line from the Mind (stream A raises it on the UI thread; C and B listen).</summary>
    public event Action<MindMsg>? Received;
    internal void Raise(MindMsg msg) => Received?.Invoke(msg);

    /// <summary>The settings app changed a Mind setting (group "mind"): every stream may listen.</summary>
    public event Action<SaharSettings>? SettingsChanged;
    internal void RaiseSettings(SaharSettings next) => SettingsChanged?.Invoke(next);

    /// <summary>Set by stream C: takes a chat line the fast path did not handle; false = today's reply.</summary>
    public Func<ChatCommand, string, bool>? ChatHandler { get; set; }

    /// <summary>Called by App.cs's chat row after the fast-path parse: true when the Mind took the line.</summary>
    public bool TakeChat(ChatCommand c, string text) => ChatHandler?.Invoke(c, text) ?? false;

    public void Attach()
    {
        AttachRuntime();
        AttachMemory();
        AttachChat();
    }

    partial void AttachRuntime();
    partial void AttachMemory();
    partial void AttachChat();
}
