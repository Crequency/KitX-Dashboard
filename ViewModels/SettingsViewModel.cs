using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BasicHelper.IO;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Media;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using System.Collections.Generic;

#pragma warning disable CS8601 // �������͸�ֵ����Ϊ null��
#pragma warning disable CS8602 // �����ÿ��ܳ��ֿ����á�
#pragma warning disable CS8600 // �� null �����������Ϊ null ��ֵת��Ϊ�� null ���͡�
#pragma warning disable CS8604 // �������Ͳ�������Ϊ null��

namespace KitX_Dashboard.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {

        public SettingsViewModel()
        {
            InitCommands();
        }

        /// <summary>
        /// ��ʼ������
        /// </summary>
        private void InitCommands()
        {
            AppNameButtonClickedCommand = new(AppNameButtonClicked);
            ColorConfirmedCommand = new(ColorConfirmed);
        }

        public int TabControlSelectedIndex { get; set; } = 0;

        public static string VersionText => Program.LocalVersion.GetVersionText();

        public string[] AppThemes { get; } = new[]
        {
            FluentAvaloniaTheme.LightModeString,
            FluentAvaloniaTheme.DarkModeString,
            FluentAvaloniaTheme.HighContrastModeString
        };

        private string _currentAppTheme = (string)
            (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[3]
            == "Follow" ? AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>().RequestedTheme
            : (string)(Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[3];

        public string CurrentAppTheme
        {
            get => _currentAppTheme;
            set
            {
                (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[3] = value;
                if (RaiseAndSetIfChanged(ref _currentAppTheme, value))
                {
                    var faTheme = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();
                    faTheme.RequestedTheme = value;
                }
            }
        }

        /// <summary>
        /// ��������
        /// </summary>
        public static void LoadLanguage()
        {
            string lang = (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[2] as string;
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary
            );
        }

        public static int LanguageSelected
        {
            get => (string)(Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[2] switch
            {
                "zh-cn" => 0,
                "zh-cnt" => 1,
                "en-us" => 2,
                "ja-jp" => 3,
                _ => 0,
            };
            set
            {
                (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[2] = value switch
                {
                    0 => "zh-cn",
                    1 => "zh-cnt",
                    2 => "en-us",
                    3 => "ja-jp",
                    _ => "zh-cn",
                };
                LoadLanguage();
            }
        }

        public static int MicaStatus
        {
            get => (bool)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[5] ? 0 : 1;
            set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[5] = value != 1;
        }

        public static double MicaOpacity
        {
            get => (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[6];
            set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[6] = value;
        }

        private Color2 nowColor = new();

        public Color2 ThemeColor
        {
            get => new((Application.Current.Resources["ThemePrimaryAccent"] as SolidColorBrush).Color);
            set => nowColor = value;
        }

        public static int WebServerPort
        {
            get => (int)(Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[5];
            set => (Helper.local_db_table_app.Query(1).ReturnResult as List<object>)[5] = value;
        }

        public bool AuthorsListVisibility { get; set; } = false;

        public int clickCount = 0;

        public DelegateCommand? AppNameButtonClickedCommand { get; set; }

        public DelegateCommand? ColorConfirmedCommand { get; set; }

        private void AppNameButtonClicked(object _) => ++clickCount;

        private void ColorConfirmed(object _)
        {
            var c = nowColor;
            Application.Current.Resources["ThemePrimaryAccent"] =
                new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));
        }
    }
}

#pragma warning restore CS8604 // �������Ͳ�������Ϊ null��
#pragma warning restore CS8600 // �� null �����������Ϊ null ��ֵת��Ϊ�� null ���͡�
#pragma warning restore CS8602 // �����ÿ��ܳ��ֿ����á�
#pragma warning restore CS8601 // �������͸�ֵ����Ϊ null��
