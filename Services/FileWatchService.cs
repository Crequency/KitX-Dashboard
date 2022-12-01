using Serilog;
using System;
using System.IO;
using KitX_Dashboard.Data;

namespace KitX_Dashboard.Services
{
    internal class FileWatchService 
    {
        internal FileSystemWatcher watcher;
        public bool isProgram { get; set; }
        public readonly object isProgramLock = new object();

        /// <summary>
        /// 配置文件热加载实现
        /// </summary>
        public FileWatchService()
        {
            watcher = new FileSystemWatcher();
            watcher.Changed += OnChanged;
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Path = Path.GetDirectoryName(Path.GetFullPath(GlobalInfo.ConfigFilePath));
            watcher.Filter = Path.GetFileName(Path.GetFullPath(GlobalInfo.ConfigFilePath));
            watcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// 写入事件函数
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event</param>
        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if ((e.ChangeType != WatcherChangeTypes.Changed))
            {
                return;
            }
            if (isProgram)
            {
                Log.Information("Config file was changed internally.");
                lock (isProgramLock)
                {
                    isProgram = false;
                }
                return;
            }
            Log.Information("Config file was changed externally. Hot load config file....");
            Helper.LoadConfig();
        }
    }
}
