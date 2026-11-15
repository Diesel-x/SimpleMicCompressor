using System.Collections.Generic;

namespace MicCompressor.Models;

public class HotkeySetting
{
    public bool Enabled { get; set; } = true;
    public bool Ctrl { get; set; } = true;
    public bool Shift { get; set; } = true;
    public bool Alt { get; set; } = false;
    public string Key { get; set; } = "B";

    public static readonly IReadOnlyList<string> AvailableKeys = new List<string>
    {
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        "SPACE", "TILDE", "TAB", "INSERT", "DELETE", "HOME", "END", "PAGEUP", "PAGEDOWN",
        "NUM0", "NUM1", "NUM2", "NUM3", "NUM4", "NUM5", "NUM6", "NUM7", "NUM8", "NUM9",
        "PAUSE", "SCROLL"
    };

    public uint GetVirtualKeyCode()
    {
        if (string.IsNullOrWhiteSpace(Key)) return 0;
        string k = Key.Trim().ToUpperInvariant();

        if (k.Length == 1 && k[0] >= 'A' && k[0] <= 'Z') return (uint)k[0];
        if (k.Length == 1 && k[0] >= '0' && k[0] <= '9') return (uint)k[0];

        if (k.StartsWith("F") && int.TryParse(k[1..], out int f) && f >= 1 && f <= 12)
        {
            return (uint)(0x70 + (f - 1)); // VK_F1 = 0x70
        }

        if (k.StartsWith("NUM") && int.TryParse(k[3..], out int num) && num >= 0 && num <= 9)
        {
            return (uint)(0x60 + num); // VK_NUMPAD0 = 0x60
        }

        return k switch
        {
            "SPACE" => 0x20,
            "TILDE" or "`" or "~" => 0xC0, // VK_OEM_3
            "TAB" => 0x09,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "PAUSE" => 0x13,
            "SCROLL" => 0x91,
            _ => (uint)k[0]
        };
    }

    public uint GetModifiers()
    {
        uint mods = 0x4000; // MOD_NOREPEAT (prevent auto-repeat)
        if (Alt) mods |= 0x0001; // MOD_ALT
        if (Ctrl) mods |= 0x0002; // MOD_CONTROL
        if (Shift) mods |= 0x0004; // MOD_SHIFT
        return mods;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}
