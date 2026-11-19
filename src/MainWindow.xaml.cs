using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;

namespace xTaskForge;

public sealed class ScriptTask : INotifyPropertyChanged
{
    private string _status = "Idle";
    private bool _isRunning;
    private int? _pid;
    private bool _runHidden;
    private bool _runAsAdmin;
    private bool _silentElevate;
    private bool _autoStart;
    private bool _loop;
    private int _loopDelayMs = 1000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Task";
    public string ScriptPath { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string LastResult { get; set; } = "Idle";
    public string Launcher { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool RunHidden
    {
        get => _runHidden;
        set
        {
            if (_runHidden != value)
            {
                _runHidden = value;
                Refresh();
            }
        }
    }
    public bool RunAsAdmin
    {
        get => _runAsAdmin;
        set
        {
            if (_runAsAdmin != value)
            {
                _runAsAdmin = value;
                Refresh();
            }
        }
    }
    public bool SilentElevate
    {
        get => _silentElevate;
        set
        {
            if (_silentElevate != value)
            {
                _silentElevate = value;
                Refresh();
            }
        }
    }
    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (_autoStart != value)
            {
                _autoStart = value;
                Refresh();
            }
        }
    }
    public bool Loop
    {
        get => _loop;
        set
        {
            if (_loop != value)
            {
                _loop = value;
                Refresh();
            }
        }
    }
    public int LoopDelayMs
    {
        get => _loopDelayMs;
        set
        {
            if (_loopDelayMs != value)
            {
                _loopDelayMs = value;
                Refresh();
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayAdmin => SilentElevate ? "⚡ Silent" : (RunAsAdmin ? "🛡️ Yes" : "No");

    public string ScriptType
    {
        get
        {
            var ext = System.IO.Path.GetExtension(ScriptPath).ToLowerInvariant();
            return ext switch
            {
                ".ps1" => "PowerShell",
                ".bat" or ".cmd" => "Batch",
                _ => "Executable"
            };
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                Refresh();
            }
        }
    }

    public int? Pid
    {
        get => _pid;
        set
        {
            if (_pid != value)
            {
                _pid = value;
                Refresh();
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                Refresh();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScriptPath)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScriptType)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunAsAdmin)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayAdmin)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
    }
}

public sealed class FileRule : INotifyPropertyChanged
{
    private string _path = "";
    private string _kind = "File";
    private bool _isSuperHidden = true;
    private bool _enforce = true;
    private string _status = "Normal";

    public string Path
    {
        get => _path;
        set { _path = value; OnPropertyChanged(nameof(Path)); }
    }

    public string Kind
    {
        get => _kind;
        set { _kind = value; OnPropertyChanged(nameof(Kind)); }
    }

    public bool IsSuperHidden
    {
        get => _isSuperHidden;
        set
        {
            _isSuperHidden = value;
            OnPropertyChanged(nameof(IsSuperHidden));
            OnPropertyChanged(nameof(ProtectionLabel));
        }
    }

    public bool Enforce
    {
        get => _enforce;
        set
        {
            _enforce = value;
            OnPropertyChanged(nameof(Enforce));
            OnPropertyChanged(nameof(EnforceLabel));
        }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); }
    }

    public string ProtectionLabel => IsSuperHidden ? "Super-Hidden" : "Hidden";
    public string EnforceLabel => Enforce ? "Yes (Watched)" : "Paused";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    public void UpdateStatusFromDisk()
    {
        if (!File.Exists(Path) && !Directory.Exists(Path))
        {
            Status = "Missing";
            return;
        }

        try
        {
            var attrs = File.GetAttributes(Path);
            bool isHidden = (attrs & FileAttributes.Hidden) != 0;
            bool isSystem = (attrs & FileAttributes.System) != 0;

            if (isHidden && isSystem)
                Status = "Super-Hidden (+H +S)";
            else if (isHidden)
                Status = "Hidden (+H)";
            else
                Status = "Visible (Normal)";
        }
        catch
        {
            Status = "Access Error";
        }
    }
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }
}

public sealed class AppSettings
{
    public List<ScriptTask> Tasks { get; set; } = [];
    public List<FileRule> Rules { get; set; } = [];
    public WindowPlacement? Placement { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool StartInTray { get; set; } = true;
    public bool EfficiencyMode { get; set; } = true;
    public bool DefaultTasksInitialized { get; set; }
}

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly ObservableCollection<ScriptTask> _tasks = [];
    private readonly ObservableCollection<FileRule> _rules = [];
    private readonly ObservableCollection<TrayMenuItemDefinition> _customMenuItems = [];
    private readonly Dictionary<string, Process> _runningProcesses = [];
    private readonly Dictionary<string, CancellationTokenSource> _taskCts = [];
    private readonly Dictionary<string, FileSystemWatcher> _ruleWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pendingRuleChecks = new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherTimer _enforceTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _savePlacementTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private readonly string _appDataDir;
    private readonly string _settingsPath;
    private readonly string _trayMenuJsonPath;

    private readonly NotifyIcon _trayIcon = new();
    private readonly NativeAudioSleepMonitor _audioMonitor = new();
    private FileSystemWatcher? _trayJsonWatcher;
    private CommandBroker? _commandBroker;
    private LocalIpcServer? _ipcServer;
    private TrayConfiguration _trayConfig = new();
    private string _displayedIconRef = "default";

    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "xTaskForge";
    private bool _reallyClosing;
    private bool _isLoaded;
    private bool _settingsFileOpenForExternalEdit;
    private AppSettings _settings = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static readonly string ScriptsDir = @"E:\.Dev-Vault\Powershell\xTaskForge";

    public static MainWindow? Instance { get; private set; }
    private static readonly ConcurrentDictionary<string, string> _stateVariables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (bool Result, DateTime Expiry)> _scriptConditionCache = new(StringComparer.OrdinalIgnoreCase);

    public bool ShouldStartHidden { get; }

    public MainWindow(bool forceStartInTray = false)
    {
        Instance = this;
        InitializeComponent();

        _appDataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "xTaskForge");
        Directory.CreateDirectory(_appDataDir);

        _settingsPath = System.IO.Path.Combine(_appDataDir, "settings.json");
        _trayMenuJsonPath = System.IO.Path.Combine(_appDataDir, "tray_menu.json");

        TasksList.ItemsSource = _tasks;
        RulesList.ItemsSource = _rules;
        CustomTrayMenuItemsList.ItemsSource = _customMenuItems;

