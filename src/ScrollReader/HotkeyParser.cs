using System.Windows.Input;
using ScrollReader.Native;

namespace ScrollReader;

/// <summary>Parses hotkey specs like "Ctrl+Alt+R" or "Ctrl+Shift+Space".</summary>
public static class HotkeyParser
{
    public static bool TryParse(string spec, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

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
        if (keyName.Length == 1 && char.IsAsciiDigit(keyName[0])) keyName = "D" + keyName;
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) return false;

        // A hotkey without modifiers would shadow normal typing; only allow
        // it for function keys.
        if (modifiers == 0 && key is not (>= Key.F1 and <= Key.F24)) return false;

        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }
}
