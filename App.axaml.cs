using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BasicHelper.LiteDB;
using BasicHelper.IO;
using KitX_Dashboard.ViewModels;
using KitX_Dashboard.Views;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Controls;
using KitX_Dashboard.Data;

#pragma warning disable CS8601 // �������͸�ֵ����Ϊ null��
#pragma warning disable CS8602 // �����ÿ��ܳ��ֿ����á�
#pragma warning disable CS8600 // �� null �����������Ϊ null ��ֵת��Ϊ�� null ���͡�
#pragma warning disable CS8604 // �������Ͳ�������Ϊ null��

namespace KitX_Dashboard
{
    public partial class App : Application
    {
        private readonly DataTable local_db_table_app = (Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("App").ReturnResult as DataTable;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            string lang = (local_db_table_app.Query(1).ReturnResult as List<object>)[2] as string;
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(
                AvaloniaRuntimeXamlLoader.Load(
                    FileHelper.ReadAll($"{GlobalInfo.LanguageFilePath}/{lang}.axaml")
                ) as ResourceDictionary
            );
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }

            string color = (local_db_table_app.Query(1).ReturnResult as List<object>)[4] as string;
            Resources["ThemePrimaryAccent"] = new SolidColorBrush(Color.Parse(color));

            base.OnFrameworkInitializationCompleted();
        }
    }
}

#pragma warning restore CS8604 // �������Ͳ�������Ϊ null��
#pragma warning restore CS8600 // �� null �����������Ϊ null ��ֵת��Ϊ�� null ���͡�
#pragma warning restore CS8602 // �����ÿ��ܳ��ֿ����á�
#pragma warning restore CS8601 // �������͸�ֵ����Ϊ null��
