using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using FlaUI.Core;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;

namespace AltRunSharp.E2E
{
    public class E2ETestBase
    {
        protected void LogTestStep(string msg)
        {
            try
            {
                string logPath = @"c:\D\VSProjects\AltRunSharp\.agents\worker_m1_gen3\tests_progress.log";
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [PID:{System.Diagnostics.Process.GetCurrentProcess().Id}] {msg}\r\n");
            }
            catch {}
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        protected static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        protected static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        protected const int WM_HOTKEY = 0x0312;
        protected const int WM_USER = 0x0400;
        protected const int WM_TRAYMSG = WM_USER + 1024;
        protected const int WM_LBUTTONUP = 0x0202;
        protected const int WM_RBUTTONUP = 0x0208;

        protected IntPtr GetMainWindowHandle()
        {
            if (App == null) return IntPtr.Zero;
            
            try
            {
                var proc = Process.GetProcessById(App.ProcessId);
                if (proc != null)
                {
                    proc.Refresh();
                    IntPtr handle = proc.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        return handle;
                    }
                }
            }
            catch {}

            IntPtr foundHandle = IntPtr.Zero;
            int processId = App.ProcessId;
            EnumWindows((hwnd, lParam) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == processId)
                {
                    var className = new System.Text.StringBuilder(256);
                    GetClassName(hwnd, className, 256);
                    var title = new System.Text.StringBuilder(256);
                    GetWindowText(hwnd, title, 256);

                    string cls = className.ToString();
                    string tit = title.ToString();

                    if (tit == "AltRunSharp")
                    {
                        foundHandle = hwnd;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return foundHandle;
        }

        protected Window? GetMainWindowWithFallback(bool forceShowIfNeeded = false, int hotkeyId = 1001)
        {
            LogTestStep("GetMainWindowWithFallback called");
            if (App == null || Automation == null)
            {
                LogTestStep("App or Automation is null");
                return null;
            }

            IntPtr hwnd = GetMainWindowHandle();
            LogTestStep($"GetMainWindowHandle returned: {hwnd}");

            if (hwnd == IntPtr.Zero)
            {
                LogTestStep("Failed to get window handle, sleeping 1000ms and retrying");
                System.Threading.Thread.Sleep(1000);
                hwnd = GetMainWindowHandle();
                LogTestStep($"Retry GetMainWindowHandle returned: {hwnd}");
            }

            if (hwnd == IntPtr.Zero)
            {
                LogTestStep("Window handle still Zero, falling back to GetMainWindow");
                return App.GetMainWindow(Automation);
            }

            if (forceShowIfNeeded)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (IsWindowVisible(hwnd))
                    {
                        break;
                    }
                    LogTestStep($"Window is hidden, sending WM_HOTKEY to wake it up (attempt {i + 1})");
                    SendMessage(hwnd, WM_HOTKEY, (IntPtr)hotkeyId, IntPtr.Zero);
                    System.Threading.Thread.Sleep(300);
                }
            }

            try
            {
                LogTestStep("Retrieving window from handle via Automation.FromHandle");
                var element = Automation.FromHandle(hwnd);
                var win = element?.AsWindow();
                LogTestStep($"Automation.FromHandle returned. IsNull? {win == null}");
                return win;
            }
            catch (Exception ex)
            {
                LogTestStep($"Exception in Automation.FromHandle: {ex.Message}");
                return App.GetMainWindow(Automation);
            }
        }

        protected bool IsAppWindowVisible()
        {
            IntPtr hwnd = GetMainWindowHandle();
            if (hwnd == IntPtr.Zero) return false;
            return IsWindowVisible(hwnd);
        }

        protected AltRunApplication? App { get; private set; }
        
        private static UIA3Automation? _staticAutomation;
        private static readonly object _automationLock = new object();

