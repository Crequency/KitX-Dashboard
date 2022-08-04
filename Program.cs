using Avalonia;
using Avalonia.ReactiveUI;
using BasicHelper.LiteLogger;
using KitX_Dashboard.Data;
using KitX_Dashboard.Services;
using KitX_Dashboard.Models;
using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.ObjectModel;

namespace KitX_Dashboard
{
    internal class Program
    {
        internal static LoggerManager LocalLogger = new();

        internal static Config GlobalConfig = new();

        internal static WebServer? LocalWebServer;

        internal static ObservableCollection<PluginCard>? PluginCards;

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

            EventHandlers.Init();

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

//                                                                                                      
//                                                                                                      
//                                    ..'',,,,,,,,,,,'''''''''''''....                                  
//                                  .';;;;;;;;;;;;;;;,,,,,,,,,,,',,,,,'.                                
//                                 .,;;;;'.........................',,,'.                               
//                                 ';;;;.                          .',,,'                               
//                                 ';;;,.                           .,,,'.                              
//                                 ';;;,.                           .,,,'.                              
//                                 ';;;,.                           .,,,'.                              
//                              ..':cc:;.                          ..,;:;'.                             
//            .,;;;;;;;;;;;;;;;;okO00Oo:;;;;;;;;;;;;;'...............,okkkxc'.............              
//           .,::::::::::::::::lOKXKKkc::::::::::::::,''.............'ckOOOx:.............              
//           .;::::::::::::::::d0XXX0o:::::::::::::::,''..............;dOOOOo'.............             
//           ,::::::::::::::::lOKKKKxc:::::::::::::::,''..............'ckOOOx:.............             
//          .::::::::::::::::cd0KKK0o::::::::::::::::,''.''''....''..'.;dOOOOl,.''..'''..'..            
//      ':::looooooooooooooooodkkkkxoooooooooooooooolc:;;;;;;;;;;;;;;;;;clollc:;;;;;;;;;;;;,,''''''.    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//     .:ddddddddddddddddddddddddddddddddddddddddddddlc::::::::::::::::::::::::::::::::::::::::::::'    
//      ....;ccccccccccccccccccccccccccccclcccccccccc:,,,,,,,,,,,,,,,;;;;;;;;;;;;;;,,,,,,,,,,,,'....    
//          .;::::::::::;;;;;;;;;;;;;;;;:::::::::::::,''...........'',,,,,,,,,,,,,,''...........        
//          .;::::::::::;;;;;;;;;;;;;;;;:::::::::::::,''...........'',,,,,,,,,,,,,,''...........        
//          .,::::::::::;;;;;;;;;;;;;;;;:::::::::::::,''...........'',,,,,,,,,,,,,,''...........        
//           '::::::::::;;;;;;;;;;;;;;;;:::::::::::::,''...........'',,,,,,,,,,,,,,''..........         
//           .:::::::::::;;;;;cooodoc::::::::::::::::,''...........'''',clllc;''''''...........         
//           .;:::::::::::::::oOKKXKd::::::::::::::::,''...............:xOOOk:'................         
//           .;:::::::::::::::lOXKXKd::::::::::::::::,''...............:kOOOk:.................         
//           .,:::::::::::::::lOXKKKxc:::::::::::::::,''..............'ckOOOx:.................         
//            ':::::::::::::::ckKXKKkc:::::::::::::::,''..............'cOOOOx;................          
//            .:::::::::::::::ckKKKKkc:::::::::::::::,''..............'lOOOOd;................          
//            .;::::::::::::::cxKKKXOl:::::::::::::::,''..............'lOOOOd,................          
//            .;::::::::::::::cxKKKXOl:::::::::::::::,''..............,oOOOOo,................          
//            .,:::::::::::::::dKXKXOo:::::::::::::::,''..............,dOOOOo'................          
//             ':::::::::::::::d0XKX0o:::::::::::::::,''..............;dOOOOl'...............           
//             .:::::::::::::::o0XKX0o:::::::::::::::,''..............;xOOOOc'...............           
//             .;::::::::::::::o0XKX0d:::::::::::::::,''..............:xOOOkc'...............           
//             .;::::::::::::::lOXKXKd:::::::::::::::,''..............:kOOOk:................           
//             .,::::::::::::::lOKKXKxc::::::::::::::,''.............'ckOOOx:................           
//              '::::::::::::::lkKKKKxc::::::::::::::,''.............'ckOOOx;...............            
//              .::::::::::::::ckKKKKkc::::::::::::::,''.............'lOOOOd;...............            
//              ...............';cccc;'...............................'::::,...............             
//                                                                                                      
//                                                                                                      
