using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AltRunSharp
{
    /// <summary>
    /// 开始菜单快捷方式索引服务：扫描用户与公共开始菜单下的 .lnk，
    /// 通过 FileSystemWatcher 监听变化并去抖自动刷新索引。
    /// </summary>
    public class StartMenuIndexService : IDisposable
    {
        public event Action? IndexChanged;

        private volatile LaunchItem[] _items = Array.Empty<LaunchItem>();
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly Timer _debounceTimer;
        private bool _disposed;

        public StartMenuIndexService()
        {
            _debounceTimer = new Timer(OnDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);

            SetupWatchers();

            // 首次索引在后台线程构建，构建完成后触发 IndexChanged
            Task.Run(() => RebuildIndex());
        }

        public LaunchItem[] GetItems() => _items;

        private void SetupWatchers()
        {
            var roots = new List<string>();
            try
            {
                string userStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                if (!string.IsNullOrWhiteSpace(userStartMenu) && Directory.Exists(userStartMenu))
                {
                    roots.Add(userStartMenu);
                }
            }
            catch { }

            try
            {
                string commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
                if (!string.IsNullOrWhiteSpace(commonStartMenu) && Directory.Exists(commonStartMenu))
                {
                    roots.Add(commonStartMenu);
                }
            }
            catch { }

            foreach (var dir in roots)
            {
                try
                {
                    var watcher = new FileSystemWatcher(dir, "*.lnk")
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
                    };
                    watcher.Created += OnFileSystemChanged;
                    watcher.Deleted += OnFileSystemChanged;
                    watcher.Renamed += OnFileSystemChanged;
                    watcher.Changed += OnFileSystemChanged;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch
                {
                    // IO try/catch 兜底
                }
            }
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;
            try
            {
                _debounceTimer.Change(500, Timeout.Infinite);
            }
            catch { }
        }

        private void OnDebounceTimer(object? state)
        {
            if (_disposed) return;
            Task.Run(() => RebuildIndex());
        }

        private void RebuildIndex()
        {
            if (_disposed) return;
            try
            {
                var items = ScanItems();
                if (_disposed) return;
                _items = items;
                IndexChanged?.Invoke();
            }
            catch
            {
                // IO 异常兜底
            }
        }

        private static LaunchItem[] ScanItems()
        {
            var dict = new Dictionary<string, LaunchItem>(StringComparer.OrdinalIgnoreCase);

            // 1. 用户目录（优先）
            try
            {
                string userStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                if (!string.IsNullOrWhiteSpace(userStartMenu) && Directory.Exists(userStartMenu))
                {
                    CollectLnkFiles(new DirectoryInfo(userStartMenu), dict);
                }
            }
            catch { }

            // 2. 公共目录
            try
            {
                string commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
                if (!string.IsNullOrWhiteSpace(commonStartMenu) && Directory.Exists(commonStartMenu))
                {
                    CollectLnkFiles(new DirectoryInfo(commonStartMenu), dict);
                }
            }
            catch { }

            return dict.Values.ToArray();
        }

        private static void CollectLnkFiles(DirectoryInfo root, Dictionary<string, LaunchItem> dict)
        {
            try
            {
                foreach (var file in root.EnumerateFiles("*.lnk"))
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(file.Name);
                        if (!string.IsNullOrWhiteSpace(name) && !dict.ContainsKey(name))
                        {
                            dict[name] = new LaunchItem
                            {
                                Name = name,
                                Path = file.FullName,
                                Description = "开始菜单"
                            };
                        }
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                foreach (var subDir in root.EnumerateDirectories())
                {
                    try
                    {
                        CollectLnkFiles(subDir, dict);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _debounceTimer.Dispose();
            }
            catch { }

            foreach (var watcher in _watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch { }
            }
            _watchers.Clear();
        }
    }
}
