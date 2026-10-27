namespace VoidsMacroProject;

public static class KeyCatalog
{
    public static readonly Dictionary<string, ushort> KeyToVirtualKey = BuildKeyMap();
    public static readonly IReadOnlyList<string> KeyboardKeyNames = KeyToVirtualKey.Keys
        .Where(k => !IsReservedPresetKey(k))
        .OrderBy(KeySortGroup)
        .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static Dictionary<string, ushort> BuildKeyMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) map[c.ToString()] = c;
        for (int i = 1; i <= 12; i++) map[$"F{i}"] = (ushort)(0x70 + i - 1);

        void Add(string name, ushort vk) => map[name] = vk;
        Add("Esc", 0x1B); Add("Tab", 0x09); Add("Caps Lock", 0x14);
        Add("Shift", 0x10); Add("Ctrl", 0x11); Add("Alt", 0x12);
        Add("Space", 0x20); Add("Enter", 0x0D); Add("Backspace", 0x08);
        Add("Insert", 0x2D); Add("Delete", 0x2E); Add("Home", 0x24); Add("End", 0x23);
        Add("Page Up", 0x21); Add("Page Down", 0x22); Add("Print Screen", 0x2C);
        Add("Up", 0x26); Add("Down", 0x28); Add("Left", 0x25); Add("Right", 0x27);
        Add("-", 0xBD); Add("=", 0xBB); Add("[", 0xDB); Add("]", 0xDD);
        Add("\\", 0xDC); Add(";", 0xBA); Add("'", 0xDE); Add(",", 0xBC);
        Add(".", 0xBE); Add("/", 0xBF); Add("`", 0xC0);
        for (int i = 0; i <= 9; i++) Add($"Num {i}", (ushort)(0x60 + i));
        Add("Num *", 0x6A); Add("Num +", 0x6B); Add("Num -", 0x6D);
        Add("Num .", 0x6E); Add("Num /", 0x6F);
        return map;
    }

    public static bool IsMouseKey(string key) =>
        key.Equals("Mouse Left", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Mouse Right", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Mouse Middle", StringComparison.OrdinalIgnoreCase);

    public static string? Canonicalize(string key)
    {
        var keyboardKey = KeyToVirtualKey.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (keyboardKey is not null) return keyboardKey;
        if (key.Equals("Mouse Left", StringComparison.OrdinalIgnoreCase)) return "Mouse Left";
        if (key.Equals("Mouse Right", StringComparison.OrdinalIgnoreCase)) return "Mouse Right";
        if (key.Equals("Mouse Middle", StringComparison.OrdinalIgnoreCase)) return "Mouse Middle";
        return null;
    }

    public static bool IsReservedPresetKey(string key) => key is "1" or "2" or "3" or "4";

    public static string? FromVirtualKey(int vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x7B) return $"F{vk - 0x70 + 1}";
        if (vk >= 0x60 && vk <= 0x69) return $"Num {vk - 0x60}";
        return vk switch
        {
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x10 or 0xA0 or 0xA1 => "Shift",
            0x11 or 0xA2 or 0xA3 => "Ctrl", 0x12 or 0xA4 or 0xA5 => "Alt", 0x14 => "Caps Lock",
            0x1B => "Esc", 0x20 => "Space", 0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End",
            0x24 => "Home", 0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x2C => "Print Screen", 0x2D => "Insert", 0x2E => "Delete",
            0x6A => "Num *", 0x6B => "Num +", 0x6D => "Num -", 0x6E => "Num .", 0x6F => "Num /",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/",
            0xC0 => "`", 0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
            _ => null
        };
    }

    private static int KeySortGroup(string key)
    {
        if (key.Length == 1 && char.IsLetter(key[0])) return 0;
        if (key.Length == 1 && char.IsDigit(key[0])) return 1;
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.AsSpan(1), out _)) return 2;
        if (key.StartsWith("Num ", StringComparison.OrdinalIgnoreCase)) return 4;
        return 3;
    }
}
