using Avalonia.Controls;
using Avalonia.Input;
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

        var runButton = this.FindControl<Button>("RunButton");

        if (runButton is not null)
            HotKeyManager.SetHotKey(runButton, new KeyGesture(Key.F5));

        EventService.ThemeConfigChanged += () => InitializeEditor();
    }

    private void InitializeEditor()
    {
        //First of all you need to have a reference for your TextEditor for it to be used inside AvaloniaEdit.TextMate project.
        var textEditor = this.FindControl<TextEditor>("Editor");

        //Here we initialize RegistryOptions with the theme we want to use.
        var registryOptions = new RegistryOptions(ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus);

        //Initial setup of TextMate.
        var textMateInstallation = textEditor.InstallTextMate(registryOptions);

        //Here we are getting the language by the extension and right after that we are initializing grammar with this language.
        //And that's all 😀, you are ready to use AvaloniaEdit with syntax highlighting!
        textMateInstallation.SetGrammar(
            registryOptions.GetScopeByLanguageId(
                registryOptions.GetLanguageByExtension(".cs").Id
            )
        );
    }
}
