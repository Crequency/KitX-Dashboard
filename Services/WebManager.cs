using Serilog;
using System;
using System.Threading;

namespace KitX_Dashboard.Services
{
    public class WebManager : IDisposable
    {
        public WebManager()
        {

        }

        internal PluginsServer? pluginsServer;
        internal DevicesServer? devicesServer;

        public WebManager Start()
        {
            new Thread(() =>
            {
                try
                {
                    DevicesManager.InitEvents();

                    DevicesManager.KeepCheckAndRemove();
                    DevicesManager.Watch4MainDevice();
                    PluginsManager.KeepCheckAndRemove();
                    PluginsManager.KeepCheckAndRemoveOrDelete();

                    pluginsServer = new();
                    devicesServer = new();

                    pluginsServer.Start();
                    devicesServer.Start();
                }
                catch (Exception ex)
                {
                    Log.Error("In WebManager Start", ex);
                }
            }).Start();

            return this;
        }

        public WebManager Stop()
        {
            pluginsServer?.Stop();
            devicesServer?.Stop();

            pluginsServer?.Dispose();
            devicesServer?.Dispose();

            return this;
        }

        public WebManager Restart()
        {
            Stop();
            Start();
            return this;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            pluginsServer?.Dispose();
            devicesServer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

//                                         .....
//                                    .e$$$$$$$$$$$$$$e.
//                                  z$$ ^$$$$$$$$$$$$$$$$$.
//                                .$$$* J$$$$$$$$$$$$$$$$$$$e
//                               .$"  .$$$$$$$$$$$$$$$$$$$$$$*-
//                              .$  $$$$$$$$$$$$$$$$***$$  .ee"
//                 z**$$        $$r ^**$$$$$$$$$*" .e$$$$$$*"
//                " -\e$$      4$$$$.         .ze$$$""""
//               4 z$$$$$      $$$$$$$$$$$$$$$$$$$$"
//               $$$$$$$$     .$$$$$$$$$$$**$$$$*"
//             z$$"    $$     $$$$P*""     J$*$$c
//            $$"      $$F   .$$$          $$ ^$$
//           $$        *$$c.z$$$          $$   $$
//          $P          $$$$$$$          4$F   4$
//         dP            *$$$"           $$    '$r
//        .$                            J$"     $"
//        $                             $P     4$
//        F                            $$      4$
//                                    4$%      4$
//                                    $$       4$
//                                   d$"       $$
//                                   $P        $$
//                                  $$         $$
//                                 4$%         $$
//                                 $$          $$
//                                d$           $$
//                                $F           "3
//                         r=4e="  ...  ..rf   .  ""%
//                        $**$*"^""=..^4*=4=^""  ^"""


