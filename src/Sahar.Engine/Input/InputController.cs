using System.Numerics;
using Sahar.Core.Motion;
using Sahar.Engine.Host;
using Sahar.Engine.Motion;

namespace Sahar.Engine.Input;

/// <summary>
/// Mouse on Sahar (through the input window): click pokes her, press-and-move drags her by the leaf,
/// a fast release throws her, and resting the cursor on her for 400 ms makes her lean toward it.
/// A right-click or a double-click opens her menu (spec §7).
/// </summary>
internal sealed unsafe class InputController
{
    public const int DragThresholdPx = 4;
    public const uint HoverMs = 400;

    private readonly InputWindow _window;
    private readonly MotionController _motion;
    private readonly Action _poke;
    private readonly Action _menu;
    private bool _down, _dragging, _tracking;
    private Vector2 _downAt, _lastClickAt;
    private long _lastClickTick = long.MinValue / 2;

    public InputController(InputWindow window, MotionController motion, Action poke, Action menu)
    {
        _window = window;
        _motion = motion;
        _poke = poke;
        _menu = menu;
        window.Handler = OnMessage;
    }

    private nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_LBUTTONDOWN:
                Native.SetCapture(hwnd);
                _down = true;
                _dragging = false;
                _downAt = Cursor();
                return 0;

            case Native.WM_MOUSEMOVE:
                if (_down && !_dragging && Vector2.Distance(Cursor(), _downAt) > DragThresholdPx)
                {
                    _dragging = true;
                    _motion.BeginDrag();
                }
                else if (!_down && !_tracking)
                {
                    var tme = new TRACKMOUSEEVENT
                    {
                        CbSize = (uint)sizeof(TRACKMOUSEEVENT), Flags = Native.TME_HOVER | Native.TME_LEAVE,
                        HwndTrack = hwnd, HoverTime = HoverMs,
                    };
                    _tracking = Native.TrackMouseEvent(&tme) != 0;
                }
                return 0;

            case Native.WM_LBUTTONUP:
                bool wasDragging = _dragging;
                _down = _dragging = false;
                Native.ReleaseCapture();
                if (wasDragging) { _motion.EndDrag(); return 0; }
                var at = Cursor();
                long tick = Environment.TickCount64;
                if (tick - _lastClickTick <= Native.GetDoubleClickTime() && Vector2.Distance(at, _lastClickAt) <= DragThresholdPx)
                {
                    _lastClickTick = long.MinValue / 2;   // a third click starts over
                    _menu();
                    return 0;
                }
                _lastClickTick = tick;
                _lastClickAt = at;
                _poke();
                return 0;

            case Native.WM_RBUTTONUP:
                _menu();
                return 0;

            case Native.WM_CAPTURECHANGED:
                if (_dragging)
                {
                    _down = _dragging = false;
                    _motion.EndDrag();
                }
                return 0;

            case Native.WM_MOUSEHOVER:
                _tracking = false;
                _motion.Lean(true);
                return 0;

            case Native.WM_MOUSELEAVE:
                _tracking = false;
                _motion.Lean(false);
                return 0;
        }
        return null;
    }

    private static Vector2 Cursor()
    {
        var p = Native.CursorPos();
        return new Vector2(p.X, p.Y);
    }
}
