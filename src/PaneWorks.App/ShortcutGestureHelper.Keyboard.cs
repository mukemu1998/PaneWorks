using System.Runtime.InteropServices;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace PaneWorks.App;

public static partial class ShortcutGestureHelper
{
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
        if (IsAnyVirtualKeyPressed(0x11, 0xA2, 0xA3))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsAnyVirtualKeyPressed(0x10, 0xA0, 0xA1))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsAnyVirtualKeyPressed(0x12, 0xA4, 0xA5))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (IsAnyVirtualKeyPressed(0x5B, 0x5C))
        {
            modifiers |= ModifierKeys.Windows;
        }

        return modifiers;
    }

    private static bool IsAnyVirtualKeyPressed(params int[] virtualKeys)
    {
        foreach (var virtualKey in virtualKeys)
        {
            if (IsVirtualKeyPressed(virtualKey))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVirtualKeyPressed(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
