using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using TextMateSharp.Grammars;

namespace KitX.Dashboard.Views;

public partial class DebugWindow : Window, IView
{
    private readonly DebugWindowViewModel viewModel = new();

    public DebugWindow()
    {
        InitializeComponent();

        DataContext = viewModel;

        Initialize();
    }

    private void Initialize()
    {
        InitializeEditor();

        EventService.ThemeConfigChanged += InitializeEditor;
    }

    private void InitializeEditor()
    {
        var textEditor = this.FindControl<TextEditor>("CodeEditor");

        var outputEditor = this.FindControl<TextEditor>("OutputEditor");

        SetEditor(textEditor, ".cs");

        SetEditor(outputEditor, ".log");
    }

    private void SetEditor(TextEditor? textEditor, string ext)
    {
        if (textEditor is null) return;

        var registryOptions = new RegistryOptions(
            ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus
        );

        var textMateInstallation = textEditor.InstallTextMate(registryOptions);

        textMateInstallation.SetGrammar(
            registryOptions.GetScopeByLanguageId(
                registryOptions.GetLanguageByExtension(ext).Id
            )
        );
    }
}
