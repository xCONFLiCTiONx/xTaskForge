using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace xTaskForge;

public static class PowerKeeper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;

    public static void KeepAwake()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
    }

    public static void RestoreSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS);
    }
}

public static class EfficiencyModeManager
{
    private const int ProcessPowerThrottling = 0x1A;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        uint ProcessInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    public static bool Apply(bool enable)
    {
        try
        {
            using (var proc = System.Diagnostics.Process.GetCurrentProcess())
            {
                proc.PriorityClass = enable ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal;
            }

            var throttlingState = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = enable ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
            };

            uint size = (uint)Marshal.SizeOf(throttlingState);
            return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref throttlingState, size);
        }
        catch
        {
            return false;
        }
    }
}

public enum AudioRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();

    [PreserveSig]
    int SetDefaultEndpoint(
        [In][MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId,
        [In][MarshalAs(UnmanagedType.U4)] AudioRole role);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class CPolicyConfigClient { }

public static class PeakDetector
{
    public static float GetPeak()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var defaultDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDev != null)
            {
                return defaultDev.AudioMeterInformation.MasterPeakValue;
            }
        }
        catch { }
        return 0f;
    }
}

public sealed class NativeAudioSleepMonitor : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private bool _isRunning;
    private bool _audioActive;
    private readonly object _lock = new();

    public event Action<string>? LogMessage;
    public event Action<bool, float>? AudioStateChanged;
    public bool IsRunning { get { lock (_lock) return _isRunning; } }
    public float CurrentPeak { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning) return;
            _isRunning = true;
            _audioActive = false;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _monitorTask = Task.Run(() => MonitorLoop(token));
        }
        LogMessage?.Invoke("Native Audio Sleep Monitor started (checking WASAPI every 1s).");
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            cts = _cts;
            task = _monitorTask;
            _cts = null;
            _monitorTask = null;
        }
        cts?.Cancel();
        if (task != null && cts != null)
            _ = task.ContinueWith(_ => cts.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        LogMessage?.Invoke("Native Audio Sleep Monitor stopping.");
    }

    private void MonitorLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                Tick();
                if (token.WaitHandle.WaitOne(1000)) break;
            }
        }
        finally
        {
            PowerProvider.ReleaseSleep("AudioPlayback");
            lock (_lock)
            {
                _audioActive = false;
            }
        }
    }

    private void Tick()
    {
        if (!IsRunning) return;
        float peak = PeakDetector.GetPeak();
        CurrentPeak = peak;
        bool isPlaying = peak > 0.0001f;
        if (isPlaying == _audioActive) return;

        _audioActive = isPlaying;
        if (_audioActive)
        {
            PowerProvider.PreventSleep("AudioPlayback");
            LogMessage?.Invoke($"[Audio Monitor] Audio detected (Peak: {peak:F3}). Keeping PC awake.");
        }
        else
        {
            PowerProvider.ReleaseSleep("AudioPlayback");
            LogMessage?.Invoke("[Audio Monitor] Audio silent. Normal sleep restored.");
        }

        AudioStateChanged?.Invoke(_audioActive, peak);
    }

    public void Dispose() => Stop();
}

public static class NativeDisplayManager
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll")]
    public static extern int EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll")]
    public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int CDS_UPDATEREGISTRY = 0x01;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    private const byte VK_LWIN = 0x5B;
    private const byte VK_MENU = 0x12; // ALT
    private const byte VK_B = 0x42;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static bool SetResolution(int width, int height)
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) != 0)
        {
            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmFields = 0x00080000 | 0x00100000; // DM_PELSWIDTH | DM_PELSHEIGHT
            int res = ChangeDisplaySettings(ref dm, CDS_UPDATEREGISTRY);
            return res == DISP_CHANGE_SUCCESSFUL;
        }
        return false;
    }

    public static void ToggleHdr()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_B, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        keybd_event(VK_B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}

public sealed class AudioOutputDeviceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public string IconFile { get; set; } = "speakers.ico";
}

public static class NativeAudioDeviceManager
{
    public static event Action<string>? LogMessage;

