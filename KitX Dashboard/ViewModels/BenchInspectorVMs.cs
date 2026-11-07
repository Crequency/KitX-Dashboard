using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using KitX.Core.Contract.Plugin;
using KitX.ToolKit.Contract.Declaration;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>The two structured parameter-injection source forms a trigger binding can use.</summary>
/// <remarks>
/// There is no <c>$output</c> form. A completion edge carries topology only (design Q44): the
/// node it starts reads the predecessor's whole output packet, and the scheduler hands that
/// binding no parameters at all. Keeping an <c>$output</c> option in the editor would offer a
/// choice the compiler now rejects (<c>trigger.binding.params-on-completion</c>).
/// </remarks>
public enum BenchParamSourceKind
{
    Payload,
    Literal,
}

/// <summary>
/// One row of the structured parameter-injection map. It is an observable façade over a
/// <see cref="TriggerBinding.Params"/> entry; the persisted wire format is
/// <c>$payload.x</c> or a literal — only the interaction shape changes.
/// </summary>
public sealed class BenchParamRowVM : ReactiveObject
{
    private readonly TriggerBinding _binding;
    private readonly Action _onEdit;
    private bool _legacyOutputReference;
    private string _name = string.Empty;
    private BenchParamSourceKind _sourceKind;
    private string _path = string.Empty;
    private string _literalValue = string.Empty;
    private string? _error;

    public BenchParamRowVM(TriggerBinding binding, string name, string? rawValue, Action onEdit)
    {
        _binding = binding;
        _name = name;
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
                Error = ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorParamNameEmpty") ?? "形参名不能为空";
                return;
            }

            if (_binding.Params.ContainsKey(value) && _name != value)
            {
                Error = ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorParamNameExists") ?? "形参名已存在";
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
            // Choosing a form replaces whatever was stored, so the legacy flag stops applying.
            _legacyOutputReference = false;
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
        _ => _literalValue,
    };

    /// <summary>Row validation error, when any.</summary>
    public string? Error
    {
        get => _error;
        private set => this.RaiseAndSetIfChanged(ref _error, value);
    }

    /// <summary>
    /// A legacy <c>$output.x</c> param survives only long enough to be reported: the compiler
    /// rejects it outright (<c>trigger.binding.output-retired</c>), so the editor must not
    /// quietly rewrite it into something else either. It becomes a literal row that keeps the
    /// original text, and <see cref="ValidatePath"/> is not consulted for literals — the error
    /// surfaces from the toolkit's own diagnostic instead.
    /// </summary>
    public bool HasLegacyOutputReference => _legacyOutputReference;

    public string? Warning => HasLegacyOutputReference
        ? ViewModelBase.TranslateTextWithSuffix("Bench", "ParamWarning") ?? "完成边不再带参数；$output 已废弃，请改用 $payload 或删除该参数"
        : null;

    public static IReadOnlyList<string> SourceOptions => [
        ViewModelBase.TranslateTextWithSuffix("Bench", "SourcePayloadPath") ?? "$payload 路径",
        ViewModelBase.TranslateTextWithSuffix("Bench", "SourceLiteral") ?? "字面量",
    ];

    public int SourceIndex
    {
        get => (int)SourceKind;
        set => SourceKind = (BenchParamSourceKind)value;
    }

    public bool IsPayload => SourceKind == BenchParamSourceKind.Payload;
    public bool IsLiteral => SourceKind == BenchParamSourceKind.Literal;

    public void NotifyKindChanged()
    {
        this.RaisePropertyChanged(nameof(IsPayload));
        this.RaisePropertyChanged(nameof(IsLiteral));
        this.RaisePropertyChanged(nameof(Warning));
    }

    private void Persist()
    {
        var key = _name;
        if (string.IsNullOrWhiteSpace(key))
            return;
        _binding.Params[key] = RawValue;
    }

    private (BenchParamSourceKind, string, string) Split(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (BenchParamSourceKind.Literal, string.Empty, string.Empty);
        if (raw.StartsWith("$payload.", StringComparison.Ordinal))
            return (BenchParamSourceKind.Payload, raw["$payload.".Length..], string.Empty);
        if (raw.StartsWith("$output.", StringComparison.Ordinal))
        {
            // Pre-Q44 configs can still contain these. Keep the text verbatim so the row shows
            // what is actually stored and the compiler's rejection can be fixed by hand — a
            // silent rewrite would change the user's data behind their back.
            _legacyOutputReference = true;
            return (BenchParamSourceKind.Literal, string.Empty, raw);
        }
        return (BenchParamSourceKind.Literal, string.Empty, raw);
    }

    public static string? ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorPathEmpty") ?? "路径不能为空";
        if (path.Contains("..", StringComparison.Ordinal))
            return ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorPathDotDot") ?? "路径不能包含 ..";
        if (path.Split('.').Any(segment => segment.Length == 0 || segment.Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '-'))))
            return ViewModelBase.TranslateTextWithSuffix("Bench", "ErrorPathInvalidChars") ?? "路径段只能包含字母、数字、_、-";
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
        ShowParams = !completionBinding;
        _onEdit = onEdit;
        ParamRows = new ObservableCollection<BenchParamRowVM>(
            model.Params.Select(kv => new BenchParamRowVM(model, kv.Key, kv.Value, onEdit)));
    }

    public TriggerBinding Model { get; }

    /// <summary>Shared live workflow-id options (the canvas updates it on add/rename/delete).</summary>
    public ObservableCollection<string> WorkflowOptions { get; }

    public ObservableCollection<BenchParamRowVM> ParamRows { get; }

    /// <summary>
    /// False on a completion binding: the scheduler hands it no parameters at all, so the
    /// inspector offers no param editor there. Any params already in a legacy config still
    /// appear in <see cref="ParamRows"/> — they are shown so the operator can see and delete
    /// them, and the compiler reports them as <c>trigger.binding.params-on-completion</c>.
    /// </summary>
    public bool ShowParams { get; }

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
        if (!ShowParams)
            return;
        var name = "param" + (Model.Params.Count + 1);
        while (Model.Params.ContainsKey(name))
            name += "_";
        Model.Params[name] = string.Empty;
        ParamRows.Add(new BenchParamRowVM(Model, name, string.Empty, _onEdit));
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

    public string InstallStatus => IsInstalled
        ? ViewModelBase.TranslateTextWithSuffix("Bench", "Installed") ?? "已安装"
        : (_model.Source == "local"
            ? ViewModelBase.TranslateTextWithSuffix("Bench", "NotInstalled") ?? "未安装"
            : ViewModelBase.TranslateTextWithSuffix("Bench", "RemoteSource") ?? "远程来源");
}

/// <summary>ToolKit metadata / plugins / run-parameter inspector page (Bench UX v2 §4.3).</summary>
public sealed class BenchToolkitInspectorVM : ReactiveObject
{
    private static readonly string[] IconChoices =
        ["🛠️", "🤖", "📦", "🧰", "⚙️", "🎛️", "🔧", "🧩", "💡", "🖥️", "📡", "🧠"];

    private readonly Toolkit _toolkit;
    private readonly Action _onEdit;
    private readonly Func<string, bool> _isPluginInstalled;
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
