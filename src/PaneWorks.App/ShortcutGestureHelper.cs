using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace PaneWorks.App;

public static partial class ShortcutGestureHelper
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

    private sealed record ShortcutGesture(ModifierKeys Modifiers, Key? Key);
}
