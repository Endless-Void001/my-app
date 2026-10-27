using System.Text.Json.Serialization;

namespace VoidsMacroProject;

public sealed class MacroConfiguration
{
    public int Version { get; set; } = 1;
    public int GlobalIntervalMs { get; set; } = 100;
    public int ActivePresetId { get; set; } = 1;
    public List<PresetConfiguration> Presets { get; set; } = CreateDefaultPresets();

    public static MacroConfiguration CreateDefault() => new();

    private static List<PresetConfiguration> CreateDefaultPresets() => new()
    {
        new PresetConfiguration { Id = 1, Name = "Fighting Style" },
        new PresetConfiguration { Id = 2, Name = "Fruit" },
        new PresetConfiguration { Id = 3, Name = "Sword" },
        new PresetConfiguration { Id = 4, Name = "Gun" }
    };

    public PresetConfiguration GetPreset(int id)
    {
        Presets ??= CreateDefaultPresets();
        var preset = Presets.FirstOrDefault(p => p.Id == id);
        if (preset is not null) return preset;
        preset = new PresetConfiguration { Id = id, Name = $"Preset {id}" };
        Presets.Add(preset);
        return preset;
    }

    public void Normalize()
    {
        GlobalIntervalMs = Math.Clamp(GlobalIntervalMs, 10, 2000);
        ActivePresetId = Math.Clamp(ActivePresetId, 1, 4);
        Presets ??= new List<PresetConfiguration>();

        var oldPresets = Presets;
        Presets = new List<PresetConfiguration>();
        for (var id = 1; id <= 4; id++)
        {
            var preset = oldPresets.FirstOrDefault(p => p.Id == id) ?? new PresetConfiguration
            {
                Id = id,
                Name = id switch { 1 => "Fighting Style", 2 => "Fruit", 3 => "Sword", _ => "Gun" }
            };
            preset.Id = id;
            if (string.IsNullOrWhiteSpace(preset.Name)) preset.Name = $"Preset {id}";
            preset.Name = preset.Name.Trim();
            preset.Bindings ??= new List<KeyBindingConfiguration>();

            var unique = new Dictionary<string, KeyBindingConfiguration>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in preset.Bindings)
            {
                if (binding is null || string.IsNullOrWhiteSpace(binding.Key)) continue;
                var key = KeyCatalog.Canonicalize(binding.Key.Trim());
                if (key is null) continue;
                if (KeyCatalog.IsReservedPresetKey(key)) continue;
                binding.Key = key;
                if (binding.IntervalMs.HasValue)
                    binding.IntervalMs = Math.Clamp(binding.IntervalMs.Value, 10, 2000);
                unique[key] = binding;
            }
            preset.Bindings = unique.Values.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase).ToList();
            Presets.Add(preset);
        }
    }
}

public sealed class PresetConfiguration
{
    public int Id { get; set; }
    public string Name { get; set; } = "Preset";
    public List<KeyBindingConfiguration> Bindings { get; set; } = new();
}

public sealed class KeyBindingConfiguration
{
    public string Key { get; set; } = "A";
    // null means use the global default interval.
    public int? IntervalMs { get; set; }
}
