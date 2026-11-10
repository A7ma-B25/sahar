namespace Sahar.Core.Menu;

public enum MenuKey { Tab, ShiftTab, Left, Right, Up, Down, Enter, Escape, Digit1, Digit2, Digit3 }

/// <summary>What a key does: move the focus, activate an item, go back from a drill, or close the card.</summary>
public sealed record KeyResult(string? Focus, string? Activate, bool Back, bool Close);

/// <summary>The card's keyboard (spec §5): Tab / arrows move (mirrored in RTL), 1–3 pick the tiles, Enter acts, Esc goes back then closes.</summary>
public static class MenuKeys
{
    public static KeyResult Handle(CardLayout l, MenuView v, string? focused, MenuKey key, bool rtl, bool inChat)
    {
        var order = l.FocusOrder;
        KeyResult Move(string? to) => new(to ?? focused, null, false, false);
        switch (key)
        {
            case MenuKey.Escape:
                return v.Drill is not null ? new(null, null, true, false) : new(null, null, false, true);
            case MenuKey.Enter:
                return new(focused, focused, false, false);
            case MenuKey.Tab or MenuKey.ShiftTab:
            {
                if (order.Count == 0) return Move(null);
                int i = focused is null ? -1 : IndexOf(order, focused);
                int n = order.Count;
                int next = key == MenuKey.Tab ? (i < 0 ? 0 : (i + 1) % n) : (i < 0 ? n - 1 : (i - 1 + n) % n);
                return Move(order[next]);
            }
            case MenuKey.Digit1 or MenuKey.Digit2 or MenuKey.Digit3:
            {
                if (inChat || v.Drill is not null) return new(focused, null, false, false);
                var tiles = MenuCatalog.Tiles(false).Select(t => t.Id).Where(v.Visible.Contains).ToList();
                int k = key - MenuKey.Digit1;
                return new(focused, k < tiles.Count ? tiles[k] : null, false, false);
            }
            case MenuKey.Left or MenuKey.Right:
            {
                if (inChat || focused is null) return Move(null);
                var row = l.Rows.FirstOrDefault(r => r.Contains(focused));
                if (row is null) return Move(null);
                int i = IndexOf(row, focused);
                bool forward = (key == MenuKey.Left) == rtl;   // toward the logical end
                int j = Math.Clamp(i + (forward ? 1 : -1), 0, row.Count - 1);
                return Move(row[j]);
            }
            default:   // Up, Down
            {
                if (focused is null) return Move(order.Count > 0 ? order[0] : null);
                int r = -1;
                for (int k = 0; k < l.Rows.Count; k++) if (l.Rows[k].Contains(focused)) r = k;
                if (r < 0) return Move(null);
                int to = r + (key == MenuKey.Down ? 1 : -1);
                if (to < 0 || to >= l.Rows.Count) return Move(null);
                var from = l.Rects[focused];
                float cx = from.X + from.Width / 2;
                var best = l.Rows[to].MinBy(id => MathF.Abs(l.Rects[id].X + l.Rects[id].Width / 2 - cx));
                return Move(best);
            }
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string id)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == id) return i;
        return -1;
    }
}