    public static List<AudioOutputDeviceInfo> GetOutputDevices()
    {
        var result = new List<AudioOutputDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            try
            {
                using var defaultDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = defaultDev?.ID;
            }
            catch { }

            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in devices)
            {
                string id = dev.ID;
                string name = dev.FriendlyName ?? "Audio Device";
                bool isDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
                string icon = DetectDeviceIcon(dev);

                result.Add(new AudioOutputDeviceInfo
                {
                    Id = id,
                    Name = name,
                    IsDefault = isDefault,
                    IconFile = icon
                });
            }

            LogMessage?.Invoke($"[Audio] Found {result.Count} active audio output device(s).");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Audio Exception] GetOutputDevices failed: {ex.Message}");
        }

        return result;
    }

    public static (bool success, string? error) SetDefaultAudioDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return (false, "Device ID is empty");
        try
        {
            var policyConfig = (IPolicyConfig)new CPolicyConfigClient();
            int hr0 = policyConfig.SetDefaultEndpoint(deviceId, AudioRole.Console);
            int hr1 = policyConfig.SetDefaultEndpoint(deviceId, AudioRole.Multimedia);
            int hr2 = policyConfig.SetDefaultEndpoint(deviceId, AudioRole.Communications);

            if (hr0 != 0 && hr1 != 0 && hr2 != 0)
            {
                return (false, $"IPolicyConfig::SetDefaultEndpoint failed with HRESULT 0x{hr0:X8}");
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static string DetectDeviceIcon(MMDevice dev)
    {
        try
        {
            string name = dev.FriendlyName ?? "";
            string iconPath = "";
            try { iconPath = dev.IconPath ?? ""; } catch { }

            string enumerator = "";
            string interfacePath = "";
            string deviceDesc = "";
            int formFactor = -1;

            try
            {
                for (int i = 0; i < dev.Properties.Count; i++)
                {
                    var key = dev.Properties.Get(i);
                    if (key.formatId == new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"))
                    {
                        if (key.propertyId == 24) enumerator = dev.Properties[key].Value?.ToString() ?? "";
                        else if (key.propertyId == 2) deviceDesc = dev.Properties[key].Value?.ToString() ?? "";
                    }
                    else if (key.formatId == new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc") && key.propertyId == 2)
                    {
                        interfacePath = dev.Properties[key].Value?.ToString() ?? "";
                    }
                    else if (key.formatId == new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e") && key.propertyId == 0)
                    {
                        var val = dev.Properties[key].Value;
                        if (val is int intVal) formFactor = intVal;
                        else if (val != null && int.TryParse(val.ToString(), out int parsed)) formFactor = parsed;
                    }
                }
            }
            catch { }

            // 1. Bluetooth
            if (enumerator.Contains("BTH", StringComparison.OrdinalIgnoreCase) ||
                enumerator.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                interfacePath.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                interfacePath.Contains("BTHHFENUM", StringComparison.OrdinalIgnoreCase) ||
                interfacePath.Contains("BTH\\", StringComparison.OrdinalIgnoreCase) ||
                interfacePath.Contains("BLUETOOTH", StringComparison.OrdinalIgnoreCase) ||
                iconPath.Contains("bth", StringComparison.OrdinalIgnoreCase) ||
                iconPath.Contains("bluetooth", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("bluetooth", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("hands-free", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("handsfree", StringComparison.OrdinalIgnoreCase))
            {
                return "bluetooth.ico";
            }

            // 2. Headphones
            if (formFactor == 3 || formFactor == 5 || formFactor == 6 ||
                iconPath.Contains("-3011") || iconPath.Contains("-3012") ||
                deviceDesc.Contains("headphone", StringComparison.OrdinalIgnoreCase) ||
                deviceDesc.Contains("headset", StringComparison.OrdinalIgnoreCase) ||
                deviceDesc.Contains("earphone", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("headphone", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("headset", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("earphone", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("earbud", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("airpod", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("buds", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("iem", StringComparison.OrdinalIgnoreCase))
            {
                return "headphones.ico";
            }
        }
        catch { }

        return "speakers.ico";
    }

    public static string GetIconForDeviceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "speakers.ico";
        string lower = name.ToLowerInvariant();
        if (lower.Contains("bluetooth") || lower.Contains("wireless") || lower.Contains("airpods") ||
            lower.Contains("buds") || lower.Contains("hands-free") || lower.Contains("bt ") || lower.Contains("freebuds"))
        {
            return "bluetooth.ico";
        }
        if (lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("earphone") ||
            lower.Contains("iem") || lower.Contains("phones"))
        {
            return "headphones.ico";
        }
        return "speakers.ico";
    }
}

public static class IconPickerHelper
{
    [DllImport("shell32.dll", EntryPoint = "PickIconDlg", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int PickIconDlgByName(IntPtr hwndOwner, StringBuilder pszIconPath, int cchIconPath, ref int piIconIndex);

    [DllImport("shell32.dll", EntryPoint = "#62", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int PickIconDlgByOrdinal(IntPtr hwndOwner, StringBuilder pszIconPath, int cchIconPath, ref int piIconIndex);

    public static bool ShowWindowsPickIconDialog(IntPtr hwndOwner, ref string iconPath, ref int iconIndex)
    {
        var sb = new StringBuilder(iconPath ?? "", 1024);
        int idx = iconIndex;
        int res = 0;
        try
        {
            res = PickIconDlgByName(hwndOwner, sb, sb.Capacity, ref idx);
        }
        catch (EntryPointNotFoundException)
        {
            res = PickIconDlgByOrdinal(hwndOwner, sb, sb.Capacity, ref idx);
        }

        if (res != 0)
        {
            iconPath = Environment.ExpandEnvironmentVariables(sb.ToString().Trim());
            iconIndex = idx;
            return true;
        }

        return false;
    }

    public static string? PickIcon(IntPtr hwndOwner, string? currentIconRef)
    {
        var (path, index) = MainWindow.ParseIconRef(currentIconRef);

        string defaultImageres = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "imageres.dll");

        string resolved = !string.IsNullOrWhiteSpace(path) ? MainWindow.ResolveFilePath(path) : "";
        string initialPath = !string.IsNullOrWhiteSpace(resolved) && System.IO.File.Exists(resolved)
            ? resolved
            : defaultImageres;

        int initialIndex = index >= 0 ? index : 0;

        if (ShowWindowsPickIconDialog(hwndOwner, ref initialPath, ref initialIndex))
        {
            string relativeOrShort = initialPath;
            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (initialPath.StartsWith(sysDir, StringComparison.OrdinalIgnoreCase))
            {
                relativeOrShort = System.IO.Path.GetFileName(initialPath);
            }

            return $"{relativeOrShort},{initialIndex}";
        }

        return null;
    }
}

public static class NativeWiFiOptimizer
{
    private static bool _isEnabled;
    public static event Action<string>? LogMessage;

    // BSSID inspection and selection use Windows Wi-Fi APIs that are
    // location-sensitive on current Windows versions. Leave AP roaming
    // to the WLAN AutoConfig service and the adapter driver instead.
    public static bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            LogMessage?.Invoke(value
                ? "Manual BSSID optimization is unavailable; Windows-managed roaming remains active."
                : "Native BSSID optimizer disabled. Windows-managed roaming remains active.");
        }
    }

    public static List<string> PreferredBssids { get; set; } = new()
    {
        "f0:9f:c2:d2:3e:0e",
        "f0:9f:c2:d2:3e:07"
    };

    public static string ProfileName { get; set; } = "jimosrvpark";
    public static string InterfaceName { get; set; } = "Wi-Fi";

    public static void Start()
    {
        _isEnabled = false;
        LogMessage?.Invoke("Location-safe Wi-Fi mode: xTaskForge will not inspect BSSIDs or scan nearby networks. Windows and the Wi-Fi adapter manage roaming.");
    }

    public static void Stop()
    {
        _isEnabled = false;
    }

    public static string CheckAndOptimize(bool force)
    {
        return "Windows-managed roaming active; BSSID inspection disabled.";
    }

    // Intentionally returns no BSSID; Windows manages AP roaming.
    public static string GetCurrentConnectedBssid() => "";

}
