using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoidsMacroProject;

/// <summary>
/// Watches physical keyboard/mouse input globally. Physical events are never blocked;
/// this engine only adds repeated input for configured held keys.
/// </summary>
public sealed class MacroEngine : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const uint LlkhfInjected = 0x00000012;
    private const uint LlmhfInjected = 0x00000003;

    private readonly object _gate = new();
    private readonly Action<int> _presetChanged;
    private readonly Action<string> _statusChanged;
    private readonly HookProc _keyboardCallback;
    private readonly HookProc _mouseCallback;
    private readonly HashSet<string> _physicallyHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _repeaters = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, Dictionary<string, int?>> _bindings = new();
    private int _globalIntervalMs = 100;
    private int _activePresetId = 1;
    private bool _enabled;
    private bool _disposed;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    public bool HooksAvailable { get; private set; }
    public string? HookError { get; private set; }

    public bool IsEnabled
    {
        get { lock (_gate) return _enabled; }
    }

    public int ActivePresetId
    {
        get { lock (_gate) return _activePresetId; }
    }

    public MacroEngine(Action<int> presetChanged, Action<string> statusChanged)
    {
        _presetChanged = presetChanged;
        _statusChanged = statusChanged;
        _keyboardCallback = KeyboardHookCallback;
        _mouseCallback = MouseHookCallback;
        InstallHooks();
    }

    public void UpdateConfiguration(MacroConfiguration config)
    {
        config.Normalize();
        lock (_gate)
        {
            _globalIntervalMs = Math.Clamp(config.GlobalIntervalMs, 10, 2000);
            var newBindings = new Dictionary<int, Dictionary<string, int?>>();
            foreach (var preset in config.Presets)
            {
                var map = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
                foreach (var binding in preset.Bindings)
                    map[binding.Key] = binding.IntervalMs;
                newBindings[preset.Id] = map;
            }
            _bindings = newBindings;
            _activePresetId = Math.Clamp(_activePresetId, 1, 4);

            foreach (var key in _repeaters.Keys.ToList())
            {
                if (!CanKeyRepeatLocked(key)) StopRepeaterLocked(key);
            }
        }
    }

    public void SelectPreset(int presetId)
    {
        presetId = Math.Clamp(presetId, 1, 4);
        bool changed;
        lock (_gate)
        {
            changed = _activePresetId != presetId;
            if (!changed) return;
            _activePresetId = presetId;
            StopAllRepeatersLocked();
        }
        _presetChanged(presetId);
        _statusChanged($"Preset {presetId} selected. Existing repetitions stopped.");
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            if (!enabled) StopAllRepeatersLocked();
        }
        _statusChanged(enabled
            ? "Macro enabled. Hold a configured key to repeat it."
            : "Macro disabled. Repetition stopped; physical input remains normal.");
    }

    private bool CanKeyRepeatLocked(string key)
    {
        return _enabled
            && _bindings.TryGetValue(_activePresetId, out var map)
            && map.ContainsKey(key)
            && !KeyCatalog.IsReservedPresetKey(key);
    }

    private void OnPhysicalKeyDown(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        int? changedPreset = null;
        bool started = false;
        lock (_gate)
        {
            // Low-level hooks receive repeated keydown notifications while a key is held.
            if (!_physicallyHeld.Add(key)) return;

            if (KeyCatalog.IsReservedPresetKey(key))
            {
                // The number keys are preset selectors only while the macro engine is enabled.
                // When disabled, all of their behavior is ordinary physical input.
                if (_enabled)
                {
                    var nextPreset = int.Parse(key);
                    if (_activePresetId != nextPreset)
                    {
                        _activePresetId = nextPreset;
                        StopAllRepeatersLocked();
                        changedPreset = nextPreset;
                    }
                }
            }
            else if (CanKeyRepeatLocked(key) && !_repeaters.ContainsKey(key))
            {
                var cancellation = new CancellationTokenSource();
                _repeaters[key] = cancellation;
                _ = Task.Run(() => RepeatLoopAsync(key, cancellation));
                started = true;
            }
        }

        if (changedPreset.HasValue)
        {
            _presetChanged(changedPreset.Value);
            _statusChanged($"Switched to preset {changedPreset.Value}. Existing repetitions stopped.");
        }
        else if (started)
        {
            _statusChanged($"Repeating {key} while held.");
        }
    }

    private void OnPhysicalKeyUp(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        bool stopped;
        lock (_gate)
        {
            _physicallyHeld.Remove(key);
            stopped = _repeaters.ContainsKey(key);
            StopRepeaterLocked(key);
        }
        if (stopped) _statusChanged($"Stopped repeating {key}.");
    }

    private async Task RepeatLoopAsync(string key, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            while (true)
            {
                int interval;
                lock (_gate)
                {
                    if (!IsRepeaterValidLocked(key, cancellation)) break;
                    interval = _bindings[_activePresetId][key] ?? _globalIntervalMs;
                    interval = Math.Clamp(interval, 10, 2000);
                }

                await Task.Delay(interval, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) break;

                lock (_gate)
                {
                    if (!IsRepeaterValidLocked(key, cancellation)) break;
                    // SendInput is called under the gate so a preset switch/disable cannot
                    // race between the final validity check and sending a pulse.
                    WindowsInput.Pulse(key);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the key is released, the preset changes, or the engine is disabled.
        }
        catch (Exception ex)
        {
            _statusChanged($"Input repetition error for {key}: {ex.Message}");
        }
        finally
        {
            lock (_gate)
            {
                if (_repeaters.TryGetValue(key, out var current) && ReferenceEquals(current, cancellation))
                    _repeaters.Remove(key);
            }
            cancellation.Dispose();
        }
    }

    private bool IsRepeaterValidLocked(string key, CancellationTokenSource cancellation)
    {
        return _repeaters.TryGetValue(key, out var current)
            && ReferenceEquals(current, cancellation)
            && !cancellation.IsCancellationRequested
            && _physicallyHeld.Contains(key)
            && CanKeyRepeatLocked(key);
    }

    private void StopRepeaterLocked(string key)
    {
        if (_repeaters.Remove(key, out var cancellation))
            cancellation.Cancel();
    }

    private void StopAllRepeatersLocked()
    {
        foreach (var cancellation in _repeaters.Values) cancellation.Cancel();
        _repeaters.Clear();
    }

    private void InstallHooks()
    {
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardCallback, module, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            HookError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            HooksAvailable = false;
            _statusChanged($"Could not install keyboard hook: {HookError}");
            return;
        }

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseCallback, module, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            HookError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            _ = UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            HooksAvailable = false;
            _statusChanged($"Could not install mouse hook: {HookError}");
            return;
        }
        HooksAvailable = true;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            try
            {
                var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
                if ((data.Flags & LlkhfInjected) == 0)
                {
                    var message = wParam.ToInt32();
                    var name = KeyCatalog.FromVirtualKey(unchecked((int)data.VirtualKey));
                    if (name is not null)
                    {
                        if (message is WmKeyDown or WmSysKeyDown) OnPhysicalKeyDown(name);
                        else if (message is WmKeyUp or WmSysKeyUp) OnPhysicalKeyUp(name);
                    }
                }
            }
            catch (Exception ex)
            {
                _statusChanged($"Keyboard hook error: {ex.Message}");
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            try
            {
                var data = Marshal.PtrToStructure<MouseHookData>(lParam);
                if ((data.Flags & LlmhfInjected) == 0)
                {
                    var message = wParam.ToInt32();
                    var key = message switch
                    {
                        WmLButtonDown or WmLButtonUp => "Mouse Left",
                        WmRButtonDown or WmRButtonUp => "Mouse Right",
                        WmMButtonDown or WmMButtonUp => "Mouse Middle",
                        _ => null
                    };
                    if (key is not null)
                    {
                        if (message is WmLButtonDown or WmRButtonDown or WmMButtonDown) OnPhysicalKeyDown(key);
                        else if (message is WmLButtonUp or WmRButtonUp or WmMButtonUp) OnPhysicalKeyUp(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _statusChanged($"Mouse hook error: {ex.Message}");
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopAllRepeatersLocked();
        }
        if (_keyboardHook != IntPtr.Zero) _ = UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) _ = UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
