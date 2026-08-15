using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using KitX.Core.Contract.Plugin;
using KitX.ToolKit.Models;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>The three structured parameter-injection source forms (Bench UX v2 §4.4).</summary>
public enum BenchParamSourceKind
{
    Payload,
    Output,
    Literal,
}

/// <summary>
/// One row of the structured parameter-injection map. It is an observable façade over a
/// <see cref="TriggerBinding.Params"/> entry; the persisted wire format stays
/// <c>$payload.x</c> / <c>$output.x</c> / literal — only the interaction shape changes.
/// </summary>
public sealed class BenchParamRowVM : ReactiveObject
{
    private readonly TriggerBinding _binding;
    private readonly Action _onEdit;
    private readonly bool _completionBinding;
    private string _name = string.Empty;
    private BenchParamSourceKind _sourceKind;
    private string _path = string.Empty;
    private string _literalValue = string.Empty;
    private string? _error;

    public BenchParamRowVM(TriggerBinding binding, string name, string? rawValue, bool completionBinding, Action onEdit)
    {
        _binding = binding;
        _name = name;
        _completionBinding = completionBinding;
        _onEdit = onEdit;
        (_sourceKind, _path, _literalValue) = Split(rawValue);
    }

    public string Name
    {
        get => _name;
        set
        {
            value = value?.Trim() ?? string.Empty;
            if (_name == value)
                return;

            if (string.IsNullOrWhiteSpace(value))
            {
                Error = "形参名不能为空";
                return;
            }

            if (_binding.Params.ContainsKey(value) && _name != value)
            {
                Error = "形参名已存在";
                return;
            }

            _binding.Params.Remove(_name);
            _name = value;
            _binding.Params[_name] = RawValue;
            Error = null;
            this.RaisePropertyChanged();
            _onEdit();
        }
    }

    public BenchParamSourceKind SourceKind
    {
        get => _sourceKind;
        set
        {
            if (_sourceKind == value)
                return;
            _sourceKind = value;
            Persist();
            this.RaisePropertyChanged();
            NotifyKindChanged();
            _onEdit();
        }
    }

    /// <summary>Dot-path for <c>$payload</c>/<c>$output</c> sources.</summary>
    public string Path
    {
        get => _path;
        set
        {
            value = value?.Trim() ?? string.Empty;
            if (_path == value)
                return;
            _path = value;
            Error = ValidatePath(value);
            Persist();
            this.RaisePropertyChanged();
            _onEdit();
        }
    }

    /// <summary>Literal value for the <c>Literal</c> source.</summary>
    public string LiteralValue
    {
        get => _literalValue;
        set
        {
            if (_literalValue == value)
                return;
            _literalValue = value ?? string.Empty;
            Error = null;
            Persist();
            this.RaisePropertyChanged();
            _onEdit();
        }
    }

    /// <summary>The persisted source expression (single source of truth on the model).</summary>
    public string RawValue => SourceKind switch
    {
        BenchParamSourceKind.Payload => "$payload." + _path,
        BenchParamSourceKind.Output => "$output." + _path,
        _ => _literalValue,
    };

    /// <summary>Row validation error, when any.</summary>
    public string? Error
    {
        get => _error;
        private set => this.RaiseAndSetIfChanged(ref _error, value);
    }

    /// <summary><c>$output</c> is only valid on WorkflowCompletion bindings (Bench UX v2 §4.4).</summary>
    public bool HasWarning => SourceKind == BenchParamSourceKind.Output && !_completionBinding;

    public string? Warning => HasWarning ? "完成边才可注入 $output；Spawn 绑定中此参数将不会解析" : null;

    public static IReadOnlyList<string> SourceOptions { get; } = ["$payload 路径", "$output 路径", "字面量"];

    public int SourceIndex
    {
        get => (int)SourceKind;
        set => SourceKind = (BenchParamSourceKind)value;
    }

    public bool IsPayload => SourceKind == BenchParamSourceKind.Payload;
    public bool IsOutput => SourceKind == BenchParamSourceKind.Output;
    public bool IsLiteral => SourceKind == BenchParamSourceKind.Literal;

