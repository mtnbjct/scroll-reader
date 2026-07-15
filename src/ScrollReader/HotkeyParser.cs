using System.Windows.Input;
using ScrollReader.Native;

namespace ScrollReader;

/// <summary>A parsed activation hotkey: either a keyboard chord or a middle-click chord.</summary>
public readonly record struct HotkeySpec(uint Modifiers, uint Vk, bool IsMiddleClick);

/// <summary>Parses hotkey specs like "Ctrl+Alt+R", "F9", or "Ctrl+MiddleClick".</summary>
public static class HotkeyParser
{
    public static bool TryParse(string spec, out HotkeySpec hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        uint modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "win" or "windows": modifiers |= NativeMethods.MOD_WIN; break;
                default: return false;
            }
        }

        var keyName = parts[^1];
        if (keyName.ToLowerInvariant() is "middleclick" or "middle" or "mbutton")
        {
            hotkey = new HotkeySpec(modifiers, 0, IsMiddleClick: true);
            return true;
        }

        if (keyName.Length == 1 && char.IsAsciiDigit(keyName[0])) keyName = "D" + keyName;
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) return false;

        // A keyboard hotkey without modifiers would shadow normal typing;
        // only allow it for function keys.
        if (modifiers == 0 && key is not (>= Key.F1 and <= Key.F24)) return false;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return false;
        hotkey = new HotkeySpec(modifiers, vk, IsMiddleClick: false);
        return true;
    }
}
