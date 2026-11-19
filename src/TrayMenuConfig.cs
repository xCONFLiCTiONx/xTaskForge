using System.ComponentModel;
using System.Text.Json.Serialization;

namespace xTaskForge;

public sealed class TrayMenuItemDefinition : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _title = "Menu Item";
    private string _command = "";
    private string _arguments = "";
    private bool _checked;
    private string _icon = "";
    private bool _isSeparator;
    private List<TrayMenuItemDefinition>? _items;
    private int _indentLevel;

    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(nameof(Id)); } }
    }

    [JsonPropertyName("title")]
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    [JsonPropertyName("command")]
    public string Command
    {
        get => _command;
        set
        {
            if (_command != value)
            {
                _command = value;
                OnPropertyChanged(nameof(Command));
                OnPropertyChanged(nameof(DisplayCommand));
            }
        }
    }

    [JsonPropertyName("arguments")]
    public string Arguments
    {
        get => _arguments;
        set
        {
            if (_arguments != value)
            {
                _arguments = value;
                OnPropertyChanged(nameof(Arguments));
                OnPropertyChanged(nameof(DisplayArguments));
            }
        }
    }

    [JsonPropertyName("checked")]
    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; OnPropertyChanged(nameof(Checked)); } }
    }

    [JsonPropertyName("icon")]
    public string Icon
    {
        get => _icon;
        set { if (_icon != value) { _icon = value; OnPropertyChanged(nameof(Icon)); } }
    }

    [JsonPropertyName("isSeparator")]
    public bool IsSeparator
    {
        get => _isSeparator;
        set
        {
            if (_isSeparator != value)
            {
                _isSeparator = value;
                OnPropertyChanged(nameof(IsSeparator));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplayCommand));
                OnPropertyChanged(nameof(DisplayArguments));
                OnPropertyChanged(nameof(DisplayAdmin));
            }
        }
    }

    /// <summary>Condition that must be true for the item to appear in the tray menu (e.g. "pip.active", "!pip.active"). Blank = always shown.</summary>
    [JsonPropertyName("visibleWhen")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VisibleWhen { get; set; }

    /// <summary>Condition that switches the item into its alternate state (e.g. "pip.paused").</summary>
    [JsonPropertyName("toggledWhen")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToggledWhen { get; set; }

    [JsonPropertyName("toggledTitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToggledTitle { get; set; }

    [JsonPropertyName("toggledCommand")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToggledCommand { get; set; }

    [JsonPropertyName("toggledArguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToggledArguments { get; set; }

    private string? _workingDirectory;
    private bool _runHidden;
    private bool _runAsAdmin;
    private bool _silentElevate;

    [JsonPropertyName("workingDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set { if (_workingDirectory != value) { _workingDirectory = value; OnPropertyChanged(nameof(WorkingDirectory)); } }
    }

    [JsonPropertyName("runHidden")]
    public bool RunHidden
    {
        get => _runHidden;
        set { if (_runHidden != value) { _runHidden = value; OnPropertyChanged(nameof(RunHidden)); } }
    }

    [JsonPropertyName("runAsAdmin")]
    public bool RunAsAdmin
    {
        get => _runAsAdmin;
        set
        {
            if (_runAsAdmin != value)
            {
                _runAsAdmin = value;
                OnPropertyChanged(nameof(RunAsAdmin));
                OnPropertyChanged(nameof(DisplayAdmin));
            }
        }
    }

    [JsonPropertyName("silentElevate")]
    public bool SilentElevate
    {
        get => _silentElevate;
        set
        {
            if (_silentElevate != value)
            {
                _silentElevate = value;
                OnPropertyChanged(nameof(SilentElevate));
                OnPropertyChanged(nameof(DisplayAdmin));
            }
        }
    }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TrayMenuItemDefinition>? Items
    {
        get => _items;
        set
        {
            if (_items != value)
            {
                _items = value;
                OnPropertyChanged(nameof(Items));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(DisplayAdmin));
            }
        }
    }

    [JsonIgnore]
    public bool HasItems => Items != null;

    [JsonIgnore]
    public int IndentLevel
    {
        get => _indentLevel;
        set
        {
            if (_indentLevel != value)
            {
                _indentLevel = value;
                OnPropertyChanged(nameof(IndentLevel));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (IsSeparator) return "──────── (Separator) ────────";
            string prefix = IndentLevel > 0 ? new string(' ', IndentLevel * 3) + "↳ " : "";
            if (HasItems) return $"{prefix}📂 {Title} [Submenu]";
            return $"{prefix}{Title}";
        }
    }

    [JsonIgnore]
    public string DisplayCommand => IsSeparator ? "" : (HasItems ? "(Submenu)" : Command);

    [JsonIgnore]
    public string DisplayArguments => IsSeparator ? "" : (HasItems ? "" : Arguments);

    [JsonIgnore]
    public string DisplayAdmin => IsSeparator ? "" : (HasItems ? "" : (SilentElevate ? "⚡ Silent" : (RunAsAdmin ? "🛡️ Yes" : "No")));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}

public sealed class TrayConfiguration
{
    [JsonPropertyName("tooltip")]
    public string Tooltip { get; set; } = "xTaskForge - Automation & System Utility";

    [JsonPropertyName("currentIcon")]
    public string CurrentIcon { get; set; } = "icon1.ico";

    [JsonPropertyName("doubleClickCommand")]
    public string DoubleClickCommand { get; set; } = "native:open";

    [JsonPropertyName("doubleClickArguments")]
    public string DoubleClickArguments { get; set; } = "";

    [JsonPropertyName("doubleClickRunAsAdmin")]
    public bool DoubleClickRunAsAdmin { get; set; }

    [JsonPropertyName("items")]
    public List<TrayMenuItemDefinition> Items { get; set; } = [];

    public static List<TrayMenuItemDefinition> FlattenItems(IEnumerable<TrayMenuItemDefinition> items, int level = 0)
    {
        var list = new List<TrayMenuItemDefinition>();
        foreach (var item in items)
        {
            item.IndentLevel = level;
            list.Add(item);
            if (item.Items != null)
            {
                list.AddRange(FlattenItems(item.Items, level + 1));
            }
        }
        return list;
    }
}
