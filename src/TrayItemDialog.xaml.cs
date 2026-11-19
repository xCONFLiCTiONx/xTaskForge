using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace xTaskForge;

public partial class TrayItemDialog : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public TrayMenuItemDefinition Item { get; }
    public bool IsNew { get; }

    public TrayItemDialog(TrayMenuItemDefinition? existingItem = null)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 20;

        if (existingItem != null)
        {
            Item = existingItem;
            IsNew = false;
            DialogTitleText.Text = "Edit Tray Menu Item";
            TitleBox.Text = Item.Title;
            CommandBox.Text = Item.Command;
            ArgumentsBox.Text = Item.Arguments;
            IconBox.Text = Item.Icon;
            IsSeparatorCheckBox.IsChecked = Item.IsSeparator;
            IsSubmenuCheckBox.IsChecked = Item.Items != null;
            VisibleWhenBox.Text = Item.VisibleWhen ?? "";
            ToggledWhenBox.Text = Item.ToggledWhen ?? "";
            ToggledTitleBox.Text = Item.ToggledTitle ?? "";
            ToggledCommandBox.Text = Item.ToggledCommand ?? "";
            ToggledArgumentsBox.Text = Item.ToggledArguments ?? "";
            WorkingDirBox.Text = Item.WorkingDirectory ?? "";
            RunHiddenCheckBox.IsChecked = Item.RunHidden;
            RunAsAdminCheckBox.IsChecked = Item.RunAsAdmin;
            SilentElevateCheckBox.IsChecked = Item.SilentElevate;
        }
        else
        {
            Item = new TrayMenuItemDefinition();
            IsNew = true;
            DialogTitleText.Text = "Add Tray Menu Item";
            TitleBox.Text = "";
            CommandBox.Text = "";
            ArgumentsBox.Text = "";
            IconBox.Text = "";
            WorkingDirBox.Text = "";
            RunHiddenCheckBox.IsChecked = false;
            RunAsAdminCheckBox.IsChecked = false;
            SilentElevateCheckBox.IsChecked = false;
            IsSeparatorCheckBox.IsChecked = false;
            IsSubmenuCheckBox.IsChecked = false;
        }

        UpdateSeparatorUi();
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

    private void IsSeparator_Changed(object sender, RoutedEventArgs e) => UpdateSeparatorUi();

    private void UpdateSeparatorUi()
    {
        if (IsSubmenuCheckBox == null) return;
        bool isSep = IsSeparatorCheckBox.IsChecked == true;
        bool isSub = IsSubmenuCheckBox.IsChecked == true && !isSep;
        IsSubmenuCheckBox.IsEnabled = !isSep;
        DynamicSection.IsEnabled = !isSep;
        DynamicSection.Opacity = isSep ? 0.4 : 1.0;
        TitleSection.IsEnabled = !isSep;
        CommandSection.IsEnabled = !isSep && !isSub;
        ArgumentsSection.IsEnabled = !isSep && !isSub;
        CommandSection.Opacity = (isSep || isSub) ? 0.4 : 1.0;
        ArgumentsSection.Opacity = (isSep || isSub) ? 0.4 : 1.0;
        IconSection.IsEnabled = !isSep;
        TitleSection.Opacity = isSep ? 0.4 : 1.0;
        IconSection.Opacity = isSep ? 0.4 : 1.0;
        RunHiddenCheckBox.IsEnabled = !isSep && !isSub;
        RunHiddenCheckBox.Opacity = (isSep || isSub) ? 0.4 : 1.0;
        RunAsAdminCheckBox.IsEnabled = !isSep && !isSub;
        RunAsAdminCheckBox.Opacity = (isSep || isSub) ? 0.4 : 1.0;
        SilentElevateCheckBox.IsEnabled = !isSep && !isSub;
        SilentElevateCheckBox.Opacity = (isSep || isSub) ? 0.4 : 1.0;
    }

    private void IconHeadphones_Click(object sender, RoutedEventArgs e) => IconBox.Text = "headphones.ico";
    private void IconBluetooth_Click(object sender, RoutedEventArgs e) => IconBox.Text = "bluetooth.ico";
    private void IconSpeakers_Click(object sender, RoutedEventArgs e) => IconBox.Text = "speakers.ico";
    private void IconAuto_Click(object sender, RoutedEventArgs e) => IconBox.Text = "";

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            string? selected = IconPickerHelper.PickIcon(helper.Handle, IconBox.Text);
            if (selected != null)
            {
                IconBox.Text = selected;
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not open Windows icon picker: {ex.Message}", "Icon Picker Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PresetPipOverlay_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "📌 PiP Click-Through Overlay";
        CommandBox.Text = "native:pip_clickthrough";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void PresetPipPause_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "⏸️ Pause PiP Click-Through (10s)";
        CommandBox.Text = "native:pip_pause";
        ArgumentsBox.Text = "10";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void PresetNextAudio_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "🔊 Next audio device";
        CommandBox.Text = "native:cycle_audio_device";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void PresetOpen_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "Open xTaskForge";
        CommandBox.Text = "native:open";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void Preset4k_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "🖥️ 3840 x 2160 (Native)";
        CommandBox.Text = "native:res_4k";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void Preset1440_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "🖥️ 2560 x 1440 (Native)";
        CommandBox.Text = "native:res_1440";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void Preset1080_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "🖥️ 1920 x 1080 (Native)";
        CommandBox.Text = "native:res_1080";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void PresetHdr_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "✨ Toggle HDR";
        CommandBox.Text = "native:toggle_hdr";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void PresetKeepAwake_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "☕ Keep PC Awake";
        CommandBox.Text = "native:prevent_sleep";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void PresetOpenScripts_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "📁 Open Scripts Folder";
        CommandBox.Text = "native:open_scripts";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void PresetExplorer_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "📁 File Explorer";
        CommandBox.Text = "explorer.exe";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void PresetNotepad_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = "📝 Notepad";
        CommandBox.Text = "notepad.exe";
        ArgumentsBox.Text = "";
        IsSeparatorCheckBox.IsChecked = false;
    }

    private void BrowseCommand_Click(object sender, RoutedEventArgs e)
    {
        string defaultScriptsDir = MainWindow.ScriptsDir;
        if (!Directory.Exists(defaultScriptsDir))
        {
            try { Directory.CreateDirectory(defaultScriptsDir); } catch { }
        }

        string initialDir = defaultScriptsDir;
        if (!string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            string? dir = Path.GetDirectoryName(CommandBox.Text);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                initialDir = dir;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose command executable or script",
            Filter = "All executable files (*.exe;*.ps1;*.bat;*.cmd)|*.exe;*.ps1;*.bat;*.cmd|PowerShell scripts (*.ps1)|*.ps1|Batch scripts (*.bat;*.cmd)|*.bat;*.cmd|Executables (*.exe)|*.exe|All files (*.*)|*.*",
            InitialDirectory = initialDir,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            CommandBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose directory argument",
            SelectedPath = Directory.Exists(ArgumentsBox.Text) ? ArgumentsBox.Text : ""
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ArgumentsBox.Text = dialog.SelectedPath;
        }
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose argument file",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            ArgumentsBox.Text = dialog.FileName;
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        bool isSep = IsSeparatorCheckBox.IsChecked == true;
        if (!isSep && string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Please enter a title for the menu item.", "Title Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isSub = IsSubmenuCheckBox.IsChecked == true && !isSep;
        if (!isSub && Item.Items is { Count: > 0 })
        {
            System.Windows.MessageBox.Show(this, "This submenu still contains items. Remove or move them before turning it back into a normal item.", "Submenu Not Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        Item.VisibleWhen = isSep ? null : N(VisibleWhenBox.Text);
        Item.ToggledWhen = isSep ? null : N(ToggledWhenBox.Text);
        Item.ToggledTitle = isSep ? null : N(ToggledTitleBox.Text);
        Item.ToggledCommand = isSep ? null : N(ToggledCommandBox.Text);
        Item.ToggledArguments = isSep ? null : N(ToggledArgumentsBox.Text);

        Item.IsSeparator = isSep;
        Item.Items = isSub ? (Item.Items ?? new List<TrayMenuItemDefinition>()) : null;
        Item.WorkingDirectory = (isSep || isSub) ? null : N(WorkingDirBox.Text);
        Item.RunHidden = RunHiddenCheckBox.IsChecked == true;
        Item.RunAsAdmin = RunAsAdminCheckBox.IsChecked == true;
        Item.SilentElevate = SilentElevateCheckBox.IsChecked == true && !isSep && !isSub;
        if (isSub)
        {
            Item.Title = TitleBox.Text.Trim();
            Item.Command = "";
            Item.Arguments = "";
            Item.Icon = IconBox.Text.Trim();
        }
        else if (isSep)
        {
            Item.Title = "";
            Item.Command = "";
            Item.Arguments = "";
            Item.Icon = "";
        }
        else
        {
            Item.Title = TitleBox.Text.Trim();
            Item.Command = CommandBox.Text.Trim();
            Item.Arguments = ArgumentsBox.Text.Trim();
            Item.Icon = IconBox.Text.Trim();

            if (Item.SilentElevate && !Item.Command.StartsWith("native:", StringComparison.OrdinalIgnoreCase))
            {
                if (!SilentElevationHelper.RegisterOrUpdateTask(
                    $"tray_{Item.Id}", Item.Command, Item.Arguments, null, Item.WorkingDirectory, Item.RunHidden, out string? err))
                {
                    System.Windows.MessageBox.Show(this, $"Could not register Task Scheduler entry for silent elevation:\n{err}", "Silent Elevation Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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
