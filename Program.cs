using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using System;
using System.IO;
using BasicHelper.LiteDB;

#pragma warning disable CS8602 // �����ÿ��ܳ��ֿ����á�

namespace KitX_Dashboard
{
    internal class Program
    {
        internal static DBManager LocalDataBase = new();

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
            #region ��ʼ�� LiteDB
            string DataBaseWorkBase = Path.GetFullPath(Data.GlobalInfo.ConfigPath);

            if (Directory.Exists(DataBaseWorkBase))
                LocalDataBase.WorkBase = DataBaseWorkBase;
            else
            {
                Directory.CreateDirectory(DataBaseWorkBase);
                LocalDataBase.WorkBase = DataBaseWorkBase;
                InitDataBase();
            }
            #endregion

            #region ��� Catrol.Algorithm �⻷������װ����
            if (!Algorithm.Interop.Environment.CheckEnvironment())
                new System.Threading.Thread(() =>
                {
                    Algorithm.Interop.Environment.InstallEnvironment();
                }).Start();
            #endregion

            #region ����Ӧ����������ѭ��
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); 
            #endregion

            LocalDataBase.Save2File();
        }

        /// <summary>
        /// ��ʼ�����ݿ�
        /// </summary>
        public static void InitDataBase()
        {
            #region �������ݿ�
            LocalDataBase.CreateDataBase("Dashboard_Settings");
            #endregion

            var db_windows = LocalDataBase.GetDataBase("Dashboard_Settings").ReturnResult as DataBase;

            #region �����±���ʼ���ֶ�
            db_windows.AddTable("Windows", new(
                    new string[]
                    {
                    "Name",         "Width",        "Height",       "Left",         "Top",
                    "EnabledMica",  "MicaOpacity"
                    },
                    new Type[]
                    {
                    typeof(string), typeof(double), typeof(double), typeof(int),    typeof(int),
                    typeof(bool),   typeof(double)
                    }
                ));
            #endregion

            #region ��ʼ���±�
            var dt_mainwin = db_windows.GetTable("Windows").ReturnResult as DataTable;
            dt_mainwin.Add(
                new object[]
                {
                    "MainWindow",   (double)1280,   (double)720,    -1,             -1,
                    true,           0.15
                }
            ); 
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