    public void NotifyKindChanged()
    {
        this.RaisePropertyChanged(nameof(IsPayload));
        this.RaisePropertyChanged(nameof(IsOutput));
        this.RaisePropertyChanged(nameof(IsLiteral));
        this.RaisePropertyChanged(nameof(HasWarning));
        this.RaisePropertyChanged(nameof(Warning));
    }

    private void Persist()
    {
        var key = _name;
        if (string.IsNullOrWhiteSpace(key))
            return;
        _binding.Params[key] = RawValue;
    }

    private static (BenchParamSourceKind, string, string) Split(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (BenchParamSourceKind.Literal, string.Empty, string.Empty);
        if (raw.StartsWith("$payload.", StringComparison.Ordinal))
            return (BenchParamSourceKind.Payload, raw["$payload.".Length..], string.Empty);
        if (raw.StartsWith("$output.", StringComparison.Ordinal))
            return (BenchParamSourceKind.Output, raw["$output.".Length..], string.Empty);
        return (BenchParamSourceKind.Literal, string.Empty, raw);
    }

    public static string? ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "路径不能为空";
        if (path.Contains("..", StringComparison.Ordinal))
            return "路径不能包含 ..";
        if (path.Split('.').Any(segment => segment.Length == 0 || segment.Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '-'))))
            return "路径段只能包含字母、数字、_、-";
        return null;
    }
}

/// <summary>
/// A structured trigger→workflow binding row (replaces the previous raw
/// <c>name=source</c> text box with the v2 mapping table).
/// </summary>
public sealed class BenchBindingRowVM : ReactiveObject
{
    private readonly Action _onEdit;

    public BenchBindingRowVM(TriggerBinding model, ObservableCollection<string> workflowOptions, bool completionBinding, Action onEdit)
    {
        Model = model;
        WorkflowOptions = workflowOptions;
        IsCompletionBinding = completionBinding;
        _onEdit = onEdit;
        ParamRows = new ObservableCollection<BenchParamRowVM>(
            model.Params.Select(kv => new BenchParamRowVM(model, kv.Key, kv.Value, completionBinding, onEdit)));
    }

    public TriggerBinding Model { get; }

    /// <summary>Shared live workflow-id options (the canvas updates it on add/rename/delete).</summary>
    public ObservableCollection<string> WorkflowOptions { get; }

    public ObservableCollection<BenchParamRowVM> ParamRows { get; }

    public string SelectedWorkflow
    {
        get => Model.Workflow;
        set
        {
            if (Model.Workflow != value)
            {
                var oldValue = Model.Workflow;
                Model.Workflow = value ?? string.Empty;
                this.RaisePropertyChanged();
                WorkflowChanged?.Invoke(oldValue, Model.Workflow);
                _onEdit();
            }
        }
    }

    /// <summary>
    /// Raised when the row's target workflow changes, before <see cref="_onEdit"/> runs.
    /// The canvas subscribes to keep the projected edge in sync with the config.
    /// </summary>
    public event Action<string, string>? WorkflowChanged;

    /// <summary>Re-raises the workflow binding so the UI snaps back after a rejected change.</summary>
    public void NotifyWorkflowChanged() => this.RaisePropertyChanged(nameof(SelectedWorkflow));

    /// <summary>Adds one empty literal parameter row (the inspector may pre-fill it).</summary>
    public void AddParamRow()
    {
        var name = "param" + (Model.Params.Count + 1);
        while (Model.Params.ContainsKey(name))
            name += "_";
        Model.Params[name] = string.Empty;
        ParamRows.Add(new BenchParamRowVM(Model, name, string.Empty, IsCompletionBinding, _onEdit));
        _onEdit();
    }

    public void RemoveParamRow(BenchParamRowVM row)
    {
        if (row is null)
            return;
        Model.Params.Remove(row.Name);
        ParamRows.Remove(row);
        _onEdit();
    }

    private bool IsCompletionBinding { get; set; }
}

/// <summary>A free-form tag chip editor façade.</summary>
public sealed class BenchTagVM : ReactiveObject
{
    private readonly Action _onEdit;
    private string _text;

    public BenchTagVM(string text, Action onEdit)
    {
        _text = text;
        _onEdit = onEdit;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }
}

/// <summary>A plugin requirement row with local installation status (Bench UX v2 C2).</summary>
public sealed class BenchPluginRequirementVM : ReactiveObject
{
    private readonly PluginRequirement _model;
    private readonly Action _onEdit;
    private readonly Func<string, bool> _isInstalled;
    private string _name;
    private string _version;
    private string _source;

    public BenchPluginRequirementVM(PluginRequirement model, Func<string, bool> isInstalled, Action onEdit)
    {
        _model = model;
        _isInstalled = isInstalled;
        _onEdit = onEdit;
        _name = model.Name;
        _version = model.Version;
        _source = model.Source;
    }

    public PluginRequirement Model => _model;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                _model.Name = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(IsInstalled));
                this.RaisePropertyChanged(nameof(InstallStatus));
                _onEdit();
            }
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            if (_version != value)
            {
                _version = value;
                _model.Version = value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string Source
    {
        get => _source;
        set
        {
            if (_source != value)
            {
                _source = value;
                _model.Source = value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public bool IsInstalled => _isInstalled(_model.Name);

    public string InstallStatus => IsInstalled ? "已安装" : (_model.Source == "local" ? "未安装" : "远程来源");
}

/// <summary>ToolKit metadata / plugins / run-parameter inspector page (Bench UX v2 §4.3).</summary>
public sealed class BenchToolkitInspectorVM : ReactiveObject
{
    private static readonly string[] IconChoices =
        ["🛠️", "🤖", "📦", "🧰", "⚙️", "🎛️", "🔧", "🧩", "💡", "🖥️", "📡", "🧠"];

    private readonly Toolkit _toolkit;
    private readonly Action _onEdit;
    private readonly Func<string, bool> _isPluginInstalled;
    private bool _maxInstancesEnabled;
    private double? _maxInstancesValue;
    private string _newTag = string.Empty;
    private string _newPluginName = string.Empty;
    private string _newPluginVersion = string.Empty;
    private string _newPluginSource = "local";

    public BenchToolkitInspectorVM(Toolkit toolkit, IPluginService? pluginService, Action onEdit)
    {
        _toolkit = toolkit;
        _onEdit = onEdit;
        _isPluginInstalled = name =>
        {
            try
            {
                return pluginService?.GetInstalledPlugins()
                    .Any(p => string.Equals(p.PluginInfo?.Name, name, StringComparison.OrdinalIgnoreCase)) == true;
            }
            catch
            {
                return false;
            }
        };

        Tags = new ObservableCollection<BenchTagVM>(
            toolkit.Meta.Tags.Select(t => new BenchTagVM(t, onEdit)));
        Plugins = new ObservableCollection<BenchPluginRequirementVM>(
            toolkit.Plugins.Select(p => new BenchPluginRequirementVM(p, _isPluginInstalled, onEdit)));
        _maxInstancesEnabled = toolkit.MaxInstances.HasValue;
        _maxInstancesValue = toolkit.MaxInstances;
        AddTagCommand = ReactiveCommand.Create(AddTag);
        RemoveTagCommand = ReactiveCommand.Create<BenchTagVM>(RemoveTag);
        AddPluginCommand = ReactiveCommand.Create(AddPlugin);
        RemovePluginCommand = ReactiveCommand.Create<BenchPluginRequirementVM>(RemovePlugin);
        RefreshInstallStatusCommand = ReactiveCommand.Create(() =>
        {
            foreach (var plugin in Plugins)
            {
                plugin.RaisePropertyChanged(nameof(BenchPluginRequirementVM.IsInstalled));
                plugin.RaisePropertyChanged(nameof(BenchPluginRequirementVM.InstallStatus));
            }
        });
    }

    public string Name
    {
        get => _toolkit.Meta.Name;
        set
        {
            if (_toolkit.Meta.Name != value)
            {
                _toolkit.Meta.Name = value ?? string.Empty;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string Version
    {
        get => _toolkit.Meta.Version;
        set
        {
            if (_toolkit.Meta.Version != value)
            {
                _toolkit.Meta.Version = value ?? string.Empty;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string Author
    {
        get => _toolkit.Meta.Author;
        set
        {
            if (_toolkit.Meta.Author != value)
            {
                _toolkit.Meta.Author = value ?? string.Empty;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string Icon
    {
        get => _toolkit.Meta.Icon;
        set
        {
            if (_toolkit.Meta.Icon != value)
            {
                _toolkit.Meta.Icon = value ?? string.Empty;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string Description
    {
        get => _toolkit.Meta.Description;
        set
        {
            if (_toolkit.Meta.Description != value)
            {
                _toolkit.Meta.Description = value ?? string.Empty;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string MinKitXVersion
    {
        get => _toolkit.Meta.MinKitXVersion;
        set
        {
            if (_toolkit.Meta.MinKitXVersion != value)
            {
                _toolkit.Meta.MinKitXVersion = value ?? string.Empty;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public IReadOnlyList<string> IconOptions { get; } = IconChoices;

    public ObservableCollection<BenchTagVM> Tags { get; }

    public string NewTag
    {
        get => _newTag;
        set => this.RaiseAndSetIfChanged(ref _newTag, value);
    }

    public ReactiveCommand<Unit, Unit> AddTagCommand { get; }
    public ReactiveCommand<BenchTagVM, Unit> RemoveTagCommand { get; }

    public ObservableCollection<BenchPluginRequirementVM> Plugins { get; }

    public string NewPluginName
    {
        get => _newPluginName;
        set => this.RaiseAndSetIfChanged(ref _newPluginName, value);
    }

    public string NewPluginVersion
    {
        get => _newPluginVersion;
        set => this.RaiseAndSetIfChanged(ref _newPluginVersion, value);
    }

    public string NewPluginSource
    {
        get => _newPluginSource;
        set => this.RaiseAndSetIfChanged(ref _newPluginSource, value);
    }

    public ReactiveCommand<Unit, Unit> AddPluginCommand { get; }
    public ReactiveCommand<BenchPluginRequirementVM, Unit> RemovePluginCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshInstallStatusCommand { get; }

    public bool MaxInstancesEnabled
    {
        get => _maxInstancesEnabled;
        set
        {
            if (_maxInstancesEnabled != value)
            {
                _maxInstancesEnabled = value;
                _toolkit.MaxInstances = value ? (int)(_maxInstancesValue ?? 1) : null;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(MaxInstancesValue));
                _onEdit();
            }
        }
    }

    public double? MaxInstancesValue
    {
        get => _maxInstancesValue;
        set
        {
            var normal = value is { } d && d > 0 ? d : 1;
            if (_maxInstancesValue != normal)
            {
                _maxInstancesValue = normal;
                if (_maxInstancesEnabled)
                    _toolkit.MaxInstances = (int)normal;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string MaxInstancesHint => "关闭 = null（不限制）；开启后同一工具箱最多同时运行该数量的实例";

    private void AddTag()
    {
        var tag = NewTag?.Trim();
        if (string.IsNullOrWhiteSpace(tag) || _toolkit.Meta.Tags.Contains(tag))
            return;
        _toolkit.Meta.Tags.Add(tag);
        Tags.Add(new BenchTagVM(tag, _onEdit));
        NewTag = string.Empty;
        _onEdit();
    }

    private void RemoveTag(BenchTagVM? tag)
    {
        if (tag is null)
            return;
        _toolkit.Meta.Tags.Remove(tag.Text);
        Tags.Remove(tag);
        _onEdit();
    }

    private void AddPlugin()
    {
        var name = NewPluginName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        var model = new PluginRequirement
        {
            Name = name,
            Version = NewPluginVersion?.Trim() ?? string.Empty,
            Source = string.IsNullOrWhiteSpace(NewPluginSource) ? "local" : NewPluginSource.Trim(),
        };
        _toolkit.Plugins.Add(model);
        Plugins.Add(new BenchPluginRequirementVM(model, _isPluginInstalled, _onEdit));
        NewPluginName = string.Empty;
        NewPluginVersion = string.Empty;
        _onEdit();
    }

    private void RemovePlugin(BenchPluginRequirementVM? plugin)
    {
        if (plugin is null)
            return;
        _toolkit.Plugins.Remove(plugin.Model);
        Plugins.Remove(plugin);
        _onEdit();
    }
}

/// <summary>
/// Observable façade over a <see cref="UiControl"/> for the panel-node inspector:
/// identity, static text, type-specific Options and the three Bind paths.
/// </summary>
public sealed class BenchUiControlVM : ReactiveObject
{
    private readonly UiControl _model;
    private readonly Action _onEdit;
    private readonly Action<BenchUiControlVM, int> _move;

    public BenchUiControlVM(UiControl model, Action onEdit, Action<BenchUiControlVM, int> move)
    {
        _model = model;
        _onEdit = onEdit;
        _move = move;
        SelectItems = new ObservableCollection<string>(ReadSelectItems(model));
        _progressMax = ReadProgressMax(model);
    }

    public UiControl Model => _model;
    public string Type => _model.Type;
    public bool IsSelect => _model.Type == "Select";
    public bool IsProgress => _model.Type == "Progress";

    /// <summary>
    /// Re-reads every displayed field from the model and raises change notifications.
    /// Used by the canvas panel-node preview: the preview row is a separate VM instance
    /// from the inspector row, so it must be refreshed when the inspector edits the POCO.
    /// </summary>
    public void RefreshFromModel()
    {
        SelectItems.Clear();
        foreach (var item in ReadSelectItems(_model))
            SelectItems.Add(item);
        _progressMax = ReadProgressMax(_model);

        this.RaisePropertyChanged(nameof(Id));
        this.RaisePropertyChanged(nameof(Text));
        this.RaisePropertyChanged(nameof(Bind));
        this.RaisePropertyChanged(nameof(BindEnabled));
        this.RaisePropertyChanged(nameof(BindVisible));
        this.RaisePropertyChanged(nameof(SelectItems));
        this.RaisePropertyChanged(nameof(ProgressMax));
        this.RaisePropertyChanged(nameof(IsSelect));
        this.RaisePropertyChanged(nameof(IsProgress));
    }

    public string Id
    {
        get => _model.Id;
        set
        {
            if (_model.Id != value)
            {
                _model.Id = value ?? string.Empty;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string Text
    {
        get => _model.Text ?? string.Empty;
        set
        {
            if (_model.Text != value)
            {
                _model.Text = value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string Bind
    {
        get => _model.Bind ?? string.Empty;
        set
        {
            if (_model.Bind != value)
            {
                _model.Bind = string.IsNullOrWhiteSpace(value) ? null : value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string BindEnabled
    {
        get => _model.BindEnabled ?? string.Empty;
        set
        {
            if (_model.BindEnabled != value)
            {
                _model.BindEnabled = string.IsNullOrWhiteSpace(value) ? null : value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string BindVisible
    {
        get => _model.BindVisible ?? string.Empty;
        set
        {
            if (_model.BindVisible != value)
            {
                _model.BindVisible = string.IsNullOrWhiteSpace(value) ? null : value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public string BindHint => "留空 = 自动派生 {toolkitId}/panel/{Id}/{主属性}；填写时必须保持在 panel/ 命名空间内";

    public ObservableCollection<string> SelectItems { get; }

    private double _progressMax;

    public double ProgressMax
    {
        get => _progressMax;
        set
        {
            if (_progressMax != value)
            {
                _progressMax = value;
                _model.Options ??= new Dictionary<string, object?>();
                _model.Options["Max"] = value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    public void AddSelectItem()
    {
        var item = "item" + (SelectItems.Count + 1);
        SelectItems.Add(item);
        WriteSelectItems();
        _onEdit();
    }

    public void RemoveSelectItem(string? item)
    {
        if (item is null || !SelectItems.Remove(item))
            return;
        WriteSelectItems();
        _onEdit();
    }

    public void MoveUp() => _move(this, -1);
    public void MoveDown() => _move(this, +1);

    private void WriteSelectItems()
    {
        _model.Options ??= new Dictionary<string, object?>();
        _model.Options["Items"] = SelectItems.ToList();
    }

    private static List<string> ReadSelectItems(UiControl model)
    {
        if (model.Options is null || !model.Options.TryGetValue("Items", out var items) || items is null)
            return [];
        return items switch
        {
            System.Text.Json.Nodes.JsonArray arr => arr.Select(n => n?.ToString() ?? string.Empty).ToList(),
            IEnumerable<string> strings => strings.ToList(),
            _ => [],
        };
    }

    private static double ReadProgressMax(UiControl model)
    {
        if (model.Options is null || !model.Options.TryGetValue("Max", out var max) || max is null)
            return 100;
        try
        {
            return Convert.ToDouble(max, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 100;
        }
    }
}
