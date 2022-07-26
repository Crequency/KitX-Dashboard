using Avalonia;
using Avalonia.ReactiveUI;
using BasicHelper.LiteLogger;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.ObjectModel;

#pragma warning disable CS8602 // �����ÿ��ܳ��ֿ����á�

namespace KitX_Dashboard
{
    internal class Program
    {
        internal static LoggerManager LocalLogger = new();

        internal static Config GlobalConfig = new();

        internal static WebServer? LocalWebServer;

        internal static ObservableCollection<PluginCard>? PluginCards;

        internal delegate void LanguageChangedHandler();

        internal static event LanguageChangedHandler? LanguageChanged;

        /// <summary>
        /// ִ��ȫ���¼�
        /// </summary>
        /// <param name="eventName">�¼�����</param>
        internal static void Invoke(string eventName)
        {
            switch (eventName)
            {
                case "LanguageChanged":
                    LanguageChanged();
                    break;
            }
        }

        /// <summary>
        /// ������, Ӧ�ó������; չ�� summary �鿴����
        /// </summary>
        /// <param name="args">������������</param>
        /// Initialization code. Don't use any Avalonia, third-party APIs or any
        /// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        /// yet and stuff might break.
        /// ��ʼ������. �벻Ҫ�� AppMain ������֮ǰʹ���κ� Avalonia, �������� API ���� ͬ����������صĴ���:
        /// ��صĴ��뻹û�б���ʼ��, ���һ������ܻᱻ�ƻ�
        [STAThread]
        public static void Main(string[] args)
        {
            #region ��Ҫ�ĳ�ʼ��

            LanguageChanged += () => { };

            #endregion

            #region ִ������ʱ���

            Helper.StartUpCheck();

            #endregion

            #region ����Ӧ����������ѭ��

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            #endregion

            #region ����������Ϣ

            Helper.SaveInfo();

            #endregion

            #region �˳�����

            Helper.Exit();

            #endregion
        }

        /// <summary>
        /// ���� Avalonia Ӧ��; չ�� summary �鿴����
        /// </summary>
        /// <returns>Ӧ�ù�����</returns>
        /// Avalonia configuration, don't remove; also used by visual designer.
        /// Avalonia ������, �벻Ҫɾ��; ͬʱҲ���ڿ��ӻ������
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect().LogToTrace().UseReactiveUI();
    }
}

#pragma warning restore CS8602 // �����ÿ��ܳ��ֿ����á�
