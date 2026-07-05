using System.Windows.Input;

namespace PaneWorks.App;

public static partial class ShortcutGestureHelper
{
    private static string ToDisplayString(ShortcutGesture gesture)
    {
        var parts = new List<string>();
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        if (gesture.Key is not null)
        {
            parts.Add(GetKeyLabel(gesture.Key.Value));
        }

        return string.Join(" + ", parts);
    }

    private static string GetKeyLabel(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return key.ToString().ToUpperInvariant();
        }

        return key switch
        {
            Key.Escape => "Esc",
            Key.Enter => "Enter",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Delete => "Delete",
            Key.Back => "Backspace",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            _ => key.ToString()
        };
    }
}