        protected UIA3Automation? Automation
        {
            get
            {
                if (ShouldLaunchApp())
                {
                    bool exited = false;
                    try
                    {
                        exited = App == null || App.HasExited;
                    }
                    catch
                    {
                        exited = true;
                    }
                    if (exited)
                    {
                        Assert.Fail("主程序未成功启动或已提前退出/闪退，无法进行 UI 交互。");
                    }
                }

                if (_staticAutomation == null)
                {
                    lock (_automationLock)
                    {
                        if (_staticAutomation == null)
                        {
                            try
                            {
                                _staticAutomation = new UIA3Automation();
                                Console.WriteLine("[DEBUG_E2E] Static UIA3Automation instantiated successfully");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[DEBUG_E2E] Failed to instantiate static UIA3Automation: {ex}");
                            }
                        }
                    }
                }
                return _staticAutomation;
            }
        }

        public static void DisposeStaticAutomation()
        {
            try
            {
                if (_staticAutomation != null)
                {
                    _staticAutomation.Dispose();
                    _staticAutomation = null;
                    Console.WriteLine("[DEBUG_E2E] Static UIA3Automation disposed in GlobalSetup");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG_E2E] Failed to dispose static UIA3Automation: {ex}");
            }
        }

        protected string SolutionRoot { get; private set; } = string.Empty;
        protected string ConfigPath { get; private set; } = string.Empty;

        protected System.Collections.Generic.List<Process> TrackedProcesses { get; } = new System.Collections.Generic.List<Process>();
        private System.Collections.Generic.HashSet<int> _preExistingProcessIds = new System.Collections.Generic.HashSet<int>();
        private readonly string[] _orphanProcessNames = new[] { "notepad", "calc", "node", "AltRunSharp" };

        [OneTimeSetUp]
        public virtual void OneTimeSetUp()
        {
            LogTestStep("OneTimeSetUp started");
            ConfigureFlaUITimeouts();
            LogTestStep("OneTimeSetUp finished");
        }

        [OneTimeTearDown]
        public virtual void OneTimeTearDown()
        {
            // Static automation is disposed globally at the end of all test fixtures
        }

        protected virtual bool ShouldLaunchApp()
        {
            var testName = TestContext.CurrentContext.Test.Name ?? string.Empty;
            var className = TestContext.CurrentContext.Test.ClassName ?? string.Empty;

            if (className.Contains("ConfigTests") && !testName.Contains("CorruptConfigDowngrade"))
            {
                return false;
            }
            if (className.Contains("ExecutorTests"))
            {
                return false;
            }
            return true;
        }

        private void ConfigureFlaUITimeouts()
        {
            try
            {
                Console.WriteLine("[DEBUG_E2E] Configuring FlaUI timeouts via precise reflection...");
                var targetValue = TimeSpan.FromSeconds(2.0);

                // 1. 尝试配置 Globals.Timeouts
                var globalsType = typeof(FlaUI.Core.Application).Assembly.GetType("FlaUI.Core.Globals");
                if (globalsType != null)
                {
                    var timeoutsProp = globalsType.GetProperty("Timeouts", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (timeoutsProp != null)
                    {
                        var timeoutsObj = timeoutsProp.GetValue(null);
                        if (timeoutsObj != null)
                        {
                            var props = timeoutsObj.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            foreach (var prop in props)
                            {
                                if (prop.PropertyType == typeof(TimeSpan) && prop.CanWrite)
                                {
                                    try
                                    {
                                        prop.SetValue(timeoutsObj, targetValue);
                                        Console.WriteLine($"[DEBUG_E2E] Set FlaUI.Core.Globals.Timeouts instance property {prop.Name} to {targetValue}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[DEBUG_E2E] Failed to set property {prop.Name}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                // 2. 尝试配置可能存在的 TimeoutConfiguration 或 Timeouts 静态属性
                var configTypes = new[] { "FlaUI.Core.Configuration.TimeoutConfiguration", "FlaUI.Core.Configuration.Timeouts", "FlaUI.Core.Configuration.Configuration" };
                foreach (var typeName in configTypes)
                {
                    try
                    {
                        var configType = typeof(FlaUI.Core.Application).Assembly.GetType(typeName);
                        if (configType != null)
                        {
                            var props = configType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            foreach (var prop in props)
                            {
                                if (prop.PropertyType == typeof(TimeSpan) && prop.CanWrite)
                                {
                                    try
                                    {
                                        prop.SetValue(null, targetValue);
                                        Console.WriteLine($"[DEBUG_E2E] Set static timeout property {configType.Name}.{prop.Name} to {targetValue}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[DEBUG_E2E] Failed to set static property {configType.Name}.{prop.Name}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG_E2E] Failed to configure timeouts via reflection: {ex}");
            }
        }

        [SetUp]
        public virtual void SetUp()
        {
            LogTestStep("SetUp started");
            SolutionRoot = FindSolutionRoot();
            ConfigPath = Path.Combine(SolutionRoot, "data", "config.json");

            LogTestStep("RecordPreExistingProcesses starting");
            RecordPreExistingProcesses();
            LogTestStep("RecordPreExistingProcesses finished");

            LogTestStep("KillExistingProcesses starting");
            KillExistingProcesses();
            LogTestStep("KillExistingProcesses finished");

            LogTestStep("ResetConfigToDefault starting");
            ResetConfigToDefault();
            LogTestStep("ResetConfigToDefault finished");

            ClearAppStartupLog();

            // 3. 启动主程序进程 (如果当前测试需要启动 GUI 窗口)
            if (ShouldLaunchApp())
            {
                string exePath = FindExePath();
                LogTestStep($"App launching from {exePath}");
                if (!File.Exists(exePath))
                {
                    Console.WriteLine($"[Warning] AltRunSharp.exe not found at: {exePath}. Please build the project first.");
                    LogTestStep($"AltRunSharp.exe not found at: {exePath}");
                }

                try
                {
                    if (File.Exists(exePath))
                    {
                        Console.WriteLine($"[DEBUG_E2E] Launching app from: {exePath}");
                        var originalApp = FlaUI.Core.Application.Launch(exePath);
                        App = new AltRunApplication(originalApp);
                        Console.WriteLine("[DEBUG_E2E] App launched successfully");
                        LogTestStep("App launched");
                        
                        WaitForWindowReady();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG_E2E] Failed to launch AltRunSharp: {ex}");
                    LogTestStep($"App launch failed: {ex}");
                }
            }
            else
            {
                Console.WriteLine("[DEBUG_E2E] Skipping App launch for non-UI/logic test");
                LogTestStep("Skipping App launch for non-UI/logic test");
            }
            LogTestStep("SetUp finished");
        }

        private string GetAppStartupLogPath()
        {
            return @"c:\D\VSProjects\AltRunSharp\.agents\worker_m1_gen3\app_startup.log";
        }

        private void ClearAppStartupLog()
        {
            string path = GetAppStartupLogPath();
            int attempts = 5;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.WriteAllText(path, "# APP LOG START\r\n");
                    }
                    else
                    {
                        string dir = Path.GetDirectoryName(path)!;
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        File.WriteAllText(path, "# APP LOG START\r\n");
                    }
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        private void WaitForWindowReady()
        {
            LogTestStep("WaitForWindowReady waiting for Hiding window finished");
            string path = GetAppStartupLogPath();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 8000)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs))
                        {
                            string content = sr.ReadToEnd();
                            if (content.Contains("Hiding window finished"))
                            {
                                LogTestStep($"WaitForWindowReady completed in {stopwatch.ElapsedMilliseconds}ms");
                                return;
                            }
                        }
                    }
                }
                catch {}
                System.Threading.Thread.Sleep(50);
            }
            LogTestStep($"WaitForWindowReady timed out after {stopwatch.ElapsedMilliseconds}ms");
        }

        [TearDown]
        public virtual void TearDown()
        {
            try
            {
                if (App != null)
                {
                    var process = App.Process;
                    if (process != null && !process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to kill App in TearDown: {ex.Message}");
            }
            finally
            {
                KillExistingProcesses();
                App?.Dispose();
                App = null;
                CleanUpConfig();
            }
        }

        protected void RunTest(Action action, string featureDescription)
        {
            try
            {
                if (ShouldLaunchApp())
                {
                    if (App == null)
                    {
                        Assert.Fail($"功能尚未实现: {featureDescription}. 主程序未能成功启动 (App 为空或未编译生成 exe)。");
                    }
                    if (App.HasExited)
                    {
                        Assert.Fail($"功能尚未实现: {featureDescription}. 主程序已提前退出/闪退。");
                    }
                }
                action();
            }
            catch (System.TimeoutException ex)
            {
                Assert.Fail($"功能尚未实现: {featureDescription}. 操作超时. 详情: {ex.Message}");
            }
            catch (NUnit.Framework.AssertionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Assert.Fail($"功能尚未实现: {featureDescription}. 发生异常/未实现行为. 详情: {ex.Message}");
            }
        }

        private string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AltRunSharp.csproj")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            // Fallback to current directory
            return Directory.GetCurrentDirectory();
        }

        private string FindExePath()
        {
            // 优先查找 Release 下的，其次 Debug
            string releasePath = Path.Combine(SolutionRoot, "bin", "Release", "net10.0-windows", "AltRunSharp.exe");
            if (File.Exists(releasePath))
            {
                return releasePath;
            }
            string debugPath = Path.Combine(SolutionRoot, "bin", "Debug", "net10.0-windows", "AltRunSharp.exe");
            if (File.Exists(debugPath))
            {
                return debugPath;
            }
            // 默认返回 releasePath
            return releasePath;
        }

        private void KillExistingProcesses()
        {
            // 1. 清理在测试中启动的 TrackedProcesses
            foreach (var process in TrackedProcesses)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                }
                catch
                {
                    // Ignore
                }
                finally
                {
                    process.Dispose();
                }
            }
            TrackedProcesses.Clear();

            // 2.5. 仅清理当前实例管理的 AltRunSharp 进程，不根据名称全局强杀以避免测试并发冲突
            if (App != null)
            {
                try
                {
                    var process = App.Process;
                    if (process != null && !process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            // 3. 清理测试期间拉起的后台孤儿进程（如 notepad, calc, node, dotnet 等）
            foreach (var name in _orphanProcessNames)
            {
                try
                {
                    foreach (var process in Process.GetProcessesByName(name))
                     {
                        try
                        {
                            // 如果不在测试开始前已有的列表中，且不是当前进程本身，则是本测试产生的孤儿进程
                            if (process.Id != Process.GetCurrentProcess().Id && !_preExistingProcessIds.Contains(process.Id))
                            {
                                process.Kill();
                                process.WaitForExit(1000);
                            }
                        }
                        catch
                        {
                            // Ignore
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }

        private void RecordPreExistingProcesses()
        {
            _preExistingProcessIds.Clear();
            foreach (var name in _orphanProcessNames)
            {
                try
                {
                    foreach (var process in Process.GetProcessesByName(name))
                    {
                        _preExistingProcessIds.Add(process.Id);
                        process.Dispose();
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }

        protected void WriteConfigWithRetry(string content)
        {
            int attempts = 4;
            int[] delays = { 100, 200, 400 };
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    string dir = Path.GetDirectoryName(ConfigPath)!;
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllText(ConfigPath, content);
                    return;
                }
                catch (IOException ex) when (i < attempts - 1)
                {
                    Console.WriteLine($"[Warning] IOException during writing config (attempt {i + 1}/{attempts}), retrying in {delays[i]}ms... Exception: {ex.Message}");
                    System.Threading.Thread.Sleep(delays[i]);
                }
            }
        }

        protected void DeleteConfigWithRetry()
        {
            int attempts = 4;
            int[] delays = { 100, 200, 400 };
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        File.Delete(ConfigPath);
                    }
                    return;
                }
                catch (IOException ex) when (i < attempts - 1)
                {
                    Console.WriteLine($"[Warning] IOException during deleting config (attempt {i + 1}/{attempts}), retrying in {delays[i]}ms... Exception: {ex.Message}");
                    System.Threading.Thread.Sleep(delays[i]);
                }
            }
        }

        protected void ResetConfigToDefault()
        {
            try
            {
                string defaultConfigJson = @"{
  ""hotkey"": ""Alt+R"",
  ""doubleCtrl"": true,
  ""tripleCtrl"": true,
  ""commands"": []
 }";
                WriteConfigWithRetry(defaultConfigJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to reset config: {ex.Message}");
            }
        }

        protected void CleanUpConfig()
        {
            try
            {
                DeleteConfigWithRetry();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to clean up config: {ex.Message}");
            }
        }
    }

    [SetUpFixture]
    public class GlobalSetup
    {
        [OneTimeTearDown]
        public void RunAfterAnyTests()
        {
            E2ETestBase.DisposeStaticAutomation();
        }
    }

    public class AltRunApplication : IDisposable
    {
        private readonly FlaUI.Core.Application _app;

        public AltRunApplication(FlaUI.Core.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public FlaUI.Core.Application UnderlyingApp => _app;

        public int ProcessId => _app.ProcessId;
        public bool HasExited => _app.HasExited;
        public System.Diagnostics.Process? Process
        {
            get
            {
                try
                {
                    return System.Diagnostics.Process.GetProcessById(_app.ProcessId);
                }
                catch
                {
                    return null;
                }
            }
        }

        public void Close() => _app.Close();
        public void Dispose() => _app.Dispose();

        public FlaUI.Core.AutomationElements.Window[] GetAllTopLevelWindows(FlaUI.Core.AutomationBase automation)
        {
            return _app.GetAllTopLevelWindows(automation);
        }

        public FlaUI.Core.AutomationElements.Window? GetMainWindow(FlaUI.Core.AutomationBase automation, TimeSpan? dispatchTimeout = null)
        {
            // 1. Try retrieving from GetAllTopLevelWindows to find visible/hidden window by title/class without blocking
            try
            {
                var windows = _app.GetAllTopLevelWindows(automation);
                foreach (var win in windows)
                {
                    if (win.Title == "AltRunSharp")
                    {
                        return win;
                    }
                }
            }
            catch {}

            // 2. Try retrieving window handle using EnumWindows (even if the window is hidden/collapsed)
            IntPtr hwnd = GetMainWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    var element = automation.FromHandle(hwnd);
                    if (element != null)
                    {
                        return element.AsWindow();
                    }
                }
                catch {}
            }

            // 3. Fallback to default FlaUI GetMainWindow
            return _app.GetMainWindow(automation, dispatchTimeout);
        }

        private IntPtr GetMainWindowHandle()
        {
            try
            {
                var process = Process;
                if (process != null)
                {
                    process.Refresh();
                    IntPtr handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        return handle;
                    }
                }
            }
            catch {}

            IntPtr foundHandle = IntPtr.Zero;
            int processId = _app.ProcessId;
            EnumWindows((hwnd, lParam) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == processId)
                {
                    var className = new System.Text.StringBuilder(256);
                    GetClassName(hwnd, className, 256);
                    var title = new System.Text.StringBuilder(256);
                    GetWindowText(hwnd, title, 256);

                    string tit = title.ToString();
                    if (tit == "AltRunSharp")
                    {
                        foundHandle = hwnd;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return foundHandle;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    }
}
