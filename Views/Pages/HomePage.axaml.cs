using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BasicHelper.LiteLogger;
using FluentAvalonia.UI.Controls;
using KitX_Dashboard.ViewModels.Controls;
using KitX_Dashboard.ViewModels.Pages;
using KitX_Dashboard.Views.Controls;
using System;

#pragma warning disable CS8602 // �����ÿ��ܳ��ֿ����á�
#pragma warning disable CS8601 // �������͸�ֵ����Ϊ null��

namespace KitX_Dashboard.Views.Pages
{
    public partial class HomePage : UserControl
    {
        private readonly HomePageViewModel viewModel = new();

        public HomePage()
        {
            InitializeComponent();

            DataContext = viewModel;

            InitHomePage();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// ��ʼ������ҳ��
        /// </summary>
        private void InitHomePage()
        {
            this.FindControl<NavigationView>("HomeNavigationView").SelectedItem
                = this.FindControl<NavigationViewItem>(SelectedViewName);
        }

        private static string SelectedViewName
        {
            get => Program.GlobalConfig.Config_Pages.Config_HomePage.SelectedViewName;
            set => Program.GlobalConfig.Config_Pages.Config_HomePage.SelectedViewName = value;
        }

        /// <summary>
        /// ǰ̨ҳ���л��¼�
        /// </summary>
        /// <param name="sender">������� NavigationViewItem</param>
        /// <param name="e">·���¼�����</param>
        private void HomeNavigationView_SelectionChanged(object? sender,
            NavigationViewSelectionChangedEventArgs e)
        {
            try
            {
                SelectedViewName = (
                    (sender as NavigationView).SelectedItem as Control
                ).Tag.ToString();
                this.FindControl<Frame>("HomeFrame").Navigate(SelectedViewType());
            }
            catch (NullReferenceException o)
            {
                Program.LocalLogger.Log("Logger_Debug", o.Message, LoggerManager.LogLevel.Warn);
            }
        }

        private static Type SelectedViewType() => SelectedViewName switch
        {
            "View_Recent" => typeof(MainWindow_RecentUse),
            "View_Count" => typeof(MainWindow_Count),
            "View_ActivityLog" => typeof(MainWindow_ActivityLog),
            _ => typeof(MainWindow_RecentUse),
        };
    }
}

#pragma warning restore CS8601 // �������͸�ֵ����Ϊ null��
#pragma warning restore CS8602 // �����ÿ��ܳ��ֿ����á�

//
//                            __ _..._ _ 
//                            \ `)    `(/
//                            /`       \
//                            |   d  b  |
//              .-"````"=-..--\=    Y  /=
//            /`               `-.__=.'
//     _     / /\                 /o
//    ( \   / / |                 |
//     \ '-' /   &gt;    /`""--.    /
//      '---'   /    ||      |   \\
//              \___,,))      \_,,))
// 
//
