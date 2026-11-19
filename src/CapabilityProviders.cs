using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;

namespace xTaskForge;

#region Display Provider

public class DisplayProfile
{
    public string Name { get; set; } = "Default";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int RefreshRate { get; set; } = 60;
}

public static class DisplayProvider
{
    public static List<string> GetSupportedModes()
    {
        var modes = new List<string>();
        try
        {
            var dm = new NativeDisplayManager.DEVMODE();
            dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeDisplayManager.DEVMODE));
            int modeNum = 0;
            while (NativeDisplayManager.EnumDisplaySettings(null, modeNum, ref dm) != 0)
            {
                string modeStr = $"{dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency}Hz ({dm.dmBitsPerPel}-bit)";
                if (!modes.Contains(modeStr))
                {
                    modes.Add(modeStr);
                }
                modeNum++;
            }
        }
        catch { }
        return modes;
    }

    public static (bool success, string message) ApplyResolution(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return (false, "Invalid resolution dimensions");

        bool ok = NativeDisplayManager.SetResolution(width, height);
        if (ok)
            return (true, $"Resolution changed to {width}x{height}");
        else
            return (false, $"Failed to change resolution to {width}x{height}");
    }

    public static (bool success, string message) ApplyProfile(DisplayProfile profile)
    {
        if (profile == null) return (false, "Null profile provided");
        return ApplyResolution(profile.Width, profile.Height);
    }
}

#endregion

#region HDR Provider

public static class HdrProvider
{
    public static (bool success, string message) ToggleHdr()
    {
        try
        {
            NativeDisplayManager.ToggleHdr();
            return (true, "HDR toggle key sequence triggered (Win+Alt+B)");
        }
        catch (Exception ex)
        {
            return (false, $"HDR toggle failed: {ex.Message}");
        }
    }
}

#endregion

#region Power Management Provider

public static class PowerProvider
{
    private static readonly HashSet<string> ActiveReasons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    public static void PreventSleep(string reason)
    {
        lock (Lock)
        {
            if (string.IsNullOrWhiteSpace(reason)) reason = "General";
            ActiveReasons.Add(reason);
            PowerKeeper.KeepAwake();
        }
    }

    public static void ReleaseSleep(string reason)
    {
        lock (Lock)
        {
            if (string.IsNullOrWhiteSpace(reason)) reason = "General";
            ActiveReasons.Remove(reason);
            if (ActiveReasons.Count == 0)
            {
                PowerKeeper.RestoreSleep();
            }
        }
    }

    public static object GetStatus()
    {
        lock (Lock)
        {
            return new
            {
                isAwakeHeld = ActiveReasons.Count > 0,
                activeReasons = ActiveReasons.ToList()
            };
        }
    }
}

#endregion

#region Audio Provider

public static class AudioProvider
{
    public static List<AudioOutputDeviceInfo> ListDevices()
    {
        return NativeAudioDeviceManager.GetOutputDevices();
    }

    public static (bool success, string message) SetDefaultDevice(string deviceId)
    {
        var (success, error) = NativeAudioDeviceManager.SetDefaultAudioDevice(deviceId);
        if (success)
            return (true, $"Default audio device set to {deviceId}");
        return (false, error ?? "Failed to set default audio device");
    }

    public static (bool success, float volume, string? error) GetMasterVolume()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (dev != null)
            {
                float vol = dev.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
                return (true, vol, null);
            }
            return (false, 0f, "No default audio endpoint found");
        }
        catch (Exception ex)
        {
            return (false, 0f, ex.Message);
        }
    }

    public static (bool success, string message) SetMasterVolume(float levelPercent)
    {
        try
        {
            levelPercent = Math.Clamp(levelPercent, 0f, 100f);
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (dev != null)
            {
                dev.AudioEndpointVolume.MasterVolumeLevelScalar = levelPercent / 100f;
                return (true, $"Master volume set to {levelPercent:F0}%");
            }
            return (false, "No default audio endpoint found");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to set master volume: {ex.Message}");
        }
    }

    public static (bool success, bool isMuted, string? error) GetMute()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (dev != null)
            {
                return (true, dev.AudioEndpointVolume.Mute, null);
            }
            return (false, false, "No default audio endpoint found");
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    public static (bool success, string message) SetMute(bool isMuted)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (dev != null)
            {
                dev.AudioEndpointVolume.Mute = isMuted;
                return (true, $"Audio mute state set to {isMuted}");
            }
            return (false, "No default audio endpoint found");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to set mute: {ex.Message}");
        }
    }
}

