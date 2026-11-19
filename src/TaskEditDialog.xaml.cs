using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace xTaskForge;

public partial class TaskEditDialog : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public ScriptTask Task { get; }
    public bool IsNew { get; }

    public TaskEditDialog(ScriptTask? existingTask = null)
    {
        InitializeComponent();

        if (existingTask != null)
        {
            Task = existingTask;
            IsNew = false;
            DialogTitleText.Text = "Edit Automation Task";
            NameBox.Text = Task.Name;
            ScriptPathBox.Text = Task.ScriptPath;
            LauncherBox.Text = Task.Launcher;
            ArgumentsBox.Text = Task.Arguments;
            WorkingDirBox.Text = Task.WorkingDirectory;
            RunHiddenCheckBox.IsChecked = Task.RunHidden;
            RunAsAdminCheckBox.IsChecked = Task.RunAsAdmin;
            SilentElevateCheckBox.IsChecked = Task.SilentElevate;
            AutoStartCheckBox.IsChecked = Task.AutoStart;
            LoopCheckBox.IsChecked = Task.Loop;
            LoopDelayBox.Text = Task.LoopDelayMs.ToString();
        }
        else
        {
            Task = new ScriptTask();
            IsNew = true;
            DialogTitleText.Text = "Add Automation Task";
            NameBox.Text = "";
            ScriptPathBox.Text = "";
            LauncherBox.Text = "";
            ArgumentsBox.Text = "";
            WorkingDirBox.Text = "";
            RunHiddenCheckBox.IsChecked = false;
            RunAsAdminCheckBox.IsChecked = false;
            SilentElevateCheckBox.IsChecked = false;
            AutoStartCheckBox.IsChecked = false;
            LoopCheckBox.IsChecked = false;
            LoopDelayBox.Text = "1000";
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var helper = new WindowInteropHelper(this);
            int useDarkMode = 1;
            DwmSetWindowAttribute(helper.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
        }
        catch { }
    }

    private void BrowseScript_Click(object sender, RoutedEventArgs e)
    {
        string defaultScriptsDir = MainWindow.ScriptsDir;
        if (!Directory.Exists(defaultScriptsDir))
        {
            try { Directory.CreateDirectory(defaultScriptsDir); } catch { }
        }

        string initialDir = defaultScriptsDir;
        if (!string.IsNullOrWhiteSpace(ScriptPathBox.Text))
        {
            string? dir = Path.GetDirectoryName(ScriptPathBox.Text);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                initialDir = dir;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose script or executable",
            Filter = "Supported scripts & executables (*.ps1;*.bat;*.cmd;*.exe)|*.ps1;*.bat;*.cmd;*.exe|PowerShell scripts (*.ps1)|*.ps1|Batch & CMD (*.bat;*.cmd)|*.bat;*.cmd|Executables (*.exe)|*.exe|All files (*.*)|*.*",
            InitialDirectory = initialDir,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            ScriptPathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void BrowseLauncher_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose custom launcher executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            LauncherBox.Text = dialog.FileName;
        }
    }

    private void BrowseWorkingDir_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose working directory",
            SelectedPath = Directory.Exists(WorkingDirBox.Text) ? WorkingDirBox.Text : ""
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            WorkingDirBox.Text = dialog.SelectedPath;
        }
    }

    private void OpenScriptFile_Click(object sender, RoutedEventArgs e)
    {
        string path = ScriptPathBox.Text.Trim();
        if (File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Could not open script in Notepad: {ex.Message}", "Open Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            System.Windows.MessageBox.Show(this, "Script file does not exist at specified path.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        string script = ScriptPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(this, "Please enter a task name.", "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            System.Windows.MessageBox.Show(this, "Please specify a script path or target executable.", "Target Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Task.Name = name;
        Task.ScriptPath = script;
        Task.Launcher = LauncherBox.Text.Trim();
        Task.Arguments = ArgumentsBox.Text.Trim();
        Task.WorkingDirectory = WorkingDirBox.Text.Trim();
        Task.RunHidden = RunHiddenCheckBox.IsChecked == true;
        Task.RunAsAdmin = RunAsAdminCheckBox.IsChecked == true;
        Task.SilentElevate = SilentElevateCheckBox.IsChecked == true;
        Task.AutoStart = AutoStartCheckBox.IsChecked == true;
        Task.Loop = LoopCheckBox.IsChecked == true;
        Task.LoopDelayMs = int.TryParse(LoopDelayBox.Text.Trim(), out int delay) && delay >= 0 ? delay : 1000;

        if (Task.SilentElevate && !Task.ScriptPath.StartsWith("native:", StringComparison.OrdinalIgnoreCase))
        {
            if (!SilentElevationHelper.RegisterOrUpdateTask(
                $"task_{Task.Id}", Task.ScriptPath, Task.Arguments, Task.Launcher, Task.WorkingDirectory, Task.RunHidden, out string? err))
            {
                System.Windows.MessageBox.Show(this, $"Could not register Task Scheduler entry for silent elevation:\n{err}", "Silent Elevation Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
