using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VoidsMacroProject;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Voids Macro Project", "config.json");

    private MacroConfiguration _config = MacroConfiguration.CreateDefault();
    private MacroEngine? _engine;
    private string? _selectedBindingKey;
    private bool _refreshingControls;

    public MainWindow()
    {
        InitializeComponent();
        BuildVisualKeyboard();
        KeySelectorCombo.ItemsSource = KeyCatalog.KeyboardKeyNames;
        KeySelectorCombo.SelectedItem = "X";

        LoadDefaultConfiguration();
        _engine = new MacroEngine(OnEnginePresetChanged, OnEngineStatusChanged);
        _engine.UpdateConfiguration(_config);
        _engine.SelectPreset(_config.ActivePresetId);
        RefreshAll();

        if (!_engine.HooksAvailable)
        {
            EngineStatusText.Text = "INPUT HOOK UNAVAILABLE";
            EngineStatusText.Foreground = Brush("#F4B860");
            FooterStatusText.Text = $"Global input hook could not be installed: {_engine.HookError ?? "unknown error"}";
            ToggleMacroButton.IsEnabled = false;
        }
    }

    private PresetConfiguration CurrentPreset => _config.GetPreset(_config.ActivePresetId);

    private void BuildVisualKeyboard()
    {
        var rows = new string[][]
        {
            new[] { "Esc", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Print Screen", "Insert", "Delete" },
            new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "Backspace" },
            new[] { "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" },
            new[] { "Caps Lock", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", "Enter" },
            new[] { "Shift", "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "Shift" },
            new[] { "Ctrl", "Alt", "Space", "Alt", "Ctrl", "Home", "End", "Page Up", "Page Down", "Left", "Down", "Up", "Right" },
            new[] { "Num 7", "Num 8", "Num 9", "Num /", "Num 4", "Num 5", "Num 6", "Num *", "Num 1", "Num 2", "Num 3", "Num -", "Num 0", "Num .", "Num +" }
        };

        foreach (var rowKeys in rows)
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var key in rowKeys)
            {
                var button = new Button
                {
                    Content = key,
                    Tag = key,
                    Height = 34,
                    MinWidth = KeyWidth(key),
                    Padding = new Thickness(4, 4, 4, 4),
                    Margin = new Thickness(2),
                    FontSize = 11,
                    IsEnabled = !KeyCatalog.IsReservedPresetKey(key),
                    ToolTip = KeyCatalog.IsReservedPresetKey(key)
                        ? "Reserved for switching presets while the macro is enabled"
                        : $"Toggle {key} in the current preset"
                };
                button.Click += KeyboardKey_Click;
                row.Children.Add(button);
            }
            KeyboardRowsPanel.Children.Add(row);
        }

        AddMouseButton("Mouse Left", "Left Click");
        AddMouseButton("Mouse Right", "Right Click");
        AddMouseButton("Mouse Middle", "Middle Click");
    }

    private static double KeyWidth(string key) => key switch
    {
        "Space" => 145,
        "Backspace" => 74,
        "Caps Lock" => 72,
        "Print Screen" => 76,
        "Page Up" or "Page Down" => 70,
        "Enter" => 62,
        "Shift" => 58,
        "Tab" => 48,
        "Ctrl" or "Alt" => 45,
        "Home" or "End" or "Insert" or "Delete" => 47,
        _ => 35
    };

    private void AddMouseButton(string key, string label)
    {
        var button = new Button
        {
            Content = label,
            Tag = key,
            Height = 38,
            MinWidth = 100,
            Margin = new Thickness(3),
            ToolTip = $"Toggle {label} repetition in the current preset"
        };
        button.Click += KeyboardKey_Click;
        MouseButtonsPanel.Children.Add(button);
    }

    private void LoadDefaultConfiguration()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var loaded = JsonSerializer.Deserialize<MacroConfiguration>(File.ReadAllText(_configPath), JsonOptions);
                if (loaded is not null) _config = loaded;
            }
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Could not load saved settings; defaults loaded. {ex.Message}";
            _config = MacroConfiguration.CreateDefault();
        }
        _config.Normalize();
    }

    private void RefreshAll()
    {
        _refreshingControls = true;
        var preset = CurrentPreset;
        Preset1Button.Content = $"1  ·  {_config.GetPreset(1).Name}";
        Preset2Button.Content = $"2  ·  {_config.GetPreset(2).Name}";
        Preset3Button.Content = $"3  ·  {_config.GetPreset(3).Name}";
        Preset4Button.Content = $"4  ·  {_config.GetPreset(4).Name}";
        StylePresetButton(Preset1Button, _config.ActivePresetId == 1);
        StylePresetButton(Preset2Button, _config.ActivePresetId == 2);
        StylePresetButton(Preset3Button, _config.ActivePresetId == 3);
        StylePresetButton(Preset4Button, _config.ActivePresetId == 4);

        if (!PresetNameBox.IsKeyboardFocusWithin) PresetNameBox.Text = preset.Name;
        GlobalIntervalSlider.Value = _config.GlobalIntervalMs;
        GlobalIntervalBox.Text = _config.GlobalIntervalMs.ToString();
        ActivePresetStatusText.Text = $"Active preset: {_config.ActivePresetId} — {preset.Name}";
        _refreshingControls = false;

        RefreshAssignedKeysList();
        RefreshKeyboardStates();
        RefreshTimingPanel();
        UpdateEngineDisplay();
    }

    private static void StylePresetButton(Button button, bool active)
    {
        button.Background = Brush(active ? "#38275F" : "#1A2030");
        button.BorderBrush = Brush(active ? "#8B5CF6" : "#30394B");
    }

    private void RefreshAssignedKeysList()
    {
        _refreshingControls = true;
        AssignedKeysList.Items.Clear();
        var bindings = CurrentPreset.Bindings.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var binding in bindings)
        {
            var interval = binding.IntervalMs ?? _config.GlobalIntervalMs;
            var source = binding.IntervalMs.HasValue ? "custom" : "global";
            AssignedKeysList.Items.Add(new ListBoxItem
            {
                Content = $"{binding.Key}     ·     {interval} ms  ({source})",
                Tag = binding.Key
            });
        }
        AssignedCountText.Text = $"{bindings.Count} key{(bindings.Count == 1 ? "" : "s")}";

        if (_selectedBindingKey is not null)
        {
            foreach (var item in AssignedKeysList.Items.OfType<ListBoxItem>())
            {
                if (string.Equals(item.Tag as string, _selectedBindingKey, StringComparison.OrdinalIgnoreCase))
                {
                    AssignedKeysList.SelectedItem = item;
                    break;
                }
            }
        }
        if (AssignedKeysList.SelectedItem is ListBoxItem selected)
            _selectedBindingKey = selected.Tag as string;
        else if (_selectedBindingKey is not null && !bindings.Any(b => b.Key.Equals(_selectedBindingKey, StringComparison.OrdinalIgnoreCase)))
            _selectedBindingKey = null;
        _refreshingControls = false;
    }

    private void RefreshKeyboardStates()
    {
        var active = new HashSet<string>(CurrentPreset.Bindings.Select(b => b.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var button in DescendantButtons(KeyboardRowsPanel).Concat(DescendantButtons(MouseButtonsPanel)))
        {
            var key = button.Tag as string;
            if (key is null) continue;
            if (KeyCatalog.IsReservedPresetKey(key))
            {
                button.Background = Brush("#151922");
                button.BorderBrush = Brush("#272D39");
                button.Foreground = Brush("#5E687A");
                continue;
            }
            button.Foreground = Brush("#EEF2FA");
            button.Background = Brush(active.Contains(key) ? "#5B3CB2" : "#1A2030");
            button.BorderBrush = Brush(active.Contains(key) ? "#A48BFF" : "#30394B");
        }
    }

    private static IEnumerable<Button> DescendantButtons(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Button button) yield return button;
            foreach (var descendant in DescendantButtons(child)) yield return descendant;
        }
    }

    private void RefreshTimingPanel()
    {
        _refreshingControls = true;
        var binding = _selectedBindingKey is null
            ? null
            : CurrentPreset.Bindings.FirstOrDefault(b => b.Key.Equals(_selectedBindingKey, StringComparison.OrdinalIgnoreCase));

        var hasSelection = binding is not null;
        SelectedKeyTitle.Text = hasSelection ? binding!.Key : "Select a key above";
        UseCustomIntervalCheckBox.IsEnabled = hasSelection;
        UseCustomIntervalCheckBox.IsChecked = binding?.IntervalMs.HasValue ?? false;
        var shownInterval = binding?.IntervalMs ?? _config.GlobalIntervalMs;
        CustomIntervalSlider.Value = shownInterval;
        CustomIntervalBox.Text = shownInterval.ToString();
        CustomIntervalSlider.IsEnabled = hasSelection && binding?.IntervalMs.HasValue == true;
        CustomIntervalBox.IsEnabled = hasSelection && binding?.IntervalMs.HasValue == true;
        IntervalSourceText.Text = binding?.IntervalMs.HasValue == true ? "Custom override" : "Using global default";
        RemoveKeyButton.IsEnabled = hasSelection;
        _refreshingControls = false;
    }

    private void UpdateEngineDisplay()
    {
        if (_engine is null) return;
        if (_engine.IsEnabled)
        {
            EngineStatusText.Text = "MACRO ENABLED";
            EngineStatusText.Foreground = Brush("#75E0B1");
            ToggleMacroButton.Content = "Disable Macro";
            ToggleMacroButton.Background = Brush("#264D41");
            ToggleMacroButton.BorderBrush = Brush("#3D9E7C");
        }
        else
        {
            EngineStatusText.Text = "MACRO DISABLED";
            EngineStatusText.Foreground = Brush("#A8B1C3");
            ToggleMacroButton.Content = "Enable Macro";
            ToggleMacroButton.Background = Brush("#5B3CB2");
            ToggleMacroButton.BorderBrush = Brush("#7959D6");
        }
    }

    private static SolidColorBrush Brush(string color)
    {
        var hex = color.TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException($"Invalid RGB color: {color}", nameof(color));
        var r = (byte)((rgb >> 16) & 0xFF);
        var g = (byte)((rgb >> 8) & 0xFF);
        var b = (byte)(rgb & 0xFF);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private void KeyboardKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;
        ToggleKeyBinding(key);
    }

    private void ToggleKeyBinding(string key)
    {
        if (KeyCatalog.IsReservedPresetKey(key))
        {
            FooterStatusText.Text = "Keys 1–4 are reserved for preset switching while the macro is enabled.";
            return;
        }
        var existing = CurrentPreset.Bindings.FirstOrDefault(b => b.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            CurrentPreset.Bindings.Remove(existing);
            _selectedBindingKey = null;
            FooterStatusText.Text = $"Removed {key} from preset {_config.ActivePresetId}.";
        }
        else
        {
            CurrentPreset.Bindings.Add(new KeyBindingConfiguration { Key = key, IntervalMs = null });
            _selectedBindingKey = key;
            FooterStatusText.Text = $"Added {key} to preset {_config.ActivePresetId}. Hold it while the macro is enabled to repeat.";
        }
        ConfigurationChanged();
        RefreshAll();
        SelectAssignedKey(_selectedBindingKey);
    }

    private void SelectAssignedKey(string? key)
    {
        if (key is null) return;
        foreach (var item in AssignedKeysList.Items.OfType<ListBoxItem>())
        {
            if (string.Equals(item.Tag as string, key, StringComparison.OrdinalIgnoreCase))
            {
                AssignedKeysList.SelectedItem = item;
                item.Focus();
                break;
            }
        }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string idText || !int.TryParse(idText, out var id)) return;
        ChangePreset(id);
    }

    private void ChangePreset(int id)
    {
        _config.ActivePresetId = Math.Clamp(id, 1, 4);
        _engine?.SelectPreset(_config.ActivePresetId);
        _selectedBindingKey = null;
        RefreshAll();
        SaveDefaultConfiguration(false);
    }

    private void PresetNameBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        var name = PresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = $"Preset {_config.ActivePresetId}";
        CurrentPreset.Name = name.Length > 32 ? name[..32] : name;
        ConfigurationChanged();
        RefreshAll();
    }

    private void GlobalIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // WPF can raise ValueChanged while InitializeComponent is still creating
        // controls. Do not touch other named controls until XAML initialization completes.
        if (_refreshingControls || AssignedKeysList is null) return;
        _config.GlobalIntervalMs = Math.Clamp((int)Math.Round(GlobalIntervalSlider.Value), 10, 2000);
        _refreshingControls = true;
        GlobalIntervalBox.Text = _config.GlobalIntervalMs.ToString();
        _refreshingControls = false;
        ConfigurationChanged();
        RefreshAssignedKeysList();
        RefreshTimingPanel();
    }

    private void GlobalIntervalBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (int.TryParse(GlobalIntervalBox.Text, out var value))
            GlobalIntervalSlider.Value = Math.Clamp(value, 10, 2000);
        else
        {
            GlobalIntervalBox.Text = _config.GlobalIntervalMs.ToString();
            FooterStatusText.Text = "Enter a global interval from 10 to 2000 ms.";
        }
    }

    private void AssignedKeysList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingControls) return;
        _selectedBindingKey = (AssignedKeysList.SelectedItem as ListBoxItem)?.Tag as string;
        RefreshTimingPanel();
    }

    private void AddKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (KeySelectorCombo.SelectedItem is string key) ToggleKeyBinding(key);
    }

    private void RemoveKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBindingKey is null) return;
        var key = _selectedBindingKey;
        CurrentPreset.Bindings.RemoveAll(b => b.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        _selectedBindingKey = null;
        FooterStatusText.Text = $"Removed {key} from preset {_config.ActivePresetId}.";
        ConfigurationChanged();
        RefreshAll();
    }

    private void UseCustomIntervalCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshingControls || _selectedBindingKey is null) return;
        var binding = CurrentPreset.Bindings.FirstOrDefault(b => b.Key.Equals(_selectedBindingKey, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return;
        binding.IntervalMs = UseCustomIntervalCheckBox.IsChecked == true
            ? _config.GlobalIntervalMs
            : null;
        ConfigurationChanged();
        RefreshAssignedKeysList();
        RefreshTimingPanel();
    }

    private void CustomIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshingControls || _selectedBindingKey is null || UseCustomIntervalCheckBox.IsChecked != true) return;
        var binding = CurrentPreset.Bindings.FirstOrDefault(b => b.Key.Equals(_selectedBindingKey, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return;
        binding.IntervalMs = Math.Clamp((int)Math.Round(CustomIntervalSlider.Value), 10, 2000);
        _refreshingControls = true;
        CustomIntervalBox.Text = binding.IntervalMs.Value.ToString();
        _refreshingControls = false;
        ConfigurationChanged();
        RefreshAssignedKeysList();
    }

    private void CustomIntervalBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (UseCustomIntervalCheckBox.IsChecked != true || _selectedBindingKey is null) return;
        if (int.TryParse(CustomIntervalBox.Text, out var value))
            CustomIntervalSlider.Value = Math.Clamp(value, 10, 2000);
        else
        {
            RefreshTimingPanel();
            FooterStatusText.Text = "Enter a per-key interval from 10 to 2000 ms.";
        }
    }

    private void ToggleMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || !_engine.HooksAvailable) return;
        _engine.SetEnabled(!_engine.IsEnabled);
        UpdateEngineDisplay();
        FooterStatusText.Text = _engine.IsEnabled
            ? "Macro enabled. Hold a configured key to repeat it."
            : "Macro disabled. All repetition stopped; physical inputs remain normal.";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveDefaultConfiguration(true)) FooterStatusText.Text = $"Settings saved to {_configPath}";
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Voids Macro Project settings",
            Filter = "JSON configuration (*.json)|*.json",
            FileName = "voids-macro-config.json",
            DefaultExt = ".json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _config.Normalize();
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_config, JsonOptions));
            FooterStatusText.Text = $"Exported settings to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not export settings.\n\n{ex.Message}", "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Voids Macro Project settings",
            Filter = "JSON configuration (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = JsonSerializer.Deserialize<MacroConfiguration>(File.ReadAllText(dialog.FileName), JsonOptions)
                ?? throw new InvalidDataException("The selected file does not contain a valid configuration.");
            imported.Normalize();
            _config = imported;
            _selectedBindingKey = null;
            _engine?.UpdateConfiguration(_config);
            _engine?.SelectPreset(_config.ActivePresetId);
            RefreshAll();
            SaveDefaultConfiguration(false);
            FooterStatusText.Text = $"Imported settings from {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not import settings. The current configuration was kept.\n\n{ex.Message}", "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfigurationChanged()
    {
        _config.Normalize();
        _engine?.UpdateConfiguration(_config);
        SaveDefaultConfiguration(false);
    }

    private bool SaveDefaultConfiguration(bool showError)
    {
        try
        {
            _config.Normalize();
            var directory = Path.GetDirectoryName(_configPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            if (showError)
                MessageBox.Show(this, $"Could not save settings.\n\n{ex.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            else
                FooterStatusText.Text = $"Could not auto-save settings: {ex.Message}";
            return false;
        }
    }

    private void OnEnginePresetChanged(int presetId)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _config.ActivePresetId = presetId;
            _selectedBindingKey = null;
            RefreshAll();
            SaveDefaultConfiguration(false);
        }));
    }

    private void OnEngineStatusChanged(string message)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_engine is null) return;
            FooterStatusText.Text = message;
            UpdateEngineDisplay();
        }));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveDefaultConfiguration(false);
        _engine?.Dispose();
    }
}
