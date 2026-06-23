using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace PaneWorks.App;

public static class ShortcutGestureHelper
{
    public static string NormalizeShortcut(string? input, string fallback, bool allowModifierOnly = true)
    {
        return TryParseShortcut(input, allowModifierOnly, out var gesture)
            ? ToDisplayString(gesture)
            : fallback;
    }

    public static bool MatchesKeyEvent(WpfKeyEventArgs e, string shortcut, bool allowModifierOnly = true)
    {
        if (!TryParseShortcut(shortcut, allowModifierOnly, out var gesture))
        {
            return false;
        }

        var currentModifiers = Keyboard.Modifiers;
        if (currentModifiers != gesture.Modifiers)
        {
            return false;
        }

        var key = ResolveEventKey(e);
        if (gesture.Key is null)
        {
            return IsModifierKey(key);
        }

        return NormalizeMainKey(key) == gesture.Key.Value;
    }

    public static bool IsPressed(string shortcut, bool allowModifierOnly = true)
    {
        if (!TryParseShortcut(shortcut, allowModifierOnly, out var gesture))
        {
            return false;
        }

        var activeModifiers = GetPressedModifiers();
        if (activeModifiers != gesture.Modifiers)
        {
            return false;
        }

        if (gesture.Key is null)
        {
            return gesture.Modifiers != ModifierKeys.None;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(gesture.Key.Value);
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    public static string CaptureFromKeyEvent(WpfKeyEventArgs e, bool allowModifierOnly = true)
    {
        var key = ResolveEventKey(e);
        var modifiers = Keyboard.Modifiers;

        if (IsModifierKey(key))
        {
            modifiers = modifiers == ModifierKeys.None ? GetModifierFromKey(key) : modifiers;
            if (!allowModifierOnly || modifiers == ModifierKeys.None)
            {
                return string.Empty;
            }

            return ToDisplayString(new ShortcutGesture(modifiers, null));
        }

        var normalizedKey = NormalizeMainKey(key);
        return ToDisplayString(new ShortcutGesture(modifiers, normalizedKey));
    }

    public static string ToDisplayString(string shortcut, string fallback)
    {
        return TryParseShortcut(shortcut, allowModifierOnly: true, out var gesture)
            ? ToDisplayString(gesture)
            : fallback;
    }

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

    private static Key ResolveEventKey(WpfKeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return NormalizeMainKey(key);
    }

    private static Key NormalizeMainKey(Key key)
    {
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl => Key.LeftCtrl,
            Key.LeftShift or Key.RightShift => Key.LeftShift,
            Key.LeftAlt or Key.RightAlt => Key.LeftAlt,
            Key.LWin or Key.RWin => Key.LWin,
            _ => key
        };
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
    }

    private static ModifierKeys GetModifierFromKey(Key key)
    {
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
            Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
            Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
            Key.LWin or Key.RWin => ModifierKeys.Windows,
            _ => ModifierKeys.None
        };
    }

    private static ModifierKeys GetPressedModifiers()
    {
        var modifiers = ModifierKeys.None;
        if (IsVirtualKeyPressed(0x11))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsVirtualKeyPressed(0x10))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsVirtualKeyPressed(0x12))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (IsVirtualKeyPressed(0x5B) || IsVirtualKeyPressed(0x5C))
        {
            modifiers |= ModifierKeys.Windows;
        }

        return modifiers;
    }

    private static bool IsVirtualKeyPressed(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private sealed record ShortcutGesture(ModifierKeys Modifiers, Key? Key);
}
