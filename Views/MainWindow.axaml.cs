using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using System.ComponentModel;
using BasicHelper.LiteDB;
using System;
using System.Collections.Generic;
using Avalonia;
using FluentAvalonia.Styling;
using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using FluentAvalonia.Core.ApplicationModel;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using System;
using System.Runtime.InteropServices;

#pragma warning disable CS8602 // �����ÿ��ܳ��ֿ����á�
#pragma warning disable CS8601 // �������͸�ֵ����Ϊ null��
#pragma warning disable CS8605 // ȡ��װ�����Ϊ null ��ֵ��

namespace KitX_Dashboard.Views
{
    public partial class MainWindow : CoreWindow
    {
        private readonly DataTable local_db_table = (Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("Windows").ReturnResult as DataTable;

        public MainWindow()
        {
            local_db_table.ResetKeys(
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
            );

            InitializeComponent();

            Position = new(
                PositionCameCenter((int)(local_db_table
                    .Query(1).ReturnResult as List<object>)[3], true)
            ,
                PositionCameCenter((int)(local_db_table
                    .Query(1).ReturnResult as List<object>)[4], false)
            );
        }

        /// <summary>
        /// �������
        /// </summary>
        /// <param name="input">���������</param>
        /// <param name="isLeft">�Ƿ��Ǿ������</param>
        /// <returns>����������</returns>
        private int PositionCameCenter(int input, bool isLeft)
        {
            if (isLeft)
                return input == -1 ? (Screens.Primary.WorkingArea.Width - 1280) / 2 : input;
            else return input == -1 ? (Screens.Primary.WorkingArea.Height - 720) / 2 : input;
        }

        /// <summary>
        /// ����Ԫ����
        /// </summary>
        private void SaveMetaData()
        {
            local_db_table.Update(1, "Width", Width);
            local_db_table.Update(1, "Height", Height);
            local_db_table.Update(1, "Left", Position.X);
            local_db_table.Update(1, "Top", Position.Y);
        }

        /// <summary>
        /// ���ڹرմ���ʱ�¼�
        /// </summary>
        /// <param name="e">�ر��¼�����</param>
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            SaveMetaData();
        }

        /// <summary>
        /// �������������¼�
        /// </summary>
        /// <param name="e">������������</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            var thm = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();
            thm.RequestedThemeChanged += OnRequestedThemeChanged;

            if ((bool)(local_db_table.Query(1).ReturnResult as List<object>)[5]
                && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (IsWindows11 && thm.RequestedTheme != FluentAvaloniaTheme.HighContrastModeString)
                {
                    TransparencyBackgroundFallback = Brushes.Transparent;
                    TransparencyLevelHint = WindowTransparencyLevel.Mica;

                    TryEnableMicaEffect(thm);
                }
            }

            thm.ForceWin32WindowToTheme(this);
        }

        /// <summary>
        /// �������ڸ��������¼�
        /// </summary>
        /// <param name="sender">FluentAvaloniaTheme</param>
        /// <param name="args">�������ڸ����������</param>
        private void OnRequestedThemeChanged(FluentAvaloniaTheme sender, RequestedThemeChangedEventArgs args)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (IsWindows11 && args.NewTheme != FluentAvaloniaTheme.HighContrastModeString)
                    TryEnableMicaEffect(sender);
                else if (args.NewTheme == FluentAvaloniaTheme.HighContrastModeString)
                    SetValue(BackgroundProperty, AvaloniaProperty.UnsetValue);
            }
        }

        /// <summary>
        /// ����������ĸ��Ч
        /// </summary>
        /// <param name="thm">FluentAvaloniaTheme</param>
        private void TryEnableMicaEffect(FluentAvaloniaTheme thm)
        {
            if (thm.RequestedTheme == FluentAvaloniaTheme.DarkModeString)
            {
                var color = this.TryFindResource("SolidBackgroundFillColorBase", out var value)
                    ? (Color2)(Color)value : new Color2(32, 32, 32);

                color = color.LightenPercent(-0.8f);

                Background = new ImmutableSolidColorBrush(color,
                    (double)(local_db_table.Query(1).ReturnResult as List<object>)[6]);
            }
            else if (thm.RequestedTheme == FluentAvaloniaTheme.LightModeString)
            {
                var color = this.TryFindResource("SolidBackgroundFillColorBase", out var value)
                    ? (Color2)(Color)value : new Color2(243, 243, 243);

                color = color.LightenPercent(0.5f);

                Background = new ImmutableSolidColorBrush(color, 0.9);
            }
        }
    }
}

#pragma warning restore CS8605 // ȡ��װ�����Ϊ null ��ֵ��
#pragma warning restore CS8601 // �������͸�ֵ����Ϊ null��
#pragma warning restore CS8602 // �����ÿ��ܳ��ֿ����á�
