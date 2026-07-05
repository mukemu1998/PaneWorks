using System.Windows.Input;

namespace PaneWorks.App;

public static partial class ShortcutGestureHelper
{
    private static bool TryParseShortcut(string? input, bool allowModifierOnly, out ShortcutGesture gesture)
    {
        gesture = new ShortcutGesture(ModifierKeys.None, null);

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var tokens = input
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key? key = null;

        foreach (var rawToken in tokens)
        {
            var token = rawToken.Trim();
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    continue;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    continue;
                case "alt":
                case "menu":
                    modifiers |= ModifierKeys.Alt;
                    continue;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    continue;
            }

            if (key is not null)
            {
                return false;
            }

            if (!TryParseKeyToken(token, out var parsedKey))
            {
                return false;
            }

            key = parsedKey;
        }

        if (key is null && (!allowModifierOnly || modifiers == ModifierKeys.None))
        {
            return false;
        }

        gesture = new ShortcutGesture(modifiers, key);
        return true;
    }

    private static bool TryParseKeyToken(string token, out Key key)
    {
        key = Key.None;
        var normalized = token.Trim();
        if (normalized.Length == 1)
        {
            var ch = char.ToUpperInvariant(normalized[0]);
            if (ch >= 'A' && ch <= 'Z')
            {
                key = Key.A + (ch - 'A');
                return true;
            }

            if (ch >= '0' && ch <= '9')
            {
                key = Key.D0 + (ch - '0');
                return true;
            }
        }

        return normalized.ToLowerInvariant() switch
        {
            "esc" or "escape" => AssignKey(Key.Escape, out key),
            "enter" or "return" => AssignKey(Key.Enter, out key),
            "space" => AssignKey(Key.Space, out key),
            "tab" => AssignKey(Key.Tab, out key),
            "delete" or "del" => AssignKey(Key.Delete, out key),
            "backspace" => AssignKey(Key.Back, out key),
            "insert" or "ins" => AssignKey(Key.Insert, out key),
            "home" => AssignKey(Key.Home, out key),
            "end" => AssignKey(Key.End, out key),
            "pageup" or "pgup" => AssignKey(Key.PageUp, out key),
            "pagedown" or "pgdn" => AssignKey(Key.PageDown, out key),
            "up" => AssignKey(Key.Up, out key),
            "down" => AssignKey(Key.Down, out key),
            "left" => AssignKey(Key.Left, out key),
            "right" => AssignKey(Key.Right, out key),
            _ => TryParseFunctionKey(normalized, out key) || Enum.TryParse(normalized, true, out key)
        };
    }

    private static bool TryParseFunctionKey(string token, out Key key)
    {
        key = Key.None;
        if (!token.StartsWith('f') || token.Length < 2 || !int.TryParse(token[1..], out var index) || index is < 1 or > 24)
        {
            return false;
        }

        key = Key.F1 + (index - 1);
        return true;
    }

    private static bool AssignKey(Key value, out Key key)
    {
        key = value;
        return true;
    }
}
