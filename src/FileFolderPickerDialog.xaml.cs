using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace xTaskForge;

public sealed class FileFolderPickerItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public string FullPath { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public string AttributesString { get; set; } = "";
    public string LastModifiedString { get; set; } = "";

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }
    }

    public string TypeBadgeText => IsFolder ? "FOLDER" : "FILE";
    public MediaBrush TypeBadgeBg => IsFolder ? new SolidColorBrush(MediaColor.FromRgb(40, 56, 84)) : new SolidColorBrush(MediaColor.FromRgb(32, 40, 52));
    public MediaBrush TypeBadgeFg => IsFolder ? new SolidColorBrush(MediaColor.FromRgb(140, 180, 240)) : new SolidColorBrush(MediaColor.FromRgb(180, 190, 205));
    public string IconGlyph => IsFolder ? "📁" : "📄";
    public MediaBrush IconColor => IsFolder ? new SolidColorBrush(MediaColor.FromRgb(245, 195, 80)) : new SolidColorBrush(MediaColor.FromRgb(160, 175, 195));
    public FontWeight NameWeight => IsFolder ? FontWeights.SemiBold : FontWeights.Normal;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class FileFolderPickerDialog : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private string _currentDirectory = "";
    private readonly HashSet<string> _globallySelectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<FileFolderPickerItem> _currentItems = [];

    public List<string> SelectedPaths => _globallySelectedPaths.ToList();
    public bool AddAsSuperHidden { get; private set; } = true;

    public FileFolderPickerDialog(string? initialDirectory = null)
    {
        InitializeComponent();
        FileFolderListView.ItemsSource = _currentItems;

        string initial = initialDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(initial))
            initial = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        NavigateTo(initial);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var helper = new WindowInteropHelper(this);
            int useDarkMode = 1;
            if (DwmSetWindowAttribute(helper.Handle, 20, ref useDarkMode, sizeof(int)) != 0)
                DwmSetWindowAttribute(helper.Handle, 19, ref useDarkMode, sizeof(int));
        }
        catch { }
    }

    public void PreselectPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            _globallySelectedPaths.Add(p);
        UpdateCounters();
        SyncCurrentViewSelection();
    }

    private void NavigateTo(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                System.Windows.MessageBox.Show(this, $"Directory does not exist:\n{path}", "Directory Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentDirectory = System.IO.Path.GetFullPath(path);
            CurrentPathBox.Text = _currentDirectory;
            LoadDirectoryContents();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Could not open directory: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadDirectoryContents()
    {
        _currentItems.Clear();

        try
        {
            var dirInfo = new DirectoryInfo(_currentDirectory);

            // Directories first
            foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                var isChecked = _globallySelectedPaths.Contains(dir.FullName);
                _currentItems.Add(new FileFolderPickerItem
                {
                    FullPath = dir.FullName,
                    Name = dir.Name,
                    IsFolder = true,
                    IsChecked = isChecked,
                    AttributesString = FormatAttributes(dir.Attributes),
                    LastModifiedString = dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }

            // Files next
            foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                var isChecked = _globallySelectedPaths.Contains(file.FullName);
                _currentItems.Add(new FileFolderPickerItem
                {
                    FullPath = file.FullName,
                    Name = file.Name,
                    IsFolder = false,
                    IsChecked = isChecked,
                    AttributesString = FormatAttributes(file.Attributes),
                    LastModifiedString = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, "Access denied to this folder.", "Permission Denied",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Error reading directory: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateCounters();
    }

    private static string FormatAttributes(FileAttributes attrs)
    {
        var parts = new List<string>();
        if ((attrs & FileAttributes.Hidden) != 0) parts.Add("Hidden");
        if ((attrs & FileAttributes.System) != 0) parts.Add("System");
        if ((attrs & FileAttributes.ReadOnly) != 0) parts.Add("ReadOnly");
        return parts.Count > 0 ? string.Join(", ", parts) : "Normal";
    }

    private void SyncCurrentViewSelection()
    {
        foreach (var item in _currentItems)
        {
            item.IsChecked = _globallySelectedPaths.Contains(item.FullPath);
        }
    }

    private void ItemCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { DataContext: FileFolderPickerItem item })
        {
            if (item.IsChecked)
                _globallySelectedPaths.Add(item.FullPath);
            else
                _globallySelectedPaths.Remove(item.FullPath);

            UpdateCounters();
        }
    }

    private void UpdateCounters()
    {
        int count = _globallySelectedPaths.Count;
        SelectedCountText.Text = $"Selected total: {count} item(s)";
        AddButton.Content = $"👻 Add as Super-Hidden ({count})";
        AddHiddenButton.Content = $"👁 Add as Hidden ({count})";
    }

    private void FileFolderListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileFolderListView.SelectedItem is FileFolderPickerItem { IsFolder: true } item)
        {
            NavigateTo(item.FullPath);
        }
    }

    private void FileFolderListView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            ToggleHighlightedRows();
            e.Handled = true;
        }
    }

    private void ToggleHighlighted_Click(object sender, RoutedEventArgs e) => ToggleHighlightedRows();

    private void ToggleHighlightedRows()
    {
        var selected = FileFolderListView.SelectedItems.Cast<FileFolderPickerItem>().ToList();
        if (selected.Count == 0) return;

        bool targetState = selected.Any(i => !i.IsChecked);
        foreach (var item in selected)
        {
            item.IsChecked = targetState;
            if (targetState)
                _globallySelectedPaths.Add(item.FullPath);
            else
                _globallySelectedPaths.Remove(item.FullPath);
        }
        UpdateCounters();
    }

    private void FileFolderListView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FileFolderListView.View is System.Windows.Controls.GridView gv && gv.Columns.Count >= 5)
        {
            double available = FileFolderListView.ActualWidth - 35;
            double fixedWidth = gv.Columns[0].ActualWidth + gv.Columns[1].ActualWidth + gv.Columns[3].ActualWidth + gv.Columns[4].ActualWidth;
            double remaining = available - fixedWidth;
            if (remaining > 180)
                gv.Columns[2].Width = remaining;
        }
    }

    private void GoUp_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentDirectory);
        if (parent != null && parent.Exists)
            NavigateTo(parent.FullName);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadDirectoryContents();

    private void CurrentPathBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            NavigateTo(CurrentPathBox.Text.Trim());
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = _currentDirectory,
            Description = "Choose base folder to browse"
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            NavigateTo(dialog.SelectedPath);
    }

    private void JumpUserProfile_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private void JumpDesktop_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

    private void JumpDocuments_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    private void JumpDriveC_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(@"C:\");

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _currentItems)
        {
            item.IsChecked = true;
            _globallySelectedPaths.Add(item.FullPath);
        }
        UpdateCounters();
    }

    private void SelectAllFolders_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _currentItems.Where(i => i.IsFolder))
        {
            item.IsChecked = true;
            _globallySelectedPaths.Add(item.FullPath);
        }
        UpdateCounters();
    }

    private void SelectAllFiles_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _currentItems.Where(i => !i.IsFolder))
        {
            item.IsChecked = true;
            _globallySelectedPaths.Add(item.FullPath);
        }
        UpdateCounters();
    }

    private void SelectDotfiles_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _currentItems.Where(i => i.Name.StartsWith('.')))
        {
            item.IsChecked = true;
            _globallySelectedPaths.Add(item.FullPath);
        }
        UpdateCounters();
    }

    private void ClearInView_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _currentItems)
        {
            item.IsChecked = false;
            _globallySelectedPaths.Remove(item.FullPath);
        }
        UpdateCounters();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddHidden_Click(object sender, RoutedEventArgs e)
    {
        AddAsSuperHidden = false;
        DialogResult = true;
        Close();
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        AddAsSuperHidden = true;
        DialogResult = true;
        Close();
    }
}