        // Clean shutdown handler for ProcessExit
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { PipProvider.RestoreAllWindowsAndShutdown(); } catch { }
        };

        // Wire native background service events
        _audioMonitor.LogMessage += msg => Dispatcher.Invoke(() => Log(msg));
        _audioMonitor.AudioStateChanged += (isPlaying, peak) => Dispatcher.Invoke(() =>
        {
            string targetIcon = isPlaying ? "icon1.ico" : (string.IsNullOrWhiteSpace(_trayConfig.CurrentIcon) ? "icon.ico" : _trayConfig.CurrentIcon);
            ApplyTrayIcon(targetIcon);
        });
        NativeAudioDeviceManager.LogMessage += msg => Dispatcher.Invoke(() => Log(msg));

        LoadSettings();
        bool regStartup = IsStartupEnabled(out bool regHasTrayArg);
        if (regStartup && !_settings.StartWithWindows)
        {
            _settings.StartWithWindows = true;
            if (regHasTrayArg)
                _settings.StartInTray = true;
        }
        if (_settings.StartWithWindows)
        {
            SetStartup(true, _settings.StartInTray);
        }

        EnsureDefaultTasks();
        EnsureDefaultTrayMenuJson();
        LoadTrayMenuJson();

        foreach (var task in _tasks.Where(t => t.AutoStart).ToList())
        {
            StartTask(task);
        }

        // Start the audio monitor only after the tray icon and menu are initialized.

        ShouldStartHidden = forceStartInTray || _settings.StartInTray;
        RestoreWindowPlacement(_settings.Placement);


        _savePlacementTimer.Tick += (_, _) =>
        {
            _savePlacementTimer.Stop();
            if (_isLoaded) SaveSettings();
        };

        _enforceTimer.Tick += (_, _) =>
        {
            EnforceAllRules(report: false);
            SyncRuleWatchers();
        };
        _enforceTimer.Start();

        ConfigureTray();
        _audioMonitor.Start();
        StartIpcServer();
        SetupTrayJsonWatcher();
        EnforceAllRules(report: false);
        SyncRuleWatchers();
        RefreshCounters();

        StartWithWindowsCheckbox.IsChecked = _settings.StartWithWindows;
        StartInTrayCheckbox.IsChecked = _settings.StartInTray;
        EfficiencyModeCheckbox.IsChecked = _settings.EfficiencyMode;
        EfficiencyModeManager.Apply(_settings.EfficiencyMode);

        Loaded += (_, _) =>
        {
            _isLoaded = true;
            if (_settings.Placement == null)
                SaveSettings();

        };

        LocationChanged += (_, _) =>
        {
            if (_isLoaded && WindowState == WindowState.Normal)
            {
                _savePlacementTimer.Stop();
                _savePlacementTimer.Start();
            }
        };

        SizeChanged += (_, _) =>
        {
            if (_isLoaded && WindowState == WindowState.Normal)
            {
                _savePlacementTimer.Stop();
                _savePlacementTimer.Start();
            }
        };

        Deactivated += (_, _) => { if (_isLoaded) SaveSettings(); };

        Log("xTaskForge started. Silent task automation active (no notifications).");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar();
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            int useDarkMode = 1;
            if (DwmSetWindowAttribute(helper.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(helper.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
            }
        }
        catch { }
    }

    private void RestoreWindowPlacement(WindowPlacement? placement)
    {
        var workArea = SystemParameters.WorkArea;
        double defaultWidth = Math.Min(1020, Math.Max(MinWidth, workArea.Width - 30));
        double defaultHeight = Math.Min(680, Math.Max(MinHeight, workArea.Height - 30));

        if (placement == null)
        {
            Width = defaultWidth;
            Height = defaultHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        double targetWidth = Math.Clamp(placement.Width, MinWidth, workArea.Width);
        double targetHeight = Math.Clamp(placement.Height, MinHeight, workArea.Height);

        Width = targetWidth;
        Height = targetHeight;

        bool isVisibleOnScreen = false;
        foreach (var screen in Screen.AllScreens)
        {
            var area = screen.WorkingArea;
            if (placement.Left + targetWidth > area.Left && placement.Left < area.Right &&
                placement.Top + targetHeight > area.Top && placement.Top < area.Bottom)
            {
                isVisibleOnScreen = true;
                break;
            }
        }

        if (isVisibleOnScreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Max(SystemParameters.VirtualScreenLeft,
                Math.Min(placement.Left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - targetWidth));
            Top = Math.Max(SystemParameters.VirtualScreenTop,
                Math.Min(placement.Top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - targetHeight));
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (placement.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private WindowPlacement CaptureCurrentPlacement()
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        double defaultLeft = Math.Max(0, (SystemParameters.WorkArea.Width - width) / 2);
        double defaultTop = Math.Max(0, (SystemParameters.WorkArea.Height - height) / 2);

        double currentLeft = !double.IsNaN(Left) && !double.IsInfinity(Left) ? Left : defaultLeft;
        double currentTop = !double.IsNaN(Top) && !double.IsInfinity(Top) ? Top : defaultTop;

        var rb = GetSafeRestoreBounds();
        return new WindowPlacement
        {
            Left = !rb.IsEmpty && !double.IsNaN(rb.Left) && !double.IsInfinity(rb.Left) ? rb.Left : currentLeft,
            Top = !rb.IsEmpty && !double.IsNaN(rb.Top) && !double.IsInfinity(rb.Top) ? rb.Top : currentTop,
            Width = !rb.IsEmpty && rb.Width > 0 ? rb.Width : width,
            Height = !rb.IsEmpty && rb.Height > 0 ? rb.Height : height,
            IsMaximized = WindowState == WindowState.Maximized
        };
    }

    private Rect GetSafeRestoreBounds()
    {
        try { return RestoreBounds; }
        catch { return Rect.Empty; }
    }

    #region Tray Management & Context Menu

    private void ConfigureTray()
    {
        _trayIcon.Text = string.IsNullOrWhiteSpace(_trayConfig.Tooltip) ? "xTaskForge" : _trayConfig.Tooltip;
        ApplyTrayIcon(_trayConfig.CurrentIcon);

        RebuildTrayContextMenu();

        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(HandleTrayDoubleClick);
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left && (string.IsNullOrWhiteSpace(_trayConfig.DoubleClickCommand) || _trayConfig.DoubleClickCommand.Equals("native:open", StringComparison.OrdinalIgnoreCase)))
            {
                Dispatcher.Invoke(() =>
                {
                    if (IsVisible && WindowState != WindowState.Minimized)
                        Hide();
                    else
                        ShowMainWindow();
                });
            }
        };
        _trayIcon.Visible = true;
    }

    private void HandleTrayDoubleClick()
    {
        if (!string.IsNullOrWhiteSpace(_trayConfig.DoubleClickCommand) &&
            !_trayConfig.DoubleClickCommand.Equals("native:open", StringComparison.OrdinalIgnoreCase) &&
            !_trayConfig.DoubleClickCommand.Equals("native:show", StringComparison.OrdinalIgnoreCase) &&
            !_trayConfig.DoubleClickCommand.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            Log($"[Tray Double-Click] Executing command: {_trayConfig.DoubleClickCommand}");
            ExecuteCommand(_trayConfig.DoubleClickCommand, _trayConfig.DoubleClickArguments, runAsAdmin: _trayConfig.DoubleClickRunAsAdmin);
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void RebuildTrayContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = System.Drawing.Color.FromArgb(22, 25, 32),
            ForeColor = System.Drawing.Color.FromArgb(240, 242, 246),
            ShowCheckMargin = true,
            ShowImageMargin = true
        };

        menu.Opening += (_, _) => RefreshDynamicMenuItems(menu.Items);

        // 1. DYNAMIC CUSTOM ITEMS (From scripts or tray_menu.json)
        if (_trayConfig.Items.Count > 0)
        {
            PopulateMenuItems(menu.Items, _trayConfig.Items);
            menu.Items.Add(new ToolStripSeparator());
        }

        // 2. SYSTEM DEFAULT ITEMS (Always present at bottom)
        var openItem = new ToolStripMenuItem("Open xTaskForge", null, (_, _) => Dispatcher.Invoke(ShowMainWindow))
        {
            Font = new System.Drawing.Font(System.Windows.Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon.ContextMenuStrip = menu;

        // Update UI list on Tray & API tab
        _customMenuItems.Clear();
        var flattened = TrayConfiguration.FlattenItems(_trayConfig.Items);
        foreach (var item in flattened)
            _customMenuItems.Add(item);

        DoubleClickActionText.Text = string.IsNullOrWhiteSpace(_trayConfig.DoubleClickCommand) || _trayConfig.DoubleClickCommand.Equals("native:open", StringComparison.OrdinalIgnoreCase)
            ? "Open Window (Default)"
            : (_trayConfig.DoubleClickCommand.StartsWith("native:") ? _trayConfig.DoubleClickCommand : System.IO.Path.GetFileName(_trayConfig.DoubleClickCommand));
    }

    public static void SetStateVariable(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _stateVariables[key.Trim()] = value ?? "true";
        Instance?.Dispatcher.InvokeAsync(() => Instance.RebuildTrayContextMenu());
    }

    public static string? GetStateVariable(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return _stateVariables.TryGetValue(key.Trim(), out var val) ? val : null;
    }

    public static bool ToggleStateVariable(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        string k = key.Trim();
        bool current = IsStateSet(k);
        bool next = !current;
        _stateVariables[k] = next ? "true" : "false";
        Instance?.Dispatcher.InvokeAsync(() => Instance.RebuildTrayContextMenu());
        return next;
    }

    public static void ClearStateVariable(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _stateVariables.TryRemove(key.Trim(), out _);
        Instance?.Dispatcher.InvokeAsync(() => Instance.RebuildTrayContextMenu());
    }

    private static bool IsProcessRunning(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        query = query.Trim();

        string procName = query;
        string? requiredArgs = null;

        int spaceIdx = query.IndexOf(' ');
        if (spaceIdx > 0)
        {
            procName = query[..spaceIdx].Trim();
            requiredArgs = query[(spaceIdx + 1)..].Trim();
        }

        if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            procName = procName[..^4];
        }

        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(procName);
            if (processes.Length == 0) return false;

            if (string.IsNullOrWhiteSpace(requiredArgs))
            {
                return true;
            }

            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE Name = '{procName}.exe' OR Name = '{procName}'");
            using var results = searcher.Get();

            foreach (System.Management.ManagementObject obj in results)
            {
                string? cmdLine = obj["CommandLine"]?.ToString();
                if (!string.IsNullOrWhiteSpace(cmdLine) &&
                    cmdLine.Contains(requiredArgs, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(procName);
                return processes.Length > 0;
            }
            catch { }
        }

        return false;
    }

    private static bool IsTaskRunning(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || Instance == null) return false;
        query = query.Trim();

        lock (Instance._runningProcesses)
        {
            if (Instance._runningProcesses.ContainsKey(query)) return true;
        }

        return Instance._tasks.Any(t =>
            t.IsRunning &&
            (string.Equals(t.Id, query, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(t.Name, query, StringComparison.OrdinalIgnoreCase) ||
             t.ScriptPath.EndsWith(query, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsStateSet(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (_stateVariables.TryGetValue(key.Trim(), out var val))
        {
            if (string.IsNullOrWhiteSpace(val)) return false;
            val = val.Trim().ToLowerInvariant();
            return val is not ("false" or "0" or "off" or "no");
        }
        return false;
    }

    private static bool EvaluateScriptCondition(string scriptQuery)
    {
        if (string.IsNullOrWhiteSpace(scriptQuery)) return false;
        string key = scriptQuery.Trim();

        if (_scriptConditionCache.TryGetValue(key, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return cached.Result;
        }

        bool result = false;
        try
        {
            string scriptPath = key;
            string args = "";
            int spaceIdx = key.IndexOf(' ');
            if (spaceIdx > 0)
            {
                scriptPath = key[..spaceIdx].Trim();
                args = key[(spaceIdx + 1)..].Trim();
            }

            string resolved = ResolveFilePath(scriptPath);
            if (!File.Exists(resolved))
            {
                resolved = System.IO.Path.Combine(ScriptsDir, scriptPath);
            }

            if (File.Exists(resolved))
            {
                string ext = System.IO.Path.GetExtension(resolved).ToLowerInvariant();
                string fileName = ext switch
                {
                    ".ps1" => "powershell.exe",
                    ".bat" or ".cmd" => Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    _ => resolved
                };

                string procArgs = ext switch
                {
                    ".ps1" => $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{resolved}\" {args}".Trim(),
                    ".bat" or ".cmd" => $"/d /s /c \"\"{resolved}\" {args}\"".Trim(),
                    _ => args
                };

                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = procArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };

                proc.Start();
                string output = proc.StandardOutput.ReadToEnd();
                if (proc.WaitForExit(2000))
                {
                    result = proc.ExitCode == 0;
                    if (result && !string.IsNullOrWhiteSpace(output))
                    {
                        string trimmed = output.Trim().ToLowerInvariant();
                        if (trimmed is "false" or "0" or "error" or "failed")
                            result = false;
                    }
                }
                else
                {
                    try { proc.Kill(true); } catch { }
                }
            }
        }
        catch { }

        _scriptConditionCache[key] = (result, DateTime.UtcNow.AddSeconds(2));
        return result;
    }

    private static bool EvaluateCondition(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        string[] orParts = condition.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var orPart in orParts)
        {
            string[] andParts = orPart.Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool andResult = true;
            foreach (var andPart in andParts)
            {
                if (!EvaluateConditionSingle(andPart))
                {
                    andResult = false;
                    break;
                }
            }
            if (andResult) return true;
        }
        return false;
    }

    private static bool EvaluateConditionSingle(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return true;
        expr = expr.Trim();
        bool negate = expr.StartsWith('!');
        if (negate) expr = expr[1..].Trim();

        bool result;

        int colonIdx = expr.IndexOf(':');
        string prefix = colonIdx > 0 ? expr[..colonIdx].ToLowerInvariant().Trim() : expr.ToLowerInvariant().Trim();
        string target = colonIdx > 0 ? expr[(colonIdx + 1)..].Trim() : "";

        switch (prefix)
        {
            case "pip.active":
                result = PipProvider.HasActivePiPWindow();
                break;
            case "pip.paused":
                result = PipProvider.IsPaused;
                break;
            case "audio.playing":
            case "audio.active":
                result = Instance?._audioMonitor?.CurrentPeak > 0.0001f;
                break;

            case "process":
            case "proc":
            case "exe":
                result = IsProcessRunning(target);
                break;

            case "window":
            case "title":
                result = PipProvider.IsWindowOpen(target);
                break;

            case "task":
            case "task.running":
                result = IsTaskRunning(target);
                break;

            case "state":
            case "var":
            case "flag":
                result = IsStateSet(target);
                break;

            case "script":
                result = EvaluateScriptCondition(target);
                break;

            default:
                if (expr.Equals("pip.active", StringComparison.OrdinalIgnoreCase))
                    result = PipProvider.HasActivePiPWindow();
                else if (expr.Equals("pip.paused", StringComparison.OrdinalIgnoreCase))
                    result = PipProvider.IsPaused;
                else if (expr.StartsWith("process:", StringComparison.OrdinalIgnoreCase))
                    result = IsProcessRunning(expr[8..]);
                else
                    result = true;
                break;
        }

        return negate ? !result : result;
    }

    private static bool IsToggled(TrayMenuItemDefinition d) =>
        !string.IsNullOrWhiteSpace(d.ToggledWhen) && EvaluateCondition(d.ToggledWhen);

    private static void RefreshDynamicMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem it in items)
        {
            if (it.Tag is TrayMenuItemDefinition d)
            {
                it.Visible = EvaluateCondition(d.VisibleWhen);
                it.Text = IsToggled(d) && !string.IsNullOrWhiteSpace(d.ToggledTitle) ? d.ToggledTitle : d.Title;
            }
            if (it is ToolStripMenuItem m && m.HasDropDownItems)
                RefreshDynamicMenuItems(m.DropDownItems);
        }
    }

    private void PopulateMenuItems(ToolStripItemCollection targetCollection, IEnumerable<TrayMenuItemDefinition> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                targetCollection.Add(new ToolStripSeparator());
                continue;
            }

            var captured = item;
            var menuItem = new ToolStripMenuItem(captured.Title)
            {
                Checked = captured.Checked,
                Tag = captured,
                ToolTipText = captured.SilentElevate ? "Runs Elevated Silently (Task Scheduler)" : (captured.RunAsAdmin ? "Runs as Administrator (Elevated)" : "")
            };
            if (!EvaluateCondition(captured.VisibleWhen)) menuItem.Visible = false;

            if (!string.IsNullOrWhiteSpace(captured.Icon))
            {
                menuItem.Image = LoadMenuItemImage(captured.Icon);
            }

            if (captured.HasItems)
            {
                if (menuItem.DropDown is ToolStripDropDownMenu dropDownMenu)
                {
                    dropDownMenu.Renderer = new DarkMenuRenderer();
                    dropDownMenu.BackColor = System.Drawing.Color.FromArgb(22, 25, 32);
                    dropDownMenu.ForeColor = System.Drawing.Color.FromArgb(240, 242, 246);
                    dropDownMenu.ShowCheckMargin = true;
                    dropDownMenu.ShowImageMargin = true;
                }
                PopulateMenuItems(menuItem.DropDownItems, captured.Items!);
            }

            menuItem.Click += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (captured.Command.StartsWith("native:toggle_audio_device", StringComparison.OrdinalIgnoreCase))
                    {
                        captured.Checked = !captured.Checked;
                        SaveTrayMenuJson();
                        RebuildTrayContextMenu();
                        Log($"[Audio] Toggled device '{captured.Title}' cycling state: {(captured.Checked ? "Enabled" : "Disabled")}");
                        SetStatus($"Audio device {(captured.Checked ? "enabled" : "disabled")}: {captured.Title}");
                        return;
                    }

                    bool isToggledState = IsToggled(captured);
                    bool hasToggledCmd = !string.IsNullOrWhiteSpace(captured.ToggledCommand);
                    string runCmd = isToggledState && hasToggledCmd ? captured.ToggledCommand! : captured.Command;
                    string runArgs = isToggledState && hasToggledCmd ? (captured.ToggledArguments ?? "") : captured.Arguments;
                    string effectiveTitle = isToggledState && !string.IsNullOrWhiteSpace(captured.ToggledTitle) ? captured.ToggledTitle : captured.Title;

                    if (!string.IsNullOrWhiteSpace(runCmd))
                    {
                        string adminBadge = captured.SilentElevate ? " [Silent Elevated]" : (captured.RunAsAdmin ? " [Elevated]" : "");
                        Log($"[Tray Menu] Clicked '{effectiveTitle}'{adminBadge} -> {runCmd} {runArgs}");
                        ExecuteCommand(runCmd, runArgs, captured.WorkingDirectory, captured.RunHidden, captured.RunAsAdmin, captured.SilentElevate, $"tray_{captured.Id}");
                    }
                });
            };

            targetCollection.Add(menuItem);
        }
    }

    private static TrayMenuItemDefinition? FindMenuItemByIdOrTitle(IEnumerable<TrayMenuItemDefinition> items, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        foreach (var item in items)
        {
            if (string.Equals(item.Id, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Title, query, StringComparison.OrdinalIgnoreCase) ||
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Command, query, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
            if (item.HasItems)
            {
                var childMatch = FindMenuItemByIdOrTitle(item.Items!, query);
                if (childMatch != null) return childMatch;
            }
        }
        return null;
    }

    private static System.Drawing.Image? LoadMenuItemImage(string? iconRef)
    {
        if (string.IsNullOrWhiteSpace(iconRef) || iconRef.Equals("default", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var (rawPath, iconIndex) = ParseIconRef(iconRef);
            string resolvedPath = ResolveFilePath(rawPath);
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                using var icon = ExtractIconFromFile(resolvedPath, iconIndex) ?? new System.Drawing.Icon(resolvedPath);
                if (icon != null)
                {
                    using var bmp = icon.ToBitmap();
                    return new System.Drawing.Bitmap(bmp, new System.Drawing.Size(16, 16));
                }
            }
        }
        catch { }
        return null;
    }

    #region Win32 Native Icon APIs & PickIconDialog

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ExtractIconEx(string sFile, int iIndex, out IntPtr piLargeVersion, out IntPtr piSmallVersion, int amountIcons);

    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static (string path, int index) ParseIconRef(string? iconRef)
    {
        if (string.IsNullOrWhiteSpace(iconRef))
            return ("", 0);

        iconRef = iconRef.Trim().Trim('"');

        // Check for trailing comma or semicolon delimiter: "file.dll,12" or "file.dll;12"
        int sepIdx = iconRef.LastIndexOfAny(new[] { ',', ';' });
        if (sepIdx > 0)
        {
            string p = iconRef[..sepIdx].Trim().Trim('"');
            string idxStr = iconRef[(sepIdx + 1)..].Trim();
            if (int.TryParse(idxStr, out int parsedIdx))
            {
                return (p, parsedIdx);
            }
        }

        // Check for colon delimiter if not part of a drive letter (e.g. "imageres.dll:12")
        int colonIdx = iconRef.LastIndexOf(':');
        if (colonIdx > 1 && int.TryParse(iconRef[(colonIdx + 1)..].Trim(), out int cIdx))
        {
            return (iconRef[..colonIdx].Trim().Trim('"'), cIdx);
        }

        return (iconRef, 0);
    }

    public static string ResolveFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return "";

        string expanded = Environment.ExpandEnvironmentVariables(filePath.Trim().Trim('"'));

        // Reject remote or UNC paths so local icon resolution never makes network requests
        if (expanded.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ||
            expanded.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
            expanded.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            expanded.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
            expanded.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        // 1. Direct path exists
        if (File.Exists(expanded)) return expanded;

        // 2. Windows System32 directory (e.g. imageres.dll, shell32.dll, ddores.dll)
        string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string system32File = System.IO.Path.Combine(sysDir, expanded);
        if (File.Exists(system32File)) return system32File;

        // Also check if user passed "imageres" without ".dll"
        if (!expanded.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            string sysWithDll = System.IO.Path.Combine(sysDir, expanded + ".dll");
            if (File.Exists(sysWithDll)) return sysWithDll;
        }

        // 3. Scripts directory (E:\.Dev-Vault\Powershell\xTaskForge) and AppData icons/files
        string customScripts = System.IO.Path.Combine(ScriptsDir, expanded);
        if (File.Exists(customScripts)) return customScripts;

        string appDataRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "xTaskForge");

        string appDataIcons = System.IO.Path.Combine(appDataRoot, "icons", expanded);
        if (File.Exists(appDataIcons)) return appDataIcons;

        string appDataDirect = System.IO.Path.Combine(appDataRoot, expanded);
        if (File.Exists(appDataDirect)) return appDataDirect;

        // 4. App BaseDirectory
        string appDirFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, expanded);
        if (File.Exists(appDirFile)) return appDirFile;

        // 5. App BaseDirectory / icons
        string appIconsFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons", expanded);
        if (File.Exists(appIconsFile)) return appIconsFile;

        return expanded;
    }

    private static System.Drawing.Icon? ExtractIconFromFile(string file, int index)
    {
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(file);

            IntPtr hLarge, hSmall;
            int count = ExtractIconEx(expanded, index, out hLarge, out hSmall, 1);
            if (count > 0)
            {
                IntPtr chosen = hSmall != IntPtr.Zero ? hSmall : hLarge;
                var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(chosen).Clone();
                if (hLarge != IntPtr.Zero) DestroyIcon(hLarge);
                if (hSmall != IntPtr.Zero && hSmall != hLarge) DestroyIcon(hSmall);
                return icon;
            }

            // Fallback to PrivateExtractIconsW
            IntPtr[] phicon = new IntPtr[1];
            uint[] piconid = new uint[1];
            uint n = PrivateExtractIcons(expanded, index, 16, 16, phicon, piconid, 1, 0);
            if (n > 0 && phicon[0] != IntPtr.Zero)
            {
                var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(phicon[0]).Clone();
                DestroyIcon(phicon[0]);
                return icon;
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static bool ShowWindowsPickIconDialog(IntPtr hwndOwner, ref string iconPath, ref int iconIndex)
    {
        return IconPickerHelper.ShowWindowsPickIconDialog(hwndOwner, ref iconPath, ref iconIndex);
    }

    #endregion

    public void ApplyTrayIcon(string? iconRef)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(iconRef) || iconRef.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                _trayIcon.Icon = GetDefaultAppIcon();
                ActiveIconText.Text = "Default (icon.ico)";
                _displayedIconRef = "default";
                return;
            }

            var (rawPath, iconIndex) = ParseIconRef(iconRef);
            string resolvedPath = ResolveFilePath(rawPath);

            System.Drawing.Icon? loadedIcon = null;

            if (File.Exists(resolvedPath))
            {
                var ext = System.IO.Path.GetExtension(resolvedPath).ToLowerInvariant();
                if (ext is ".dll" or ".exe" or ".icl" or ".cpl" or ".ocx" or ".mun")
                {
                    loadedIcon = ExtractIconFromFile(resolvedPath, iconIndex);
                }
                else if (ext == ".ico")
                {
                    if (iconIndex > 0)
                    {
                        loadedIcon = ExtractIconFromFile(resolvedPath, iconIndex);
                    }
                    loadedIcon ??= new System.Drawing.Icon(resolvedPath);
                }
                else
                {
                    using var bmp = new Bitmap(resolvedPath);
                    var hIcon = bmp.GetHicon();
                    try
                    {
                        loadedIcon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
            else if (rawPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = System.IO.Path.GetFileName(rawPath);
                foreach (var uriStr in new[] { $"pack://application:,,,/{fileName}", $"pack://application:,,,/icons/{fileName}" })
                {
                    try
                    {
                        var streamInfo = System.Windows.Application.GetResourceStream(new Uri(uriStr, UriKind.Absolute));
                        if (streamInfo?.Stream != null)
                        {
                            loadedIcon = new System.Drawing.Icon(streamInfo.Stream);
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (loadedIcon != null)
            {
                _trayIcon.Icon = loadedIcon;
                string displayRef = (iconIndex > 0 || resolvedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || resolvedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    ? $"{System.IO.Path.GetFileName(resolvedPath)},{iconIndex}"
                    : System.IO.Path.GetFileName(resolvedPath);

                ActiveIconText.Text = displayRef;
                _displayedIconRef = iconRef;
                Log($"Tray icon updated to: {iconRef}");
                return;
            }

            Log($"Icon '{iconRef}' not found; falling back to default (icon.ico).");
            _trayIcon.Icon = GetDefaultAppIcon();
            ActiveIconText.Text = "Default (icon.ico)";
            _displayedIconRef = "default";
        }
        catch (Exception ex)
        {
            Log($"Could not apply icon '{iconRef}': {ex.Message}");
            _trayIcon.Icon = GetDefaultAppIcon();
            ActiveIconText.Text = "Default (icon.ico)";
            _displayedIconRef = "default";
        }
    }

    private static System.Drawing.Icon GetDefaultAppIcon()
    {
        try
        {
            // 1. Try local file in app directory or icons/
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localIcon = System.IO.Path.Combine(appDir, "icon.ico");
            if (File.Exists(localIcon))
                return new System.Drawing.Icon(localIcon);

            string localIconsDir = System.IO.Path.Combine(appDir, "icons", "icon.ico");
            if (File.Exists(localIconsDir))
                return new System.Drawing.Icon(localIconsDir);

            // 2. Try embedded resource from assembly
            var resourceUri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(resourceUri);
            if (streamInfo?.Stream != null)
                return new System.Drawing.Icon(streamInfo.Stream);

            var resourceUri2 = new Uri("pack://application:,,,/icons/icon.ico", UriKind.Absolute);
            var streamInfo2 = System.Windows.Application.GetResourceStream(resourceUri2);
            if (streamInfo2?.Stream != null)
                return new System.Drawing.Icon(streamInfo2.Stream);

            // 3. Extract icon embedded directly in the compiled .exe file
            var procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
            {
                var extracted = System.Drawing.Icon.ExtractAssociatedIcon(procPath);
                if (extracted != null)
                    return extracted;
            }
        }
        catch
        {
            // fallback to drawn badge
        }

        return CreateBrandIcon();
    }

    private static System.Drawing.Icon CreateBrandIcon()
    {
        try
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bgCol = System.Drawing.Color.FromArgb(49, 90, 165);

            using var brush = new SolidBrush(bgCol);
            using var path = new GraphicsPath();
            int radius = 8;
            path.AddArc(0, 0, radius, radius, 180, 90);
            path.AddArc(32 - radius, 0, radius, radius, 270, 90);
            path.AddArc(32 - radius, 32 - radius, radius, radius, 0, 90);
            path.AddArc(0, 32 - radius, radius, radius, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);

            // Glyphs: 'xT' badge
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 3.2f);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawLine(pen, 9, 9, 23, 23);
            g.DrawLine(pen, 23, 9, 9, 23);

            var hIcon = bmp.GetHicon();
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var background = new SolidBrush(System.Drawing.Color.FromArgb(22, 25, 32));
            e.Graphics.FillRectangle(background, e.AffectedBounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(48, 54, 66));
            e.Graphics.DrawLine(pen, 28, y, e.Item.Width - 6, y);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            var rect = new System.Drawing.Rectangle(2, 1, Math.Max(0, e.Item.Width - 4), Math.Max(0, e.Item.Height - 2));

            if (e.Item.Selected && e.Item.Enabled)
            {
                using var fillBrush = new SolidBrush(System.Drawing.Color.FromArgb(34, 40, 52));
                using var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(58, 68, 88));
                g.FillRectangle(fillBrush, rect);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }
            else
            {
                using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(22, 25, 32));
                g.FillRectangle(bgBrush, new System.Drawing.Rectangle(0, 0, e.Item.Width, e.Item.Height));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled
                ? System.Drawing.Color.FromArgb(110, 118, 132)
                : (e.Item.Selected ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(240, 242, 246));
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            bool enabled = e.Item?.Enabled ?? true;
            bool selected = e.Item?.Selected ?? false;
            e.ArrowColor = enabled
                ? (selected ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(200, 205, 215))
                : System.Drawing.Color.FromArgb(100, 105, 115);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem menuItem && menuItem.Checked)
            {
                var g = e.Graphics;
                var oldMode = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                int boxSize = 13;
                int x = e.ImageRectangle.X + Math.Max(0, (e.ImageRectangle.Width - boxSize) / 2);
                int y = e.ImageRectangle.Y + Math.Max(0, (e.ImageRectangle.Height - boxSize) / 2);
                var rect = new System.Drawing.Rectangle(x, y, boxSize, boxSize);

                using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(49, 90, 165));
                using var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(80, 140, 235));
                using var checkPen = new System.Drawing.Pen(System.Drawing.Color.White, 2f);

                g.FillRectangle(bgBrush, rect);
                g.DrawRectangle(borderPen, rect);

                g.DrawLines(checkPen, new[]
                {
                    new System.Drawing.Point(rect.X + 2, rect.Y + 6),
                    new System.Drawing.Point(rect.X + 5, rect.Y + 9),
                    new System.Drawing.Point(rect.X + 10, rect.Y + 3)
                });

                g.SmoothingMode = oldMode;
            }
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image != null)
            {
                var g = e.Graphics;
                g.DrawImage(e.Image, e.ImageRectangle);
            }
        }
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(34, 40, 52);
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(52, 62, 80);
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(48, 54, 66);
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(22, 25, 32);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(22, 25, 32);
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(22, 25, 32);
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(22, 25, 32);
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(48, 54, 66);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(48, 54, 66);
        public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(34, 40, 52);
        public override System.Drawing.Color MenuItemSelectedGradientEnd => System.Drawing.Color.FromArgb(34, 40, 52);
        public override System.Drawing.Color MenuItemPressedGradientBegin => System.Drawing.Color.FromArgb(28, 33, 44);
        public override System.Drawing.Color MenuItemPressedGradientEnd => System.Drawing.Color.FromArgb(28, 33, 44);
        public override System.Drawing.Color CheckBackground => System.Drawing.Color.FromArgb(30, 36, 48);
        public override System.Drawing.Color CheckSelectedBackground => System.Drawing.Color.FromArgb(34, 40, 52);
        public override System.Drawing.Color CheckPressedBackground => System.Drawing.Color.FromArgb(28, 33, 44);
    }

    #endregion

    #region Tray Menu JSON File Handling & Hot-Reload

    private void EnsureDefaultTrayMenuJson()
    {
        if (File.Exists(_trayMenuJsonPath)) return;

        Directory.CreateDirectory(ScriptsDir);

        var config = new TrayConfiguration
        {
            Tooltip = "xTaskForge - Automation & System Utility",
            CurrentIcon = "icon1.ico",
            DoubleClickCommand = "native:open",
            DoubleClickArguments = "",
            Items =
            [
                new TrayMenuItemDefinition
                {
                    Id = "output_devices_menu",
                    Title = "Output Devices",
                    Icon = "SndVol.exe,1",
                    Items =
                    [
                        new TrayMenuItemDefinition
                        {
                            Id = "346f0650670b40a89310e694d4f918c1",
                            Title = "Next audio device",
                            Command = "native:cycle_audio_device",
                            Arguments = ""
                        }
                    ]
                },
                new TrayMenuItemDefinition
                {
                    Id = "pip_pause",
                    Title = "Pause PiP Click-Through (10s)",
                    Command = "native:pip_pause",
                    Arguments = "10",
                    VisibleWhen = "pip.active",
                    ToggledWhen = "pip.paused",
                    ToggledTitle = "Unpause PiP Click-Through",
                    ToggledCommand = "native:pip_unpause"
                },
                new TrayMenuItemDefinition
                {
                    Id = "display_menu",
                    Title = "Display",
                    Icon = "imageres.dll,104",
                    Items =
                    [
                        new TrayMenuItemDefinition
                        {
                            Id = "toggle_hdr",
                            Title = "Toggle HDR",
                            Command = "native:toggle_hdr",
                            Arguments = ""
                        },
                        new TrayMenuItemDefinition
                        {
                            Id = "res_4k",
                            Title = "3840 x 2160",
                            Command = "native:res_4k",
                            Arguments = ""
                        },
                        new TrayMenuItemDefinition
                        {
                            Id = "res_1080",
                            Title = "1920 x 1080",
                            Command = "native:res_1080",
                            Arguments = ""
                        }
                    ]
                },
                new TrayMenuItemDefinition
                {
                    Id = "pip_clickthrough",
                    Title = "PiP Click-Through Overlay",
                    Command = "native:pip_clickthrough",
                    Icon = "imageres.dll,201",
                    Arguments = ""
                },
                new TrayMenuItemDefinition
                {
                    Id = "b6f2fac85f5b4a808bc7d270ebda63e0",
                    Title = "Terminals",
                    Icon = "cmd.exe,0",
                    Items =
                    [
                        new TrayMenuItemDefinition
                        {
                            Id = "8b4aadf0de9b4b37aac6a9083fb10d31",
                            Title = "Terminal",
                            Command = System.IO.Path.Combine(ScriptsDir, "Repo-Terminal.ps1"),
                            Icon = "cmd.exe,0"
                        },
                        new TrayMenuItemDefinition
                        {
                            Id = "ac27bdfefc56423e89bd07a8765effaa",
                            Title = "Terminal (admin)",
                            Command = System.IO.Path.Combine(ScriptsDir, "Repo-Terminal.ps1"),
                            Icon = "cmd.exe,0",
                            RunAsAdmin = true
                        }
                    ]
                }
            ]
        };

        try
        {
            File.WriteAllText(_trayMenuJsonPath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch { }
    }

    private void LoadTrayMenuJson()
    {
        try
        {
            if (!File.Exists(_trayMenuJsonPath)) return;
            var json = File.ReadAllText(_trayMenuJsonPath);
            var config = JsonSerializer.Deserialize<TrayConfiguration>(json, JsonOptions);
            if (config != null)
            {
                bool modified = false;

                // Clean up any duplicate items recursively
                DeduplicateMenuItems(config.Items);

                // Remove retired menu entries from older user configurations
                int originalItemCount = config.Items.Count;
                config.Items = config.Items
                    .Where(item => item.Id is not "sep_1" and not "audio_monitor" and not "amphub_watcher" and not "dotfiles" and not "enforce_dotfiles"
                        && item.Command is not "native:audio_monitor" and not "native:amphub_watcher" and not "native:dotfiles" and not "native:enforce_dotfiles")
                    .ToList();
                if (config.Items.Count != originalItemCount) modified = true;

                // Auto-migrate flat top-level display items (res_4k, res_1080, toggle_hdr) into Display submenu
                var displaySubmenu = config.Items.FirstOrDefault(i => i.Id == "display_menu" ||
                    string.Equals(i.Title, "Display", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.Title, "🖥️ Display", StringComparison.OrdinalIgnoreCase));

                var res4k = config.Items.FirstOrDefault(i => i.Id == "res_4k" || i.Command == "native:res_4k");
                var res1080 = config.Items.FirstOrDefault(i => i.Id == "res_1080" || i.Command == "native:res_1080");
                var toggleHdr = config.Items.FirstOrDefault(i => i.Id == "toggle_hdr" || i.Command == "native:toggle_hdr");

                if ((res4k != null || res1080 != null || toggleHdr != null) && displaySubmenu == null)
                {
                    displaySubmenu = new TrayMenuItemDefinition
                    {
                        Id = "display_menu",
                        Title = "🖥️ Display",
                        Items = []
                    };

                    if (res4k != null) { config.Items.Remove(res4k); displaySubmenu.Items.Add(res4k); }
                    if (res1080 != null) { config.Items.Remove(res1080); displaySubmenu.Items.Add(res1080); }
                    if (toggleHdr != null) { config.Items.Remove(toggleHdr); displaySubmenu.Items.Add(toggleHdr); }

                    config.Items.Insert(0, displaySubmenu);
                    modified = true;
                }

                _trayConfig = config;

                // Synchronize audio devices into 'Output Devices' secondary menu layer
                SyncAudioDevicesInMenu();

                // Auto-start PiP Click-Through Cursor Tracking if enabled in settings on startup
                var pipItem = FindMenuItemByIdOrTitle(_trayConfig.Items, "pip_clickthrough")
                           ?? _trayConfig.Items.FirstOrDefault(i => i.Command == "native:pip_clickthrough" || i.Command == "pip_clickthrough");
                if (pipItem != null && pipItem.Checked)
                {
                    PipProvider.StartHoverTracking();
                    Log("[PiP] Restored PiP Click-Through Cursor Tracking from settings (Active on startup).");
                }

                if (string.IsNullOrWhiteSpace(_trayConfig.DoubleClickCommand) ||
                    _trayConfig.DoubleClickCommand.Equals("native:cycle_audio_device", StringComparison.OrdinalIgnoreCase))
                {
                    _trayConfig.DoubleClickCommand = "native:open";
                    modified = true;
                }

                if (modified)
                    SaveTrayMenuJson();
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to read tray_menu.json: {ex.Message}");
        }
    }

    private void SaveTrayMenuJson()
    {
        try
        {
            bool wasWatching = _trayJsonWatcher?.EnableRaisingEvents == true;
            if (_trayJsonWatcher != null && wasWatching)
                _trayJsonWatcher.EnableRaisingEvents = false;

            try
            {
                File.WriteAllText(_trayMenuJsonPath, JsonSerializer.Serialize(_trayConfig, JsonOptions));
            }
            finally
            {
                if (_trayJsonWatcher != null && wasWatching)
                    _trayJsonWatcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to save tray_menu.json: {ex.Message}");
        }
    }

    private static void DeduplicateMenuItems(List<TrayMenuItemDefinition> items)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item.IsSeparator) continue;

            string key = !string.IsNullOrEmpty(item.Id) ? item.Id : item.Command;
            if (string.IsNullOrEmpty(key)) key = item.Title;

            if (!string.IsNullOrEmpty(key) && !seenKeys.Add(key))
            {
                items.RemoveAt(i);
            }
            else if (item.HasItems)
            {
                DeduplicateMenuItems(item.Items!);
            }
        }
    }

    private static bool EnsurePipPauseMenuItem(List<TrayMenuItemDefinition> items)
    {
        if (ContainsPipPause(items)) return UpgradePipPause(items);
        return InsertPipPauseAfterPipClickThrough(items);
    }

    private static bool UpgradePipPause(IEnumerable<TrayMenuItemDefinition> items)
    {
        bool changed = false;
        foreach (var item in items)
        {
            if ((item.Id == "pip_pause" || item.Command == "native:pip_pause") && string.IsNullOrWhiteSpace(item.VisibleWhen))
            {
                item.VisibleWhen = "pip.active";
                item.ToggledWhen = "pip.paused";
                item.ToggledTitle = "Unpause PiP Click-Through";
                item.ToggledCommand = "native:pip_unpause";
                changed = true;
            }
            if (item.Items != null && UpgradePipPause(item.Items)) changed = true;
        }
        return changed;
    }

    private static bool ContainsPipPause(IEnumerable<TrayMenuItemDefinition> items)
    {
        foreach (var item in items)
        {
            if (item.Id == "pip_pause" || item.Command == "native:pip_pause" || item.Command == "pip_pause")
                return true;
            if (item.Items != null && ContainsPipPause(item.Items))
                return true;
        }
        return false;
    }

    private static bool InsertPipPauseAfterPipClickThrough(List<TrayMenuItemDefinition> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Id == "pip_clickthrough" || item.Command == "native:pip_clickthrough" || item.Command == "pip_clickthrough")
            {
                items.Insert(i + 1, new TrayMenuItemDefinition
                {
                    Id = "pip_pause",
                    Title = "⏸️ Pause PiP Click-Through (10s)",
                    Command = "native:pip_pause",
                    Arguments = "10",
                    VisibleWhen = "pip.active",
                    ToggledWhen = "pip.paused",
                    ToggledTitle = "Unpause PiP Click-Through",
                    ToggledCommand = "native:pip_unpause"
                });
                return true;
            }
            if (item.Items != null && InsertPipPauseAfterPipClickThrough(item.Items))
            {
                return true;
            }
        }
        return false;
    }

    private static List<TrayMenuItemDefinition>? FindParentContainer(List<TrayMenuItemDefinition> container, TrayMenuItemDefinition target)
    {
        return FindParentContext(container, target)?.Container;
    }

    private static (List<TrayMenuItemDefinition> Container, TrayMenuItemDefinition? ParentItem)? FindParentContext(
        List<TrayMenuItemDefinition> container,
        TrayMenuItemDefinition target,
        TrayMenuItemDefinition? parentItem = null)
    {
        if (container.Contains(target)) return (container, parentItem);
        foreach (var item in container)
        {
            if (item.HasItems)
            {
                var match = FindParentContext(item.Items!, target, item);
                if (match != null) return match;
            }
        }
        return null;
    }

    private void SyncAudioDevicesInMenu()
    {
        try
        {
            var systemDevices = NativeAudioDeviceManager.GetOutputDevices();
            if (systemDevices.Count == 0) return;

            var outputSubmenu = _trayConfig.Items.FirstOrDefault(i => i.Id == "output_devices_menu" ||
                i.Title.Contains("Output Devices", StringComparison.OrdinalIgnoreCase));

            if (outputSubmenu == null)
            {
                outputSubmenu = new TrayMenuItemDefinition
                {
                    Id = "output_devices_menu",
                    Title = "🔊 Output Devices",
                    Items = []
                };

                int insertPos = _trayConfig.Items.Count > 0 && _trayConfig.Items[0].Command == "native:cycle_audio_device" ? 1 : 0;
                _trayConfig.Items.Insert(insertPos, outputSubmenu);
            }

            outputSubmenu.Items ??= [];

            foreach (var sysDev in systemDevices)
            {
                string devItemId = $"audio_dev_{sysDev.Id}";
                var existingItem = outputSubmenu.Items.FirstOrDefault(i => i.Id == devItemId ||
                    string.Equals(i.Command, $"native:select_audio_device?id={sysDev.Id}", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.Command, $"native:toggle_audio_device?id={sysDev.Id}", StringComparison.OrdinalIgnoreCase) ||
                    i.Title.StartsWith(sysDev.Name, StringComparison.OrdinalIgnoreCase));

                string titleWithStatus = sysDev.IsDefault ? $"{sysDev.Name} (Active)" : sysDev.Name;

                if (existingItem != null)
                {
                    existingItem.Id = devItemId;
                    existingItem.Title = titleWithStatus;
                    existingItem.Command = $"native:toggle_audio_device?id={sysDev.Id}";
                    // Keep existingItem.Checked (user setting!)
                }
                else
                {
                    outputSubmenu.Items.Add(new TrayMenuItemDefinition
                    {
                        Id = devItemId,
                        Title = titleWithStatus,
                        Command = $"native:toggle_audio_device?id={sysDev.Id}",
                        Checked = true // Checked by default = enabled for cycling per user requirement!
                    });
                }
            }
        }
        catch { }
    }

    private (bool success, string? deviceName, string? icon, string? error) CycleAudioOutputDevice()
    {
        try
        {
            SyncAudioDevicesInMenu();

            var systemDevices = NativeAudioDeviceManager.GetOutputDevices();
            if (systemDevices.Count == 0)
            {
                string msg = "No active audio output devices found on system.";
                Log($"[Audio Warning] {msg}");
                SetStatus(msg);
                return (false, null, null, msg);
            }

            var outputSubmenu = _trayConfig.Items.FirstOrDefault(i => i.Id == "output_devices_menu" ||
                i.Title.Contains("Output Devices", StringComparison.OrdinalIgnoreCase));

            var enabledDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (outputSubmenu?.Items != null && outputSubmenu.Items.Count > 0)
            {
                foreach (var item in outputSubmenu.Items)
                {
                    if (item.Checked) // Checked = enabled for cycling!
                    {
                        string devId = item.Command.Contains("?id=") ? item.Command[(item.Command.IndexOf("?id=") + 4)..] : item.Id.Replace("audio_dev_", "");
                        enabledDeviceIds.Add(devId);
                    }
                }
            }

            var eligibleDevices = systemDevices.Where(d => enabledDeviceIds.Contains(d.Id) || enabledDeviceIds.Count == 0).ToList();
            if (eligibleDevices.Count == 0) eligibleDevices = systemDevices;

            int currentIndex = eligibleDevices.FindIndex(d => d.IsDefault);
            int nextIndex = (currentIndex + 1) % eligibleDevices.Count;
            var targetDevice = eligibleDevices[nextIndex];

            var (switched, err) = NativeAudioDeviceManager.SetDefaultAudioDevice(targetDevice.Id);
            if (switched)
            {
                string iconFile = targetDevice.IconFile;
                var devMenuItem = outputSubmenu?.Items?.FirstOrDefault(i => i.Id == $"audio_dev_{targetDevice.Id}" || i.Command.Contains(targetDevice.Id));
                if (devMenuItem != null && !string.IsNullOrWhiteSpace(devMenuItem.Icon))
                {
                    iconFile = devMenuItem.Icon.Trim();
                }

                // Update status indicator icon ONLY on the single Next Audio Device menu item (next_audio_device)
                var nextAudioItem = FindMenuItemByIdOrTitle(_trayConfig.Items, "next_audio_device")
                                 ?? _trayConfig.Items.FirstOrDefault(i => i.Command == "native:cycle_audio_device");
                if (nextAudioItem != null)
                {
                    nextAudioItem.Icon = iconFile;
                }

                SyncAudioDevicesInMenu();
                SaveTrayMenuJson();
                RebuildTrayContextMenu();

                Log($"[Audio] Switched default audio device to: '{targetDevice.Name}' (Status Icon: {iconFile})");
                SetStatus($"Audio Output: {targetDevice.Name}");
                return (true, targetDevice.Name, iconFile, null);
            }
            else
            {
                string msg = $"Failed to set default audio device to '{targetDevice.Name}': {err}";
                Log($"[Audio Error] {msg}");
                SetStatus("Audio device switch failed");
                return (false, targetDevice.Name, null, msg);
            }
        }
        catch (Exception ex)
        {
            Log($"[Audio Exception] {ex.Message}");
            SetStatus($"Audio error: {ex.Message}");
            return (false, null, null, ex.Message);
        }
    }

    private void SetupTrayJsonWatcher()
    {
        try
        {
            _trayJsonWatcher = new FileSystemWatcher(_appDataDir, "tray_menu.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _trayJsonWatcher.Changed += (_, _) =>
            {
                // Debounce reload
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        Thread.Sleep(50);
                        LoadTrayMenuJson();
                        RebuildTrayContextMenu();
                        ApplyTrayIcon(string.IsNullOrWhiteSpace(_trayConfig.CurrentIcon) ? "default" : _trayConfig.CurrentIcon);
                        Log("Hot-reloaded tray_menu.json");
                    }
                    catch { }
                });
            };
            _trayJsonWatcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    #endregion

    #region Local IPC Command Server

    private void StartIpcServer()
    {
        _commandBroker = new CommandBroker();
        _commandBroker.AudioStatusHandler = () => new
        {
            peak = _audioMonitor.CurrentPeak,
            isPlaying = _audioMonitor.CurrentPeak > 0.0001f,
            isMonitoring = _audioMonitor.IsRunning
        };

        _commandBroker.CycleAudioHandler = () =>
        {
            return Dispatcher.Invoke(() =>
            {
                var (success, deviceName, icon, error) = CycleAudioOutputDevice();
                if (success)
                    return CommandResult.Ok(new { deviceName, icon });
                return CommandResult.Fail(error ?? "Audio output cycle failed");
            });
        };

        _commandBroker.ExecuteTaskHandler = taskId =>
        {
            return Dispatcher.Invoke<object>(() =>
            {
                var task = _tasks.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.OrdinalIgnoreCase) ||
                    t.ScriptPath.EndsWith(taskId, StringComparison.OrdinalIgnoreCase));
                if (task != null)
                {
                    StartTask(task);
                    return new { success = true, taskId = task.Id, name = task.Name };
                }
                return new { success = false, error = $"Task '{taskId}' not found" };
            });
        };

        _commandBroker.ApplyIconHandler = iconStr =>
        {
            Dispatcher.Invoke(() => ApplyTrayIcon(iconStr));
            return true;
        };

        _commandBroker.SetItemIconHandler = (targetId, iconStr) =>
        {
            return Dispatcher.Invoke<object>(() =>
            {
                if (string.IsNullOrWhiteSpace(targetId)) targetId = "next_audio_device";
                var targetItem = FindMenuItemByIdOrTitle(_trayConfig.Items, targetId);
                if (targetItem != null)
                {
                    targetItem.Icon = iconStr;
                    SaveTrayMenuJson();
                    RebuildTrayContextMenu();
                    Log($"[IPC] Updated status icon on menu item '{targetItem.Title}' to: '{iconStr}'");
                    return new { success = true, item = targetItem.Title, icon = iconStr };
                }
                return new { success = false, error = $"Menu item '{targetId}' not found" };
            });
        };

        _commandBroker.ApplyTooltipHandler = tipStr =>
        {
            Dispatcher.Invoke(() =>
            {
                _trayConfig.Tooltip = tipStr;
                _trayIcon.Text = tipStr;
                SaveTrayMenuJson();
            });
            return true;
        };

        _commandBroker.SetStateVariableHandler = (key, val) =>
        {
            SetStateVariable(key, val);
            return new { success = true, key, value = val };
        };

        _commandBroker.GetStateVariableHandler = key =>
        {
            string? val = GetStateVariable(key);
            return new { success = true, key, value = val, isSet = val != null };
        };

        _commandBroker.ToggleStateVariableHandler = key =>
        {
            bool nextState = ToggleStateVariable(key);
            return new { success = true, key, value = nextState ? "true" : "false", isSet = nextState };
        };

        _commandBroker.ClearStateVariableHandler = key =>
        {
            ClearStateVariable(key);
            return new { success = true, key, cleared = true };
        };

        _commandBroker.ListStateVariablesHandler = () =>
        {
            return _stateVariables.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        };

        _commandBroker.EvaluateConditionHandler = condition =>
        {
            bool result = EvaluateCondition(condition);
            return new { condition, result };
        };

        _ipcServer = new LocalIpcServer(_commandBroker);
        _ipcServer.LogMessage += msg => Dispatcher.Invoke(() => Log($"[IPC] {msg}"));
        _ipcServer.Start();
    }

    #endregion

    #region Task Execution & Process Management

    private void StartTask_Click(object sender, RoutedEventArgs e)
    {
        var selected = TasksList.SelectedItems.Cast<ScriptTask>().ToList();
        if (selected.Count == 0 && TasksList.SelectedItem is ScriptTask t)
            selected.Add(t);

        if (selected.Count == 0) { SetStatus("Select a task first."); return; }

        foreach (var task in selected)
            StartTask(task);
    }

    private void StopTask_Click(object sender, RoutedEventArgs e)
    {
        var selected = TasksList.SelectedItems.Cast<ScriptTask>().ToList();
        if (selected.Count == 0 && TasksList.SelectedItem is ScriptTask t)
            selected.Add(t);

        if (selected.Count == 0) { SetStatus("Select a task first."); return; }

        foreach (var task in selected)
            StopTask(task);
    }

    private void StartAll_Click(object sender, RoutedEventArgs e) => StartAllTasks();
    private void StopAll_Click(object sender, RoutedEventArgs e) => StopAllTasks();

    private void StartAllTasks()
    {
        int count = 0;
        foreach (var task in _tasks)
        {
            if (!task.IsRunning && File.Exists(task.ScriptPath))
            {
                StartTask(task);
                count++;
            }
        }
        Log($"Started {count} task(s).");
    }

    private void StopAllTasks()
    {
        int count = _runningProcesses.Count;
        foreach (var task in _tasks.Where(t => t.IsRunning).ToList())
            StopTask(task);
        Log($"Stopped {count} task process(es).");
    }

    private void StartTask(ScriptTask task)
    {
        if (task.IsRunning && _runningProcesses.ContainsKey(task.Id))
        {
            SetStatus($"Task '{task.Name}' is already running.");
            return;
        }

        if (task.ScriptPath.StartsWith("native:", StringComparison.OrdinalIgnoreCase))
            return;

        if (!File.Exists(task.ScriptPath))
        {
            Log($"[Error] Script file not found: {task.ScriptPath}");
            SetStatus("Script file not found.");
            return;
        }

        var cts = new CancellationTokenSource();
        _taskCts[task.Id] = cts;

        _ = Task.Run(async () =>
        {
            Process? process = null;
            try
            {
                var ext = System.IO.Path.GetExtension(task.ScriptPath).ToLowerInvariant();
                string exe;
                string args;
                if (!string.IsNullOrWhiteSpace(task.Launcher))
                {
                    exe = task.Launcher.Trim();
                    args = (task.Arguments ?? "").Replace("{script}", Quote(task.ScriptPath), StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    string extra = task.Arguments ?? "";
                    string hiddenArg = task.RunHidden ? "-WindowStyle Hidden " : "";
                    (exe, args) = ext switch
                    {
                        ".ps1" => ("powershell.exe", $"-NoLogo -NoProfile {hiddenArg}-ExecutionPolicy Bypass -File {Quote(task.ScriptPath)} {extra}".Trim()),
                        ".bat" or ".cmd" => (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", $"/d /s /c {Quote(task.ScriptPath)} {extra}".Trim()),
                        _ => (task.ScriptPath, extra)
                    };
                }

                string configuredWorkingDirectory = task.WorkingDirectory ?? "";
                string workingDirectory = !string.IsNullOrWhiteSpace(configuredWorkingDirectory) && Directory.Exists(configuredWorkingDirectory)
                    ? configuredWorkingDirectory
                    : (System.IO.Path.GetDirectoryName(task.ScriptPath) is string scriptDirectory && Directory.Exists(scriptDirectory)
                        ? scriptDirectory
                        : Environment.CurrentDirectory);

                int iteration = 0;
                do
                {
                    iteration++;
                    if (task.SilentElevate)
                    {
                        string taskId = $"task_{task.Id}";
                        if (!SilentElevationHelper.TaskExists(taskId))
                        {
                            if (!SilentElevationHelper.RegisterOrUpdateTask(
                                taskId, task.ScriptPath, task.Arguments ?? "", task.Launcher, workingDirectory, task.RunHidden, out string? regErr))
                            {
                                task.LastResult = "Idle (silent elevation setup failed)";
                                await Dispatcher.InvokeAsync(() => Log($"[Silent Elevate] Setup failed for '{task.Name}': {regErr}"));
                                break;
                            }
                        }

                        await Dispatcher.InvokeAsync(() =>
                        {
                            task.IsRunning = true;
                            task.Status = task.Loop ? $"Running Silent Elevated (Loop #{iteration})" : "Running Silent Elevated";
                            RefreshCounters();
                            if (iteration == 1) Log($"▶ Started silent elevated task: {task.Name}");
                        });

                        if (!SilentElevationHelper.RunScheduledTask(taskId, out string? runErr))
                        {
                            task.LastResult = "Idle (silent elevate failed)";
                            await Dispatcher.InvokeAsync(() => Log($"[Silent Elevate] Failed to run '{task.Name}': {runErr}"));
                            break;
                        }

                        task.LastResult = "Idle (triggered via Task Scheduler)";
                    }
                    else if (task.RunAsAdmin)
                    {
                        process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = exe,
                                Arguments = args,
                                WorkingDirectory = workingDirectory,
                                UseShellExecute = true,
                                Verb = "runas",
                                WindowStyle = task.RunHidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                            }
                        };

                        process.Start();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            _runningProcesses[task.Id] = process;
                            task.IsRunning = true;
                            task.Pid = process.Id;
                            task.Status = task.Loop ? $"Running Elevated (PID {process.Id}, Loop #{iteration})" : $"Running Elevated (PID {process.Id})";
                            RefreshCounters();
                            if (iteration == 1) Log($"▶ Started elevated task: {task.Name} (PID {process.Id})");
                        });

                        using var reg = cts.Token.Register(() =>
                        {
                            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                        });

                        await process.WaitForExitAsync();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (!task.Loop) Log($"⏹ Elevated task finished: {task.Name} (Exit code {process.ExitCode})");
                            task.LastResult = process.ExitCode == 0 ? "Idle (last run OK)" : $"Idle (last exit code {process.ExitCode})";
                        });
                        process.Dispose();
                        process = null;
                    }
                    else
                    {
                        bool useShell = !task.RunHidden;
                        process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = exe,
                                Arguments = args,
                                WorkingDirectory = workingDirectory,
                                UseShellExecute = useShell,
                                RedirectStandardOutput = task.RunHidden,
                                RedirectStandardError = task.RunHidden,
                                CreateNoWindow = task.RunHidden,
                                WindowStyle = task.RunHidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                            }
                        };

                        process.Start();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            _runningProcesses[task.Id] = process;
                            task.IsRunning = true;
                            task.Pid = process.Id;
                            task.Status = task.Loop ? $"Running (PID {process.Id}, Loop #{iteration})" : $"Running (PID {process.Id})";
                            RefreshCounters();
                            if (iteration == 1) Log($"▶ Started task: {task.Name} (PID {process.Id})");
                        });

                        var stdoutTask = task.RunHidden ? process.StandardOutput.ReadToEndAsync() : Task.FromResult("");
                        var stderrTask = task.RunHidden ? process.StandardError.ReadToEndAsync() : Task.FromResult("");

                        using var reg = cts.Token.Register(() =>
                        {
                            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                        });

                        await process.WaitForExitAsync();

                        var stdout = await stdoutTask;
                        var stderr = await stderrTask;

                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (!string.IsNullOrWhiteSpace(stdout)) LogLines($"[{task.Name}] {stdout.TrimEnd()}");
                            if (!string.IsNullOrWhiteSpace(stderr)) LogLines($"[{task.Name} stderr] {stderr.TrimEnd()}");
                            if (!task.Loop) Log($"⏹ Task finished: {task.Name} (Exit code {process.ExitCode})");
                            task.LastResult = process.ExitCode == 0 ? "Idle (last run OK)" : $"Idle (last exit code {process.ExitCode})";
                        });
                        process.Dispose();
                        process = null;
                    }

                    if (task.Loop && !cts.Token.IsCancellationRequested)
                    {
                        int delayMs = Math.Max(50, task.LoopDelayMs);
                        await Task.Delay(delayMs, cts.Token);
                    }
                } while (task.Loop && !cts.Token.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                task.LastResult = "Stopped";
            }
            catch (System.ComponentModel.Win32Exception wEx) when (wEx.NativeErrorCode == 1223)
            {
                task.LastResult = "Idle (elevation canceled)";
                await Dispatcher.InvokeAsync(() => Log($"[UAC] Administrator elevation was canceled by user for task '{task.Name}'."));
            }
            catch (Exception ex)
            {
                task.LastResult = "Idle (last run failed)";
                await Dispatcher.InvokeAsync(() => Log($"[Task Failed] {task.Name}: {ex.Message}"));
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _runningProcesses.Remove(task.Id);
                    _taskCts.Remove(task.Id);
                    process?.Dispose();
                    task.IsRunning = false;
                    task.Pid = null;
                    task.Status = task.LastResult;
                    RefreshCounters();
                });
                cts.Dispose();
            }
        });
    }

    private void StopTask(ScriptTask task)
    {
        if (task.ScriptPath.StartsWith("native:", StringComparison.OrdinalIgnoreCase))
            return;

        if (_taskCts.TryGetValue(task.Id, out var cts))
        {
            cts.Cancel();
            _taskCts.Remove(task.Id);
        }

        if (_runningProcesses.TryGetValue(task.Id, out var proc))
        {
            try
            {
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                Log($"Terminated process tree for {task.Name}");
            }
            catch { }
        }

        task.IsRunning = false;
        task.Pid = null;
        task.Status = "Stopped";
        RefreshCounters();

        if (task.Id == "audio_tray_monitor" || task.ScriptPath.EndsWith("AudioTrayMonitor.ps1", StringComparison.OrdinalIgnoreCase))
        {
            ApplyTrayIcon(string.IsNullOrWhiteSpace(_trayConfig.CurrentIcon) ? "default" : _trayConfig.CurrentIcon);
        }
    }

    private void RemoveTask_Click(object sender, RoutedEventArgs e)
    {
        var selected = TasksList.SelectedItems.Cast<ScriptTask>().ToList();
        if (selected.Count == 0 && TasksList.SelectedItem is ScriptTask t)
            selected.Add(t);

        if (selected.Count == 0) return;

        foreach (var task in selected)
        {
            StopTask(task);
            _tasks.Remove(task);
            Log($"Removed task: {task.Name}");

            if (task.Id == "audio_tray_monitor" || task.ScriptPath.EndsWith("AudioTrayMonitor.ps1", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTrayIcon(string.IsNullOrWhiteSpace(_trayConfig.CurrentIcon) ? "default" : _trayConfig.CurrentIcon);
            }
        }

        SaveSettings();
        RefreshCounters();
    }

    private void ExecuteCommand(string command, string arguments, string? workingDir = null, bool runHidden = false, bool runAsAdmin = false, bool silentElevate = false, string? silentTaskId = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        // Check for Native Built-in Commands (100% Native C# - Zero external dependencies)
        if (command.StartsWith("native:", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("internal:", StringComparison.OrdinalIgnoreCase))
        {
            string action = command.Substring(command.IndexOf(':') + 1).ToLowerInvariant();
            switch (action)
            {
                case "res_4k":
                case "resolution_4k":
                    bool ok4k = NativeDisplayManager.SetResolution(3840, 2160);
                    Log(ok4k ? "Display resolution set to 3840x2160." : "Could not switch to 3840x2160.");
                    SetStatus(ok4k ? "Resolution: 3840 x 2160" : "Resolution change failed");
                    return;

                case "res_1440":
                case "resolution_1440":
                    bool ok1440 = NativeDisplayManager.SetResolution(2560, 1440);
                    Log(ok1440 ? "Display resolution set to 2560x1440." : "Could not switch to 2560x1440.");
                    SetStatus(ok1440 ? "Resolution: 2560 x 1440" : "Resolution change failed");
                    return;

                case "res_1080":
                case "resolution_1080":
                    bool ok1080 = NativeDisplayManager.SetResolution(1920, 1080);
                    Log(ok1080 ? "Display resolution set to 1920x1080." : "Could not switch to 1920x1080.");
                    SetStatus(ok1080 ? "Resolution: 1920 x 1080" : "Resolution change failed");
                    return;

                case "toggle_hdr":
                case "hdr":
                    NativeDisplayManager.ToggleHdr();
                    Log("HDR toggle triggered (Win+Alt+B).");
                    SetStatus("HDR toggled (Win+Alt+B)");
                    return;

                case "pip_clickthrough":
                case "toggle_pip_clickthrough":
                case "pip_toggle":
                {
                    int.TryParse(arguments, out int alpha);
                    var (ok, msg) = PipProvider.ToggleClickThrough(alpha);

                    // Update Checked status in Tray Menu UI
                    var pipItem = FindMenuItemByIdOrTitle(_trayConfig.Items, "pip_clickthrough")
                               ?? _trayConfig.Items.FirstOrDefault(i => i.Command == "native:pip_clickthrough" || i.Command == "pip_clickthrough");
                    if (pipItem != null)
                    {
                        pipItem.Checked = PipProvider.IsTrackingActive;
                        SaveTrayMenuJson();
                        RebuildTrayContextMenu();
                    }

                    Log($"[PiP] {msg}");
                    SetStatus(msg);
                    return;
                }

                case "pip_pause":
                case "toggle_pip_pause":
                case "pause_pip":
                {
                    int seconds = 10;
                    if (!string.IsNullOrWhiteSpace(arguments) && int.TryParse(arguments.Trim(), out int sec) && sec > 0)
                    {
                        seconds = sec;
                    }
                    var (ok, msg) = PipProvider.PauseClickThrough(seconds);
                    Log($"[PiP] {msg}");
                    SetStatus(msg);
                    return;
                }

                case "pip_unpause":
                case "unpause_pip":
                {
                    var (ok, msg) = PipProvider.ResumeClickThrough();
                    Log($"[PiP] {msg}");
                    SetStatus(msg);
                    return;
                }

                case "cycle_audio_device":
                case "next_audio_device":
                case "cycle_audio":
                    Dispatcher.Invoke(CycleAudioOutputDevice);
                    return;

                case var act when act.StartsWith("toggle_audio_device"):
                {
                    string devId = "";
                    if (act.Contains("?id=")) devId = act[(act.IndexOf("?id=") + 4)..];
                    else if (!string.IsNullOrWhiteSpace(arguments)) devId = arguments;

                    if (!string.IsNullOrWhiteSpace(devId))
                    {
                        var outputSubmenu = _trayConfig.Items.FirstOrDefault(i => i.Id == "output_devices_menu" ||
                            i.Title.Contains("Output Devices", StringComparison.OrdinalIgnoreCase));
                        var item = outputSubmenu?.Items?.FirstOrDefault(i => i.Id == $"audio_dev_{devId}" ||
                            i.Command.Contains(devId));
                        if (item != null)
                        {
                            item.Checked = !item.Checked;
                            SaveTrayMenuJson();
                            RebuildTrayContextMenu();
                            Log($"[Audio] Toggled device '{item.Title}' enabled state to: {item.Checked}");
                            SetStatus($"Audio device {(item.Checked ? "enabled" : "disabled")}: {item.Title}");
                        }
                    }
                    return;
                }

                case var act when act.StartsWith("select_audio_device"):
                {
                    string devId = "";
                    if (act.Contains("?id=")) devId = act[(act.IndexOf("?id=") + 4)..];
                    else if (!string.IsNullOrWhiteSpace(arguments)) devId = arguments;

                    if (!string.IsNullOrWhiteSpace(devId))
                    {
                        var (switched, selectErr) = NativeAudioDeviceManager.SetDefaultAudioDevice(devId);
                        if (switched)
                        {
                            var devs = NativeAudioDeviceManager.GetOutputDevices();
                            var dev = devs.FirstOrDefault(d => string.Equals(d.Id, devId, StringComparison.OrdinalIgnoreCase));
                            string name = dev?.Name ?? "Audio Device";
                            string iconFile = dev?.IconFile ?? NativeAudioDeviceManager.GetIconForDeviceName(name);

                            var outputSubmenu = _trayConfig.Items.FirstOrDefault(i => i.Id == "output_devices_menu" ||
                                i.Title.Contains("Output Devices", StringComparison.OrdinalIgnoreCase));
                            var devMenuItem = outputSubmenu?.Items?.FirstOrDefault(i => i.Id == $"audio_dev_{devId}" || i.Command.Contains(devId));
                            if (devMenuItem != null && !string.IsNullOrWhiteSpace(devMenuItem.Icon))
                            {
                                iconFile = devMenuItem.Icon.Trim();
                            }

                            _trayConfig.CurrentIcon = iconFile;
                            ApplyTrayIcon(iconFile);

                            // Update status indicator icon ONLY on the single Next Audio Device menu item (next_audio_device)
                            var nextAudioItem = FindMenuItemByIdOrTitle(_trayConfig.Items, "next_audio_device")
                                             ?? _trayConfig.Items.FirstOrDefault(i => i.Command == "native:cycle_audio_device");
                            if (nextAudioItem != null)
                            {
                                nextAudioItem.Icon = iconFile;
                            }

                            SyncAudioDevicesInMenu();
                            SaveTrayMenuJson();
                            RebuildTrayContextMenu();

                            Log($"[Audio] Switched audio output device to: '{name}'");
                            SetStatus($"Audio Output: {name}");
                        }
                        else
                        {
                            Log($"[Audio Error] Could not switch to device '{devId}': {selectErr}");
                            SetStatus($"Audio switch failed: {selectErr}");
                        }
                    }
                    return;
                }

                case "open":
                case "show":
                case "open_window":
                case "toggle_window":
                case "native:open":
                    Dispatcher.Invoke(ShowMainWindow);
                    return;

                case "open_scripts":
                    OpenScriptsFolder_Click(this, new RoutedEventArgs());
                    return;

                case var act when act.StartsWith("state_set:"):
                {
                    string target = act[10..];
                    string k = target, v = "true";
                    int eqIdx = target.IndexOf('=');
                    if (eqIdx > 0) { k = target[..eqIdx]; v = target[(eqIdx + 1)..]; }
                    SetStateVariable(k, v);
                    Log($"[State] Set variable '{k}' = '{v}'");
                    return;
                }

                case var act when act.StartsWith("state_toggle:"):
                {
                    string k = act[13..];
                    bool next = ToggleStateVariable(k);
                    Log($"[State] Toggled variable '{k}' to: {next}");
                    return;
                }

                case var act when act.StartsWith("state_clear:"):
                {
                    string k = act[12..];
                    ClearStateVariable(k);
                    Log($"[State] Cleared variable '{k}'");
                    return;
                }

                case var act when act.StartsWith("kill_process:") || act.StartsWith("stop_process:"):
                {
                    string target = act[(act.IndexOf(':') + 1)..].Trim();
                    if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) target = target[..^4];
                    try
                    {
                        var procs = System.Diagnostics.Process.GetProcessesByName(target);
                        int count = 0;
                        foreach (var p in procs)
                        {
                            try { p.Kill(true); count++; } catch { }
                        }
                        Log($"[Process] Terminated {count} process(es) matching '{target}'.");
                        SetStatus($"Stopped process: {target}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[Process Error] Failed to stop process '{target}': {ex.Message}");
                    }
                    return;
                }
            }
        }

        // Standard Windows-native scripts & executables:
        Task.Run(() =>
        {
            try
            {
                if (silentElevate)
                {
                    string taskId = string.IsNullOrWhiteSpace(silentTaskId) ? "tray_custom" : silentTaskId;
                    if (!SilentElevationHelper.TaskExists(taskId))
                    {
                        if (!SilentElevationHelper.RegisterOrUpdateTask(taskId, command, arguments, null, workingDir, runHidden, out string? regErr))
                        {
                            Dispatcher.Invoke(() => Log($"[Silent Elevate] Setup failed for '{command}': {regErr}"));
                            return;
                        }
                    }
                    if (!SilentElevationHelper.RunScheduledTask(taskId, out string? runErr))
                    {
                        Dispatcher.Invoke(() => Log($"[Silent Elevate] Could not run '{command}': {runErr}"));
                    }
                    return;
                }

                var ext = System.IO.Path.GetExtension(command).ToLowerInvariant();
                string exe;
                string args;

                if (ext == ".ps1")
                {
                    exe = "powershell.exe";
                    string hiddenArg = runHidden ? "-WindowStyle Hidden " : "";
                    args = $"-NoLogo -NoProfile {hiddenArg}-ExecutionPolicy Bypass -File {Quote(command)} {arguments}".Trim();
                }
                else if (ext == ".bat" || ext == ".cmd")
                {
                    exe = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    args = $"/d /s /c {Quote(command)} {arguments}".Trim();
                }
                else
                {
                    exe = command;
                    args = arguments;
                }

                string targetWorkingDir = !string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir)
                    ? workingDir
                    : (Directory.Exists(System.IO.Path.GetDirectoryName(command))
                        ? System.IO.Path.GetDirectoryName(command)!
                        : Environment.CurrentDirectory);

                bool isUrl = command.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             command.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                             command.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

                bool useShellExecute = isUrl || !runHidden || runAsAdmin;

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = targetWorkingDir,
                    UseShellExecute = useShellExecute,
                    CreateNoWindow = runHidden && !useShellExecute,
                    WindowStyle = runHidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                };

                if (runAsAdmin)
                {
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                }

                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception wEx) when (wEx.NativeErrorCode == 1223)
            {
                Dispatcher.Invoke(() => Log($"[UAC] Administrator elevation was canceled by user for '{command}'."));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Log($"Could not run command '{command}': {ex.Message}"));
            }
        });
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    #endregion

    #region File & Folder Hide / Super-Hide & Multi-Selection

    private void OpenMultiPicker_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileFolderPickerDialog
        {
            Owner = this
        };

        if (picker.ShowDialog() == true)
        {
            var selected = picker.SelectedPaths;
            if (selected.Count == 0) return;

            bool superHidden = picker.AddAsSuperHidden;
            int added = AddRulesBatch(selected, superHidden: superHidden, autoApply: true);
            string modeLabel = superHidden ? "Super-Hid (+H +S)" : "Hid (+H)";
            Log($"Added and {modeLabel} {added} item(s) from Multi-Item Picker.");
            SetStatus($"Added {added} item(s) ({modeLabel}).");
        }
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select one or more files to hide",
            CheckFileExists = true,
            Multiselect = true,
            Filter = "All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            int added = AddRulesBatch(dialog.FileNames, superHidden: true, autoApply: true);
            Log($"Added and Super-Hid {added} file(s).");
            SetStatus($"Added {added} file(s).");
        }
    }

    private void AddFolders_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select one or more folders to hide (use Ctrl/Shift for multiple)",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true && dialog.FolderNames.Length > 0)
        {
            int added = AddRulesBatch(dialog.FolderNames, superHidden: true, autoApply: true);
            Log($"Added and Super-Hid {added} folder(s).");
            SetStatus($"Added {added} folder(s).");
        }
    }

    private void RulesList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            e.Effects = System.Windows.DragDropEffects.Copy;
        else
            e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void RulesList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                int added = AddRulesBatch(paths, superHidden: true, autoApply: true);
                Log($"Dropped and Super-Hid {added} item(s).");
                SetStatus($"Added {added} dropped item(s).");
            }
        }
    }

    private int AddRulesBatch(IEnumerable<string> paths, bool superHidden, bool autoApply)
    {
        int count = 0;
        foreach (var rawPath in paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            string path = rawPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            if (path.Length == 2 && path[1] == ':') path += System.IO.Path.DirectorySeparatorChar;

            bool isDir = Directory.Exists(path);
            var existing = _rules.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.IsSuperHidden = superHidden;
                existing.Enforce = true;
                if (autoApply)
                    ApplyFileAttribute(existing.Path, hide: true, superHide: superHidden);
                existing.UpdateStatusFromDisk();
                count++;
                continue;
            }

            var rule = new FileRule
            {
                Path = path,
                Kind = isDir ? "Folder" : "File",
                IsSuperHidden = superHidden,
                Enforce = true
            };

            if (autoApply)
                ApplyFileAttribute(rule.Path, hide: true, superHide: superHidden);

            rule.UpdateStatusFromDisk();
            _rules.Add(rule);
            count++;
        }

        if (count > 0)
        {
            SaveSettings();
            SyncRuleWatchers();
            RefreshCounters();
        }
        return count;
    }

    private List<FileRule> GetSelectedRules()
    {
        var selected = RulesList.SelectedItems.Cast<FileRule>().ToList();
        if (selected.Count == 0 && RulesList.SelectedItem is FileRule r)
            selected.Add(r);
        return selected;
    }

    private void SuperHideSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRules();
        if (selected.Count == 0) { SetStatus("Select one or more items first."); return; }

        foreach (var rule in selected)
        {
            rule.IsSuperHidden = true;
            rule.Enforce = true;
            ApplyFileAttribute(rule.Path, hide: true, superHide: true);
            rule.UpdateStatusFromDisk();
        }
        SaveSettings();
        SyncRuleWatchers();
        Log($"Super-Hid {selected.Count} item(s) (+H +S).");
        SetStatus($"Super-Hid {selected.Count} item(s).");
    }

    private void HideSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRules();
        if (selected.Count == 0) { SetStatus("Select one or more items first."); return; }

        foreach (var rule in selected)
        {
            rule.IsSuperHidden = false;
            rule.Enforce = true;
            ApplyFileAttribute(rule.Path, hide: true, superHide: false);
            rule.UpdateStatusFromDisk();
        }
        SaveSettings();
        SyncRuleWatchers();
        Log($"Hid {selected.Count} item(s) (+H).");
        SetStatus($"Hid {selected.Count} item(s).");
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRules();
        if (selected.Count == 0) { SetStatus("Select one or more items first."); return; }

        foreach (var rule in selected)
        {
            rule.Enforce = false;
            ApplyFileAttribute(rule.Path, hide: false, superHide: false);
            rule.UpdateStatusFromDisk();
        }
        SaveSettings();
        SyncRuleWatchers();
        Log($"Restored/Unhid {selected.Count} item(s) and paused auto-enforce.");
        SetStatus($"Restored {selected.Count} item(s).");
    }

    private void ToggleEnforceSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRules();
        if (selected.Count == 0) { SetStatus("Select one or more items first."); return; }

        bool targetState = selected.Any(r => !r.Enforce);
        foreach (var rule in selected)
        {
            rule.Enforce = targetState;
            if (targetState)
            {
                ApplyFileAttribute(rule.Path, hide: true, superHide: rule.IsSuperHidden);
                rule.UpdateStatusFromDisk();
            }
        }
        SaveSettings();
        SyncRuleWatchers();
        SetStatus($"Auto-enforce {(targetState ? "enabled" : "paused")} for {selected.Count} item(s).");
    }

    private void RulesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RulesList.SelectedItem is FileRule rule)
        {
            rule.IsSuperHidden = !rule.IsSuperHidden;
            if (rule.Enforce)
                ApplyFileAttribute(rule.Path, hide: true, superHide: rule.IsSuperHidden);
            rule.UpdateStatusFromDisk();
            SaveSettings();
            SetStatus($"{System.IO.Path.GetFileName(rule.Path)} set to {rule.ProtectionLabel}");
        }
    }

    private void OpenRuleFolder_Click(object sender, RoutedEventArgs e)
    {
        var rule = RulesList.SelectedItem as FileRule;
        if (rule == null) return;
        string? folder = Directory.Exists(rule.Path) ? rule.Path : System.IO.Path.GetDirectoryName(rule.Path);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = Quote(folder), UseShellExecute = true });
            }
            catch { }
        }
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRules();
        if (selected.Count == 0) return;

        foreach (var rule in selected)
        {
            _rules.Remove(rule);
            Log($"Removed rule: {rule.Path}");
        }

        SaveSettings();
        SyncRuleWatchers();
        RefreshCounters();
    }

    private void EnforceRules_Click(object sender, RoutedEventArgs e) => EnforceAllRules(true);

    private void EnforceAllRules(bool report)
    {
        int changed = 0;
        foreach (var rule in _rules.Where(r => r.Enforce))
        {
            try
            {
                if (!File.Exists(rule.Path) && !Directory.Exists(rule.Path))
                {
                    rule.UpdateStatusFromDisk();
                    continue;
                }

                bool wasChanged = ApplyFileAttribute(rule.Path, hide: true, superHide: rule.IsSuperHidden);
                if (wasChanged) changed++;
                rule.UpdateStatusFromDisk();
            }
            catch (Exception ex)
            {
                if (report) Log($"Enforce failed for {rule.Path}: {ex.Message}");
            }
        }

        if (report)
        {
            Log($"Rule enforcement complete. Updated {changed} item(s).");
            SetStatus($"Enforced all rules ({changed} updated).");
        }
        else if (changed > 0)
        {
            Log($"[Auto-Enforce] Re-applied hidden attributes to {changed} item(s).");
        }
    }

    private void SyncRuleWatchers()
    {
        try
        {
            var requiredDirs = _rules
                .Where(r => r.Enforce && !string.IsNullOrWhiteSpace(r.Path))
                .Select(r => System.IO.Path.GetDirectoryName(r.Path) ?? "")
                .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Remove watchers no longer needed
            foreach (var existingDir in _ruleWatchers.Keys.ToList())
            {
                if (!requiredDirs.Contains(existingDir))
                {
                    try { _ruleWatchers[existingDir].Dispose(); } catch { }
                    _ruleWatchers.Remove(existingDir);
                }
            }

            // Add watchers for new parent directories
            foreach (var dir in requiredDirs)
            {
                if (_ruleWatchers.ContainsKey(dir)) continue;

                try
                {
                    var watcher = new FileSystemWatcher(dir)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.LastWrite
                    };

                    watcher.Created += (_, args) => OnRuleFileSystemEvent(args.FullPath);
                    watcher.Changed += (_, args) => OnRuleFileSystemEvent(args.FullPath);
                    watcher.Renamed += (_, args) => OnRuleFileSystemEvent(args.FullPath);
                    watcher.EnableRaisingEvents = true;

                    _ruleWatchers[dir] = watcher;
                }
                catch { }
            }

            if (RuleWatcherStatusText != null)
            {
                int activeRules = _rules.Count(r => r.Enforce);
                RuleWatcherStatusText.Text = activeRules > 0
                    ? $"⚡ Instant watcher active ({_ruleWatchers.Count} parent folder(s), {activeRules} rule(s)) + 30s safety check"
                    : "💡 Tip: Drag & drop multiple files/folders directly from Explorer!";
            }
        }
        catch { }
    }

    private void OnRuleFileSystemEvent(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;

        // Only schedule if this exact path is in our enforced rules
        if (!_pendingRuleChecks.TryAdd(fullPath, 1))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150);
                await Dispatcher.InvokeAsync(() =>
                {
                    var rule = _rules.FirstOrDefault(r => r.Enforce && string.Equals(r.Path, fullPath, StringComparison.OrdinalIgnoreCase));
                    if (rule == null) return;

                    if (ApplyFileAttribute(rule.Path, hide: true, superHide: rule.IsSuperHidden))
                    {
                        Log($"[Watcher] Re-hid modified/recreated item: {rule.Path}");
                    }
                    rule.UpdateStatusFromDisk();
                });
            }
            catch { }
            finally
            {
                _pendingRuleChecks.TryRemove(fullPath, out _);
            }
        });
    }

    private void DisposeRuleWatchers()
    {
        foreach (var w in _ruleWatchers.Values)
        {
            try { w.Dispose(); } catch { }
        }
        _ruleWatchers.Clear();
    }

    private static bool ApplyFileAttribute(string path, bool hide, bool superHide)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return false;

            bool isDir = Directory.Exists(path);
            var attrs = File.GetAttributes(path);
            var original = attrs;

            if (hide)
            {
                attrs |= FileAttributes.Hidden;
                if (superHide)
                {
                    attrs |= FileAttributes.System;
                    if (isDir) attrs |= FileAttributes.ReadOnly;
                }
                else
                {
                    attrs &= ~FileAttributes.System;
                }
            }
            else
            {
                attrs &= ~FileAttributes.Hidden;
                attrs &= ~FileAttributes.System;
                if (isDir) attrs &= ~FileAttributes.ReadOnly;
            }

            if (attrs != original)
            {
                File.SetAttributes(path, attrs);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    #endregion

    #region Tray & API Tab Controls

    private void OpenTrayMenuJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureDefaultTrayMenuJson();
            Process.Start(new ProcessStartInfo
            {
                FileName = _trayMenuJsonPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"Could not open tray_menu.json: {ex.Message}");
        }
    }

    private void OpenSettingsJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            if (!File.Exists(_settingsPath))
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, JsonOptions));
            _settingsFileOpenForExternalEdit = true;
            Process.Start(new ProcessStartInfo { FileName = _settingsPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _settingsFileOpenForExternalEdit = false;
            Log($"Could not open settings.json: {ex.Message}");
        }
    }

    private void ReloadSettingsJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                SetStatus("settings.json does not exist yet.");
                return;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            if (loaded == null)
            {
                SetStatus("Could not parse settings.json.");
                return;
            }

            StopAllTasks();
            _tasks.Clear();
            foreach (var task in loaded.Tasks ?? [])
            {
                if (string.IsNullOrWhiteSpace(task.ScriptPath) ||
                    task.ScriptPath.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase) ||
                    task.ScriptPath.StartsWith("native:", StringComparison.OrdinalIgnoreCase))
                    continue;

                task.IsRunning = false;
                task.Pid = null;
                task.Status = "Stopped";
                _tasks.Add(task);
            }

            _rules.Clear();
            foreach (var rule in loaded.Rules ?? [])
            {
                if (string.IsNullOrWhiteSpace(rule.Path)) continue;
                rule.UpdateStatusFromDisk();
                _rules.Add(rule);
            }

            _settings = loaded;
            _settingsFileOpenForExternalEdit = false;
            EnforceAllRules(report: false);
            SyncRuleWatchers();
            bool prevLoaded = _isLoaded;
            _isLoaded = false;
            StartWithWindowsCheckbox.IsChecked = _settings.StartWithWindows;
            StartInTrayCheckbox.IsChecked = _settings.StartInTray;
            EfficiencyModeCheckbox.IsChecked = _settings.EfficiencyMode;
            EfficiencyModeManager.Apply(_settings.EfficiencyMode);
            _isLoaded = prevLoaded;
            SetStartup(_settings.StartWithWindows, _settings.StartInTray);
            RefreshCounters();
            Log("Reloaded task configuration from settings.json.");
            SetStatus("Task settings reloaded.");
        }
        catch (Exception ex)
        {
            Log($"Could not reload settings.json: {ex.Message}");
            SetStatus("Could not reload settings.json.");
        }
    }

    private void ReloadTray_Click(object sender, RoutedEventArgs e)
    {
        LoadTrayMenuJson();
        RebuildTrayContextMenu();
        ApplyTrayIcon(string.IsNullOrWhiteSpace(_trayConfig.CurrentIcon) ? "default" : _trayConfig.CurrentIcon);
        Log("Reloaded tray menu from disk.");
        SetStatus("Tray menu reloaded.");
    }

    private void PickCustomIcon_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            string? iconRef = IconPickerHelper.PickIcon(helper.Handle, _displayedIconRef ?? _trayConfig.CurrentIcon);
            if (!string.IsNullOrEmpty(iconRef))
            {
                _trayConfig.CurrentIcon = iconRef;
                ApplyTrayIcon(iconRef);
                SaveTrayMenuJson();
                Log($"Custom tray icon selected: {iconRef}");
                SetStatus($"Tray icon set to: {iconRef}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error with Windows icon picker: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Could not open Windows icon picker: {ex.Message}",
                "Icon Picker Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ResetDefaultIcon_Click(object sender, RoutedEventArgs e)
    {
        _trayConfig.CurrentIcon = "default";
        ApplyTrayIcon("default");
        SaveTrayMenuJson();
        Log("Tray icon reset to default (icon.ico).");
        SetStatus("Tray icon reset to default (icon.ico).");
    }

    private void CopyIconString_Click(object sender, RoutedEventArgs e)
    {
        string iconStr = _displayedIconRef;
        if (string.IsNullOrWhiteSpace(iconStr)) iconStr = "icon1.ico";
        string psCommand = $"[System.IO.File]::WriteAllText('\\\\.\\pipe\\xTaskForgePipe', '{{\"command\":\"icon.set\",\"params\":{{\"icon\":\"{iconStr}\"}}}}')";
        System.Windows.Clipboard.SetText(psCommand);
        Log($"[IPC] Copied PowerShell IPC command to clipboard: {psCommand}");
        SetStatus($"Copied IPC command to clipboard: {psCommand}");
    }

    private void ClearCustomMenu_Click(object sender, RoutedEventArgs e)
    {
        _trayConfig.Items.Clear();
        RebuildTrayContextMenu();
        SaveTrayMenuJson();
        Log("Cleared all custom tray menu items.");
        SetStatus("Custom items cleared.");
    }

    private void OpenScriptsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ScriptsDir);
            Process.Start(new ProcessStartInfo { FileName = ScriptsDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log($"Could not open scripts folder: {ex.Message}");
        }
    }

    private void OpenGuide_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            const string githubGuideUrl = "https://github.com/xCONFLiCTiONx/xTaskForge/blob/main/README_Scripting_Guide.md";
            Process.Start(new ProcessStartInfo
            {
                FileName = githubGuideUrl,
                UseShellExecute = true
            });
            Log($"Opened online guide on GitHub: {githubGuideUrl}");
        }
        catch (Exception ex)
        {
            Log($"Could not open guide on GitHub: {ex.Message}");
            SetStatus("Could not open guide on GitHub.");
        }
    }

    #endregion

    #region Settings Persistence & State

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings == null) return;

            _settings = settings;

            foreach (var task in settings.Tasks)
            {
                if (task.ScriptPath.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (task.ScriptPath.StartsWith("native:", StringComparison.OrdinalIgnoreCase))
                    continue;
                else
                {
                    task.IsRunning = false;
                    task.Pid = null;
                    task.Status = "Stopped";
                }
                _tasks.Add(task);
            }

            foreach (var rule in settings.Rules ?? [])
            {
                if (string.IsNullOrWhiteSpace(rule.Path)) continue;
                rule.UpdateStatusFromDisk();
                _rules.Add(rule);
            }
        }
        catch (Exception ex)
        {
            Log($"Could not load settings: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        if (_settingsFileOpenForExternalEdit) return;
        try
        {
            _settings.Tasks = _tasks.ToList();
            _settings.Rules = _rules.ToList();
            _settings.Placement = CaptureCurrentPlacement();

            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, JsonOptions));
        }
        catch (Exception ex)
        {
            Log($"Could not save settings: {ex.Message}");
        }
    }

    private void SendToTray_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        Hide();
        // Silent send to tray: NO notifications!
    }

    private void StartInTray_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.StartInTray = true;
        if (_settings.StartWithWindows)
            SetStartup(true, true);
        SaveSettings();
        Log("[Startup] Start in tray enabled.");
        SetStatus("Start in tray enabled");
    }

    private void StartInTray_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.StartInTray = false;
        if (_settings.StartWithWindows)
            SetStartup(true, false);
        SaveSettings();
        Log("[Startup] Start in tray disabled.");
        SetStatus("Start in tray disabled");
    }
    private void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            SaveSettings();
            Hide();
            // NO balloon notifications!
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_reallyClosing)
        {
            e.Cancel = true;
            SaveSettings();
            Hide();
            // NO balloon notifications!
            return;
        }

        // Really exiting
        _enforceTimer.Stop();
        DisposeRuleWatchers();
        _audioMonitor.Stop();
        StopAllTasks();
        PipProvider.RestoreAllWindowsAndShutdown();
        _ipcServer?.Stop();
        _trayJsonWatcher?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        SaveSettings();
    }

    private void ExitApplication()
    {
        _reallyClosing = true;
        SaveSettings();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    #endregion

    #region Logging & UI Helpers

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void Log(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Log(message));
            return;
        }
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void LogLines(string output)
    {
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (!string.IsNullOrWhiteSpace(line))
                Log(line);
        }
    }

    private void SetStatus(string text) => FooterStatus.Text = text;

    private void RefreshCounters()
    {
        TaskCountText.Text = _tasks.Count.ToString();
        RunningCountText.Text = _tasks.Count(t => t.IsRunning).ToString();
        RuleCountText.Text = _rules.Count.ToString();
    }

    #endregion

    #region Windows Startup Registry Management

    private static bool IsStartupEnabled(out bool hasTrayArg)
    {
        hasTrayArg = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            if (key?.GetValue(AppName) is string val && !string.IsNullOrWhiteSpace(val))
            {
                hasTrayArg = val.Contains("--tray", StringComparison.OrdinalIgnoreCase) ||
                             val.Contains("-tray", StringComparison.OrdinalIgnoreCase) ||
                             val.Contains("/tray", StringComparison.OrdinalIgnoreCase);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static void SetStartup(bool enable, bool inTray)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;
            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string target = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xTaskForge.exe");
                    exePath = File.Exists(target) ? target : (exePath ?? target);
                }
                string cmd = inTray ? $"\"{exePath}\" --tray" : $"\"{exePath}\"";
                key.SetValue(AppName, cmd);
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch { }
    }

    private void StartWithWindows_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.StartWithWindows = true;
        SetStartup(true, _settings.StartInTray);
        SaveSettings();
        Log("[Startup] xTaskForge registered to launch on Windows startup.");
        SetStatus("Start with Windows enabled");
    }

    private void StartWithWindows_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.StartWithWindows = false;
        SetStartup(false, false);
        SaveSettings();
        Log("[Startup] xTaskForge removed from Windows startup.");
        SetStatus("Start with Windows disabled");
    }

    private void EfficiencyMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.EfficiencyMode = true;
        EfficiencyModeManager.Apply(true);
        SaveSettings();
        Log("[System] Windows Efficiency Mode (EcoQoS) enabled.");
        SetStatus("Efficiency Mode enabled");
    }

    private void EfficiencyMode_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.EfficiencyMode = false;
        EfficiencyModeManager.Apply(false);
        SaveSettings();
        Log("[System] Windows Efficiency Mode (EcoQoS) disabled.");
        SetStatus("Efficiency Mode disabled");
    }
    #endregion

    #region Table Column Auto-Stretching

    private void RulesList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (RulesList.View is System.Windows.Controls.GridView gridView && gridView.Columns.Count >= 5)
        {
            double available = RulesList.ActualWidth - 35;
            double fixedCols = gridView.Columns[0].ActualWidth + gridView.Columns[2].ActualWidth + gridView.Columns[3].ActualWidth + gridView.Columns[4].ActualWidth;
            double remaining = available - fixedCols;
            if (remaining > 180)
                gridView.Columns[1].Width = remaining;
        }
    }

    private void CustomTrayMenuItemsList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        AdjustTrayMenuColumns();
    }

    private void AdjustTrayMenuColumns()
    {
        if (CustomTrayMenuItemsList.View is System.Windows.Controls.GridView gridView && gridView.Columns.Count >= 3)
        {
            double available = CustomTrayMenuItemsList.ActualWidth - 30;
            double col0 = gridView.Columns[0].ActualWidth > 0 ? gridView.Columns[0].ActualWidth : 180;
            double col1 = gridView.Columns[1].ActualWidth > 0 ? gridView.Columns[1].ActualWidth : 180;
            double colAdmin = gridView.Columns.Count >= 4 ? (gridView.Columns[3].ActualWidth > 0 ? gridView.Columns[3].ActualWidth : 65) : 0;
            double remaining = available - col0 - col1 - colAdmin;
            if (remaining > 100)
            {
                gridView.Columns[2].Width = remaining;
            }
        }
    }

    private void TasksList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        AdjustTasksColumns();
    }

    private void AdjustTasksColumns()
    {
        if (TasksList.View is System.Windows.Controls.GridView gridView && gridView.Columns.Count >= 6)
        {
            double available = TasksList.ActualWidth - 35;
            double fixedCols = 0;
            for (int i = 0; i < gridView.Columns.Count; i++)
            {
                if (i == 4) continue; // Arguments column (index 4) takes remaining space
                fixedCols += gridView.Columns[i].ActualWidth > 0 ? gridView.Columns[i].ActualWidth : gridView.Columns[i].Width;
            }
            double remaining = available - fixedCols;
            if (remaining > 100)
            {
                gridView.Columns[4].Width = remaining;
            }
        }
    }

    #endregion

    #region Dynamic Tray Menu Item Management (Add, Edit, Remove, Reorder)

    private void AddTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TrayItemDialog();
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
            _trayConfig.Items.Add(dlg.Item);

            SaveTrayMenuJson();
            RebuildTrayContextMenu();
            CustomTrayMenuItemsList.SelectedItem = dlg.Item;
            CustomTrayMenuItemsList.ScrollIntoView(dlg.Item);
            Log($"Added dynamic tray menu item: {dlg.Item.Title}");
            SetStatus($"Added: {dlg.Item.Title}");
        }
    }

    private void EditTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selected = CustomTrayMenuItemsList.SelectedItem as TrayMenuItemDefinition;
        if (selected == null)
        {
            SetStatus("Select a tray menu item to edit.");
            return;
        }
        var dlg = new TrayItemDialog(selected);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
            SaveTrayMenuJson();
            RebuildTrayContextMenu();
            Log($"Updated dynamic tray menu item: {selected.Title}");
            SetStatus($"Updated: {selected.Title}");
        }
    }

    private void CustomTrayMenuItemsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CustomTrayMenuItemsList.SelectedItem is TrayMenuItemDefinition)
        {
            EditTrayMenuItem_Click(sender, e);
        }
    }

    private void RemoveTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selected = CustomTrayMenuItemsList.SelectedItem as TrayMenuItemDefinition;
        if (selected == null)
        {
            SetStatus("Select an item to remove.");
            return;
        }

        var parentList = FindParentContainer(_trayConfig.Items, selected);
        if (parentList != null)
        {
            parentList.Remove(selected);
            SaveTrayMenuJson();
            RebuildTrayContextMenu();
            Log($"Removed dynamic tray menu item: {selected.Title}");
            SetStatus("Item removed.");
        }
    }

    private void MoveUpTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MoveTrayMenuItem(moveUp: true);
    }

    private void MoveDownTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MoveTrayMenuItem(moveUp: false);
    }

    private void MoveTrayMenuItem(bool moveUp)
    {
        var selected = CustomTrayMenuItemsList.SelectedItem as TrayMenuItemDefinition;
        if (selected == null) return;

        var ctx = FindParentContext(_trayConfig.Items, selected);
        if (ctx == null) return;

        var (parentList, parentItem) = ctx.Value;
        int idx = parentList.IndexOf(selected);
        if (idx < 0) return;

        bool moved = false;
        if (moveUp ? idx > 0 : idx < parentList.Count - 1)
        {
            int siblingIdx = moveUp ? idx - 1 : idx + 1;
            var sibling = parentList[siblingIdx];
            if (sibling.HasItems)
            {
                parentList.RemoveAt(idx);
                sibling.Items ??= [];
                if (moveUp)
                    sibling.Items.Add(selected);
                else
                    sibling.Items.Insert(0, selected);
            }
            else
            {
                parentList[idx] = sibling;
                parentList[siblingIdx] = selected;
            }
            moved = true;
        }
        else if (parentItem != null)
        {
            var grandCtx = FindParentContext(_trayConfig.Items, parentItem);
            if (grandCtx != null)
            {
                var grandList = grandCtx.Value.Container;
                int pIdx = grandList.IndexOf(parentItem);
                if (pIdx >= 0)
                {
                    parentList.RemoveAt(idx);
                    grandList.Insert(moveUp ? pIdx : pIdx + 1, selected);
                    moved = true;
                }
            }
        }

        if (moved)
        {
            SaveTrayMenuJson();
            RebuildTrayContextMenu();
            CustomTrayMenuItemsList.SelectedItem = selected;
            CustomTrayMenuItemsList.ScrollIntoView(selected);
        }
    }

    private void AddSeparatorTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sep = new TrayMenuItemDefinition { Id = "sep_" + Guid.NewGuid().ToString("N")[..6], IsSeparator = true };
        _trayConfig.Items.Add(sep);

        SaveTrayMenuJson();
        RebuildTrayContextMenu();
        CustomTrayMenuItemsList.SelectedItem = sep;
        CustomTrayMenuItemsList.ScrollIntoView(sep);
        Log("Added tray menu separator.");
    }

    private void TestRunTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selected = CustomTrayMenuItemsList.SelectedItem as TrayMenuItemDefinition;
        if (selected != null && !selected.IsSeparator)
        {
            string adminBadge = selected.SilentElevate ? " [Silent Elevated]" : (selected.RunAsAdmin ? " [Elevated]" : "");
            Log($"[Test Run] Executing '{selected.Title}'{adminBadge} -> {selected.Command} {selected.Arguments}");
            ExecuteCommand(selected.Command, selected.Arguments, selected.WorkingDirectory, selected.RunHidden, selected.RunAsAdmin, selected.SilentElevate, $"tray_{selected.Id}");
        }
    }

    private void ChangeDoubleClickAction_Click(object sender, RoutedEventArgs e)
    {
        var currentDef = new TrayMenuItemDefinition
        {
            Title = "Tray Double-Click Action",
            Command = string.IsNullOrWhiteSpace(_trayConfig.DoubleClickCommand) ? "native:open" : _trayConfig.DoubleClickCommand,
            Arguments = _trayConfig.DoubleClickArguments ?? "",
            RunAsAdmin = _trayConfig.DoubleClickRunAsAdmin
        };
        var dlg = new TrayItemDialog(currentDef) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            string cmd = dlg.Item.Command.Trim();
            if (cmd.Equals("native:open", StringComparison.OrdinalIgnoreCase) || cmd.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                cmd = "";
            }
            _trayConfig.DoubleClickCommand = cmd;
            _trayConfig.DoubleClickArguments = dlg.Item.Arguments.Trim();
            _trayConfig.DoubleClickRunAsAdmin = dlg.Item.RunAsAdmin;
            SaveTrayMenuJson();
            SaveSettings();
            RebuildTrayContextMenu();
            Log($"[Settings] Updated tray double-click action to: {(string.IsNullOrEmpty(cmd) ? "Open Window (Default)" : cmd)}");
            SetStatus($"Double-click set: {(string.IsNullOrEmpty(cmd) ? "Open Window (Default)" : cmd)}");
        }
    }

    private void ResetDoubleClickAction_Click(object sender, RoutedEventArgs e)
    {
        _trayConfig.DoubleClickCommand = "native:open";
        _trayConfig.DoubleClickArguments = "";
        _trayConfig.DoubleClickRunAsAdmin = false;
        SaveTrayMenuJson();
        SaveSettings();
        RebuildTrayContextMenu();
        Log("[Settings] Reset tray double-click action to Open Window (Default).");
        SetStatus("Double-click action reset to Open Window (Default).");
    }

    #endregion

    #region Task Editing & File Helpers

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TaskEditDialog();
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
            _tasks.Add(dlg.Task);
            SaveSettings();
            RefreshCounters();
            Log($"Added automation task: {dlg.Task.Name}");
            SetStatus($"Added: {dlg.Task.Name}");
        }
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        var selected = TasksList.SelectedItem as ScriptTask;
        if (selected == null)
        {
            SetStatus("Select a task to edit.");
            return;
        }
        var dlg = new TaskEditDialog(selected);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
            selected.Refresh();
            SaveSettings();
            Log($"Updated automation task: {selected.Name}");
            SetStatus($"Updated: {selected.Name}");
        }
    }

    private void EditScriptFile_Click(object sender, RoutedEventArgs e)
    {
        var selected = TasksList.SelectedItem as ScriptTask;
        if (selected == null)
        {
            SetStatus("Select a task first.");
            return;
        }
        if (File.Exists(selected.ScriptPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{selected.ScriptPath}\"",
                    UseShellExecute = true
                });
                Log($"Opened script in Notepad: {selected.ScriptPath}");
            }
            catch (Exception ex)
            {
                Log($"Could not open script in Notepad: {ex.Message}");
            }
        }
        else
        {
            SetStatus("Script file not found on disk.");
        }
    }

    private void TasksList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TasksList.SelectedItem is ScriptTask)
        {
            EditTask_Click(sender, e);
        }
    }

    private void EnsureDefaultTasks()
    {
        try
        {
            Directory.CreateDirectory(ScriptsDir);

            // Copy all bundled scripts from app directory to user scripts directory
            string bundledDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
            if (Directory.Exists(bundledDir))
            {
                foreach (var file in Directory.GetFiles(bundledDir))
                {
                    string fileName = System.IO.Path.GetFileName(file);
                    if (string.Equals(fileName, "Setup-xTaskForgeScripts.ps1", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string dest = System.IO.Path.Combine(ScriptsDir, fileName);
                    if (!File.Exists(dest))
                    {
                        try { File.Copy(file, dest, false); } catch { }
                    }
                }
            }

            // Audio tray monitoring is built into xTaskForge; drop the legacy no-op monitor task.
            foreach (var legacy in _tasks.Where(t => t.Id == "audio_tray_monitor" || t.ScriptPath.EndsWith("AudioTrayMonitor.ps1", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _tasks.Remove(legacy);
                SaveSettings();
            }

            string cycleScript = System.IO.Path.Combine(ScriptsDir, "CycleAudioOutput.ps1");
            EnsureCycleAudioScriptExists(cycleScript);

            string exampleScript = System.IO.Path.Combine(ScriptsDir, "Example.ps1");
            EnsureExampleScriptExists(exampleScript);
        }
        catch { }
    }

    private static void EnsureExampleScriptExists(string path)
    {
        if (File.Exists(path)) return;
        string defaultScript = @"param([string]$Mode = 'Check')
$log = Join-Path $env:LOCALAPPDATA 'xTaskForge\example.log'
""[{0}] Example script ran. Mode={1}"" -f (Get-Date -Format o), $Mode | Add-Content -LiteralPath $log -Encoding utf8
";
        try { File.WriteAllText(path, defaultScript, Encoding.UTF8); } catch { }
    }

    private static void EnsureCycleAudioScriptExists(string path)
    {
        if (File.Exists(path)) return;

        // 1. Try manifest embedded resource
        try
        {
            var asm = typeof(MainWindow).Assembly;
            using var stream = asm.GetManifestResourceStream("xTaskForge.scripts.CycleAudioOutput.ps1")
                            ?? asm.GetManifestResourceStream("CycleAudioOutput.ps1");
            if (stream != null)
            {
                using var fs = File.Create(path);
                stream.CopyTo(fs);
                return;
            }
        }
        catch { }

        // 2. Try output directory bundled scripts
        string bundled = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "CycleAudioOutput.ps1");
        if (File.Exists(bundled))
        {
            try { File.Copy(bundled, path, true); return; } catch { }
        }

        // 3. Try repo scripts folder
        string repoScripts = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scripts", "CycleAudioOutput.ps1");
        if (File.Exists(repoScripts))
        {
            try { File.Copy(repoScripts, path, true); return; } catch { }
        }

        // 4. Default fallback script
        string defaultScript = @"<#
.SYNOPSIS
    Cycle Audio Output Device script for xTaskForge
.DESCRIPTION
    Cycles through enabled Windows audio output devices via xTaskForge local Windows Named Pipe IPC.
#>

$ErrorActionPreference = 'Stop'

try {
    Write-Output ""[$(Get-Date -Format 'HH:mm:ss')] Requesting audio output device cycle via local IPC...""
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(""."", ""xTaskForgePipe"", [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(2000)
    $writer = New-Object System.IO.StreamWriter($pipe, [System.Text.Encoding]::UTF8)
    $reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
    $writer.WriteLine('{""command"":""audio.cycle""}')
    $writer.Flush()
    $responseJson = $reader.ReadLine()
    $pipe.Close()

    if ($responseJson) {
        $res = $responseJson | ConvertFrom-Json
        if ($res.Success) {
            Write-Output ""[$(Get-Date -Format 'HH:mm:ss')] Successfully switched audio output: $($res.Data.deviceName)""
        } else {
            Write-Output ""[ERROR] Audio cycle failed: $($res.Error)""
        }
    }
}
catch {
    Write-Output ""[ERROR] Failed to communicate with xTaskForge IPC Pipe: $($_.Exception.Message)""
}
";
        try { File.WriteAllText(path, defaultScript, Encoding.UTF8); } catch { }
    }

    #endregion
}