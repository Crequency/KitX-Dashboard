using Avalonia;
using Avalonia.ReactiveUI;
using BasicHelper.LiteDB;
using BasicHelper.LiteLogger;
using BasicHelper.Util;
using KitX_Dashboard.Services;
using System;
using System.Collections.Generic;
using System.IO;

using Version = BasicHelper.Util.Version;

namespace KitX_Dashboard
{
    internal class Program
    {
        internal static DBManager LocalDataBase = new();

        internal static LoggerManager LocalLogger = new();

        internal static Version LocalVersion;

        internal static WebServer LocalWebServer = new();

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
            #region ִ������ʱ���
            Helper.StartUpCheck();
            #endregion

            #region ����Ӧ����������ѭ��
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            #endregion

            Helper.SaveInfo();

            Helper.Exit();

            LocalDataBase.Save2File();
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
