using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AltRunSharp
{
    public partial class MainWindow : Window
    {
        private IntPtr _hWnd;
        private IHotkeyService _hotkeyService = null!;
        private IConfigService _configService = null!;
        private string _configPath = null!;
        private FileSystemWatcher? _configWatcher;
        private AppConfig _config = new AppConfig();

        private readonly LauncherViewModel _vm = new LauncherViewModel();
        private RunnerService _runner = null!;
        private ServiceManager _serviceManager = null!;
        private ScheduleManager _scheduleManager = null!;
        private SettingsWindow? _settingsWindow;

        // ── Tray Win32 ──────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        private const int NIM_ADD = 0x00;
        private const int NIM_DELETE = 0x02;
        private const int NIF_MESSAGE = 0x01;
        private const int NIF_ICON = 0x02;
        private const int NIF_TIP = 0x04;
        private const int WM_USER = 0x0400;
        private const int WM_TRAYMSG = WM_USER + 1024;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;

        private NOTIFYICONDATA _nid;
        private ContextMenu? _trayMenu;

        // ── Monitor & DPI ────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        // ── Focus & IME helpers ──────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll", CharSet = CharSet.Auto)] private static extern int ImmGetCompositionString(IntPtr hIMC, uint dwIndex, StringBuilder? lpBuf, int dwBufLen);
        private const uint GCS_COMPSTR = 0x0008;

        private const int WM_WTSSESSION_CHANGE = 0x02B1;
        private const int WTS_SESSION_LOCK = 0x7;

        // ────────────────────────────────────────────────────────────────────

        public MainWindow()
        {
            InitializeComponent();
            this.Deactivated += MainWindow_Deactivated;
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _hWnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(_hWnd);
            source?.AddHook(WndProc);

            // data/ is always relative to the exe location
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.Combine(baseDir, "data", "config.json");

            _configService = new ConfigService(_configPath);
            _runner = new RunnerService(Path.GetDirectoryName(_configPath)!);
            _serviceManager = new ServiceManager(Path.GetDirectoryName(_configPath)!, Dispatcher);
            _scheduleManager = new ScheduleManager(Path.GetDirectoryName(_configPath)!, Dispatcher, _config);
            _hotkeyService = new HotkeyService(_hWnd);

            InitTray();
            ReloadConfigAndHotkeys();

            InitConfigWatcher();

            // Auto-start boot services and schedule tasks after config is loaded
            _serviceManager.StartBootServices(_config.ScriptItems);
            _scheduleManager.StartBootTasks();

            Microsoft.Win32.SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

            this.Dispatcher.BeginInvoke(new Action(HideWindow), DispatcherPriority.Background);
        }

        // ── Tray ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads the tray icon from the WPF embedded resource (works in both
        /// normal builds and PublishSingleFile bundles).
        /// </summary>
        private static IntPtr LoadTrayIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/icon.ico");
                using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
                if (stream != null)
                    return new System.Drawing.Icon(stream).Handle;
            }
            catch { }
            return System.Drawing.SystemIcons.Application.Handle;
        }

        private void InitTray()
        {
            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hWnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYMSG,
                hIcon = LoadTrayIcon(),
                szTip = "AltRunSharp"
            };
            Shell_NotifyIcon(NIM_ADD, ref _nid);

            _trayMenu = new ContextMenu();
            var showItem = new MenuItem { Header = "显示" };
            showItem.Click += (s, e) => ShowLauncher();
            var settingsItem = new MenuItem { Header = "设置" };
            settingsItem.Click += (s, e) => OpenSettings();
            var exitItem = new MenuItem { Header = "退出" };
            exitItem.Click += (s, e) => CloseApp();

            _trayMenu.Items.Add(showItem);
            _trayMenu.Items.Add(settingsItem);
            _trayMenu.Items.Add(exitItem);
        }

        private void ShowTrayContextMenu()
        {
            if (_trayMenu != null)
            {
                SetForegroundWindow(_hWnd);
                _trayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                _trayMenu.IsOpen = true;
            }
        }

        // ── Config ───────────────────────────────────────────────────────────

        private void InitConfigWatcher()
        {
            string dir = Path.GetDirectoryName(_configPath)!;
            Directory.CreateDirectory(dir);

            _configWatcher = new FileSystemWatcher(dir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            Action onChange = () =>
            {
                Thread.Sleep(100);
                this.Dispatcher.BeginInvoke(new Action(ReloadConfigAndHotkeys));
            };

            _configWatcher.Changed += (s, e) => onChange();
            _configWatcher.Created += (s, e) => onChange();
            _configWatcher.EnableRaisingEvents = true;
        }

        private void ReloadConfigAndHotkeys()
        {
            _config = _configService.LoadConfig();
            _vm.UpdateConfig(_config);
            _hotkeyService.RegisterGlobalHotkey(
                _config.Hotkey ?? "Alt+R",
                _config.ClickMode ?? "single",
                () => ToggleWindow());
        }


        // ── Window show/hide ─────────────────────────────────────────────────

        private void ToggleWindow()
        {
            if (this.Visibility == Visibility.Visible)
                HideWindow();
            else
                ShowLauncher();
        }

        private void ShowLauncher()
        {
            if (this.Visibility == Visibility.Visible && this.IsActive)
            {
                InputTextBox.Focus();
                return;
            }

            // Center on mouse monitor with DPI awareness
            GetCursorPos(out POINT pt);
            IntPtr hMonitor = MonitorFromPoint(pt, 2);
            MONITORINFO mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(hMonitor, ref mi);

            double scaleX = 1.0, scaleY = 1.0;
            try
            {
                GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                scaleX = dpiX / 96.0;
                scaleY = dpiY / 96.0;
            }
            catch
            {
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget != null)
                {
                    scaleX = src.CompositionTarget.TransformToDevice.M11;
                    scaleY = src.CompositionTarget.TransformToDevice.M22;
                }
            }

            double workLeft = mi.rcWork.Left / scaleX;
            double workTop = mi.rcWork.Top / scaleY;
            double workWidth = (mi.rcWork.Right - mi.rcWork.Left) / scaleX;
            double workHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / scaleY;

            this.Left = workLeft + (workWidth - this.Width) / 2.0;
            this.Top = workTop + (workHeight - this.Height) / 2.0;

            this.Visibility = Visibility.Visible;
            this.Show();
            this.Activate();
            SetForegroundWindow(_hWnd);

            // Clear and focus
            InputTextBox.Text = string.Empty;
            HideResults();
            InputTextBox.Focus();
        }

        private void HideWindow()
        {
            if (this.Visibility == Visibility.Collapsed) return;
            this.Visibility = Visibility.Collapsed;
            this.Hide();
        }

        // ── Search & Execute ─────────────────────────────────────────────────

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = InputTextBox.Text;
            List<SearchResult> results;

            // If input starts with '/' and contains a space, the user is passing args to a script.
            // In this case, match by exact command name only (don't filter on the args part).
            if (text.StartsWith("/") && text.Contains(' '))
            {
                string cmdName = text.Substring(1, text.IndexOf(' ') - 1);
                var hit = _vm.FindScriptByName(cmdName);
                results = hit != null ? new List<SearchResult> { hit } : new List<SearchResult>();
            }
            else
            {
                results = _vm.Search(text);
            }

            if (results.Count == 0)
            {
                HideResults();
            }
            else
            {
                Separator.Visibility = Visibility.Visible;
                ResultsList.Visibility = Visibility.Visible;
                ResultsList.ItemsSource = results;
                ResultsList.SelectedIndex = 0;
            }
        }

        private void HideResults()
        {
            Separator.Visibility = Visibility.Collapsed;
            ResultsList.Visibility = Visibility.Collapsed;
            ResultsList.ItemsSource = null;
        }

        private void ExecuteSelected()
        {
            var result = ResultsList.SelectedItem as SearchResult;
            if (result == null) return;

            HideWindow();

            if (result.Kind == "launch" && result.LaunchItem != null)
            {
                _runner.RunLaunchItem(result.LaunchItem);
            }
            else if (result.Kind == "script" && result.ScriptItem != null)
            {
                // Parse extra args from input after the command name
                string input = InputTextBox.Text.Trim();
                string cmdPrefix = "/" + result.ScriptItem.Name;
                string argsText = input.StartsWith(cmdPrefix, StringComparison.OrdinalIgnoreCase)
                    ? input.Substring(cmdPrefix.Length).Trim()
                    : string.Empty;
                string[] extraArgs = ScriptArgsParser.Parse(argsText);
                // Script item
                if (result.ScriptItem != null)
                {
                    if (result.ScriptItem.LaunchMode == "service")
                    {
                        // Route to ServiceManager
                        _serviceManager.StartService(result.ScriptItem, string.Join(" ", extraArgs));
                    }
                    else
                    {
                        _runner.RunScriptItem(result.ScriptItem, extraArgs, _config);
                    }
                }
            }
        }

        // ── Keyboard handling ─────────────────────────────────────────────────

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed) return;

            switch (e.Key)
            {
                case Key.Escape:
                    HandleEscape(e);
                    break;

                case Key.Tab:
                    if (ResultsList.Visibility == Visibility.Visible && ResultsList.SelectedItem is SearchResult tab)
                    {
                        InputTextBox.Text = tab.Name;
                        InputTextBox.CaretIndex = InputTextBox.Text.Length;
                        e.Handled = true;
                    }
                    break;

                case Key.Up:
                    if (ResultsList.Visibility == Visibility.Visible)
                    {
                        int idx = ResultsList.SelectedIndex;
                        if (idx > 0) ResultsList.SelectedIndex = idx - 1;
                        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                        e.Handled = true;
                    }
                    break;

                case Key.Down:
                    if (ResultsList.Visibility == Visibility.Visible)
                    {
                        int idx = ResultsList.SelectedIndex;
                        if (idx < ResultsList.Items.Count - 1) ResultsList.SelectedIndex = idx + 1;
                        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                        e.Handled = true;
                    }
                    break;

                case Key.Enter:
                    ExecuteSelected();
                    e.Handled = true;
                    break;
            }
        }

        private void HandleEscape(KeyEventArgs e)
        {
            // If IME has composition, let IME handle it
            IntPtr hIMC = ImmGetContext(_hWnd);
            if (hIMC != IntPtr.Zero)
            {
                try
                {
                    int len = ImmGetCompositionString(hIMC, GCS_COMPSTR, null, 0);
                    if (len > 0) return; // let IME consume Escape
                }
                finally
                {
                    ImmReleaseContext(_hWnd, hIMC);
                }
            }

            HideWindow();
            e.Handled = true;
        }

        // ── WndProc ──────────────────────────────────────────────────────────

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYMSG)
            {
                int eventId = (int)lParam;
                if (eventId == WM_RBUTTONUP) { ShowTrayContextMenu(); handled = true; }
                else if (eventId == WM_LBUTTONUP) { ToggleWindow(); handled = true; }
            }
            else if (msg == 0x0312) // WM_HOTKEY
            {
                if (_hotkeyService is HotkeyService service)
                    service.HandleHotkeyMessage((int)wParam);
                handled = true;
            }
            else if (msg == WM_WTSSESSION_CHANGE && (int)wParam == WTS_SESSION_LOCK)
            {
                HideWindow();
            }

            return IntPtr.Zero;
        }

        // ── Deactivated (auto-hide) ───────────────────────────────────────────

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (this.Visibility == Visibility.Collapsed) return;

            IntPtr hwndForeground = GetForegroundWindow();
            if (hwndForeground != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwndForeground, out uint processId);
                uint currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                if (processId == currentPid) return; // focus moved to our own window (e.g. SettingsWindow)

                var sb = new StringBuilder(256);
                if (GetClassName(hwndForeground, sb, 256) > 0)
                {
                    string cls = sb.ToString();
                    if (cls.Contains("IME") || cls.Contains("InputMethod") ||
                        cls.Contains("MSCTFIME UI") || cls.Contains("Cand"))
                        return; // IME window
                }
            }

            HideWindow();
        }

        // ── System events ────────────────────────────────────────────────────

        private void SystemEvents_SessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
                ReloadConfigAndHotkeys();
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
                HideWindow();
        }

        private void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                ReloadConfigAndHotkeys();
        }

        // ── Settings & cleanup ────────────────────────────────────────────────

        private void OpenSettings()
        {
            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new SettingsWindow(_configService, _config, _serviceManager, _scheduleManager);
                _settingsWindow.ConfigSaved += ReloadConfigAndHotkeys;
            }
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        private void CloseApp()
        {
            Cleanup();
            Application.Current.Shutdown();
        }

        private void Cleanup()
        {
            _hotkeyService?.StopHook();
            if (_configWatcher != null)
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Dispose();
                _configWatcher = null;
            }
            Microsoft.Win32.SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
        }

        protected override void OnClosed(EventArgs e)
        {
            _serviceManager.StopAllServices();
            _scheduleManager.Dispose();
            Cleanup();
            base.OnClosed(e);
        }
    }
}