#endregion

#region Picture-in-Picture Provider

public static class PipProvider
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TOPMOST = 0x8;
    private const uint LWA_ALPHA = 0x2;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private enum PipWindowState
    {
        Unknown,
        OpaqueNormal,
        HoverHidden
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    private static readonly Dictionary<IntPtr, PipWindowState> _windowStates = new Dictionary<IntPtr, PipWindowState>();
    private static DateTime _lastEnumTime = DateTime.MinValue;
    private static List<IntPtr> _cachedPiPWindows = new List<IntPtr>();

    public static bool IsTrackingActive => _hoverTrackingActive;

    public static List<IntPtr> GetOrRefreshPiPWindows()
    {
        if ((DateTime.UtcNow - _lastEnumTime).TotalMilliseconds > 1000)
        {
            _cachedPiPWindows = FindPiPWindows();
            _lastEnumTime = DateTime.UtcNow;
        }
        else
        {
            _cachedPiPWindows.RemoveAll(hWnd => !IsWindow(hWnd) || !IsWindowVisible(hWnd));
        }
        return _cachedPiPWindows;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    private static bool IsSmallTopmostWindow(IntPtr hWnd)
    {
        // A real PiP window is a small always-on-top popup. Never match full browser windows.
        if (!GetWindowRect(hWnd, out RECT r)) return false;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return false;
        if ((GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) return false;
        var screen = System.Windows.Forms.Screen.FromHandle(hWnd).Bounds;
        return w <= screen.Width * 0.6 && h <= screen.Height * 0.6;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static bool IsShellForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        var sb = new System.Text.StringBuilder(256);
        GetClassName(fg, sb, 256);
        string c = sb.ToString();
        return c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "Windows.UI.Core.CoreWindow" ||
               c == "XamlExplorerHostIslandWindow" || c == "TopLevelWindowForOverflowXamlIsland" ||
               c == "NotifyIconOverflowWindow" || c == "Progman" || c == "WorkerW";
    }

    public static List<IntPtr> FindPiPWindows()
    {
        var found = new List<IntPtr>();
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var titleSb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, titleSb, 256);
            string title = titleSb.ToString();

            var classSb = new System.Text.StringBuilder(256);
            GetClassName(hWnd, classSb, 256);
            string className = classSb.ToString();

            if (className.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) &&
                title.Equals("Picture in Picture", StringComparison.OrdinalIgnoreCase) &&
                IsSmallTopmostWindow(hWnd))
            {
                found.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static bool IsWindowOpen(string titleQuery)
    {
        if (string.IsNullOrWhiteSpace(titleQuery)) return false;
        bool found = false;
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();
            if (!string.IsNullOrWhiteSpace(title) && title.Contains(titleQuery, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static System.Threading.Timer? _hoverTimer;
    private static bool _hoverTrackingActive = false;
    private static DateTime _pausedUntil = DateTime.MinValue;
    private static readonly object _lock = new object();

    public static bool IsPaused => DateTime.UtcNow < _pausedUntil;

    public static (bool success, string message) ToggleClickThrough(int hoverAlpha = 0)
    {
        lock (_lock)
        {
            _hoverTrackingActive = !_hoverTrackingActive;
            _pausedUntil = DateTime.MinValue;

            if (_hoverTrackingActive)
            {
                _windowStates.Clear();
                _cachedPiPWindows.Clear();
                _lastEnumTime = DateTime.MinValue;
                _hoverTimer ??= new System.Threading.Timer(_ => CheckHoverState(hoverAlpha), null, 0, 50);
                return (true, "PiP Cursor Tracking started: Hover -> Hide & Pass Clicks, Off-Hover -> Show Video.");
            }
            else
            {
                RestoreAllWindowsAndShutdown();
                return (true, "PiP Cursor Tracking stopped (Restored fully opaque).");
            }
        }
    }

    public static (bool success, string message) PauseClickThrough(int seconds = 10)
    {
        if (seconds <= 0) seconds = 10;
        lock (_lock)
        {
            _pausedUntil = DateTime.UtcNow.AddSeconds(seconds);
            _cachedPiPWindows.Clear();

            try
            {
                var windows = FindPiPWindows();
                foreach (var hWnd in windows)
                {
                    if (!IsWindow(hWnd)) continue;
                    _windowStates[hWnd] = PipWindowState.OpaqueNormal;
                    int style = GetWindowLong(hWnd, GWL_EXSTYLE);
                    int newStyle = style & ~WS_EX_TRANSPARENT;
                    SetWindowLong(hWnd, GWL_EXSTYLE, newStyle);
                    SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);
                }
            }
            catch { }

            StartHoverTracking();
        }
        return (true, $"PiP Click-Through paused for {seconds} seconds.");
    }

    public static (bool success, string message) ResumeClickThrough()
    {
        lock (_lock)
        {
            _pausedUntil = DateTime.MinValue;
        }
        return (true, "PiP Click-Through resumed.");
    }

    public static bool HasActivePiPWindow() => FindPiPWindows().Count > 0;

    public static void StartHoverTracking(int hoverAlpha = 0)
    {
        lock (_lock)
        {
            if (!_hoverTrackingActive)
            {
                _hoverTrackingActive = true;
                _windowStates.Clear();
                _cachedPiPWindows.Clear();
                _lastEnumTime = DateTime.MinValue;
                _hoverTimer ??= new System.Threading.Timer(_ => CheckHoverState(hoverAlpha), null, 0, 50);
            }
        }
    }

    public static void RestoreAllWindowsAndShutdown()
    {
        lock (_lock)
        {
            _hoverTrackingActive = false;
            _pausedUntil = DateTime.MinValue;
            _hoverTimer?.Dispose();
            _hoverTimer = null;
            _windowStates.Clear();
            _cachedPiPWindows.Clear();

            try
            {
                var windows = FindPiPWindows();
                foreach (var hWnd in windows)
                {
                    if (!IsWindow(hWnd)) continue;
                    int style = GetWindowLong(hWnd, GWL_EXSTYLE);
                    int newStyle = style & ~WS_EX_TRANSPARENT;
                    SetWindowLong(hWnd, GWL_EXSTYLE, newStyle);
                    SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);
                }
            }
            catch { }
        }
    }

    private static void CheckHoverState(int hoverAlpha)
    {
        try
        {
            if (!_hoverTrackingActive) return;

            var windows = GetOrRefreshPiPWindows();
            if (windows.Count == 0) return;

            if (IsPaused || IsShellForeground())
            {
                foreach (var hWnd in windows)
                {
                    if (!IsWindow(hWnd)) continue;
                    if (_windowStates.TryGetValue(hWnd, out var state) && state == PipWindowState.HoverHidden)
                    {
                        _windowStates[hWnd] = PipWindowState.OpaqueNormal;
                        int currentStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                        SetWindowLong(hWnd, GWL_EXSTYLE, currentStyle & ~WS_EX_TRANSPARENT);
                        SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);
                    }
                }
                return;
            }

            if (!GetCursorPos(out POINT pt)) return;

            foreach (var hWnd in windows)
            {
                if (!GetWindowRect(hWnd, out RECT rect)) continue;

                bool isMouseOver = pt.X >= rect.Left && pt.X <= rect.Right &&
                                   pt.Y >= rect.Top && pt.Y <= rect.Bottom;

                PipWindowState targetState = isMouseOver ? PipWindowState.HoverHidden : PipWindowState.OpaqueNormal;

                if (_windowStates.TryGetValue(hWnd, out var currentState) && currentState == targetState)
                {
                    continue;
                }

                _windowStates[hWnd] = targetState;
                int currentStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

                switch (targetState)
                {
                    case PipWindowState.HoverHidden:
                        SetWindowLong(hWnd, GWL_EXSTYLE, currentStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                        byte targetAlpha = (byte)Math.Clamp(hoverAlpha, 0, 255);
                        SetLayeredWindowAttributes(hWnd, 0, targetAlpha, LWA_ALPHA);
                        break;

                    case PipWindowState.OpaqueNormal:
                        SetWindowLong(hWnd, GWL_EXSTYLE, (currentStyle & ~WS_EX_TRANSPARENT) | WS_EX_LAYERED);
                        SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);
                        break;
                }
            }
        }
        catch { }
    }
}

#endregion

