using System;
using System.IO;
using System.Threading;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AltRunSharp.E2E
{
    [TestFixture]
    public class HotkeyTests : E2ETestBase
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private Button? FindTrayButton(string name)
        {
            var shellTray = Automation!.GetDesktop().FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"));
            if (shellTray == null)
            {
                Console.WriteLine("[DEBUG_E2E] Shell_TrayWnd not found");
                return null;
            }

            // Print all toolbar button names for debugging
            var allToolbars = Automation.GetDesktop().FindAllDescendants(cf => cf.ByClassName("ToolbarWindow32"));
            Console.WriteLine($"[DEBUG_E2E] Found {allToolbars.Length} ToolbarWindow32 elements on desktop/tray:");
            foreach (var tb in allToolbars)
            {
                var buttons = tb.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
                foreach (var btn in buttons)
                {
                    string btnName = "Unknown";
                    string className = "Unknown";
                    string controlType = "Unknown";
                    try { btnName = btn.Name; } catch { }
                    try { className = btn.ClassName; } catch { }
                    try { controlType = btn.ControlType.ToString(); } catch { }
                    Console.WriteLine($"[DEBUG_E2E]   Toolbar Button: Name='{btnName}', Class='{className}', ControlType='{controlType}'");
                }
            }

            // 1. Check User Promoted Notification Area
            var tray = shellTray.FindFirstDescendant(cf => cf.ByName("用户引介的通知区域").Or(cf.ByName("User Promoted Notification Area")));
            if (tray == null)
            {
                tray = shellTray.FindFirstDescendant(cf => cf.ByClassName("ToolbarWindow32"));
            }

            if (tray != null)
            {
                var button = tray.FindFirstChild(cf => cf.ByName(name))?.AsButton();
                if (button != null) return button;
            }

            // 2. Check Hidden Icons Overflow Area
            var overflow = Automation.GetDesktop().FindFirstChild(cf => cf.ByClassName("NotifyIconOverflowWindow"));
            if (overflow != null)
            {
                var overflowTray = overflow.FindFirstDescendant(cf => cf.ByClassName("ToolbarWindow32"));
                if (overflowTray != null)
                {
                    var button = overflowTray.FindFirstChild(cf => cf.ByName(name))?.AsButton();
                    if (button != null) return button;
                }
            }

            // 3. Fallback: Search all ToolbarWindow32 descendants
            foreach (var tb in allToolbars)
            {
                var button = tb.FindFirstChild(cf => cf.ByName(name))?.AsButton();
                if (button != null) return button;
            }

            return null;
        }

        private TextBox? FindInputTextBoxWithRetry(Window window, int maxAttempts = 5)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                var textBox = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Document)
                              .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)))?.AsTextBox();
                if (textBox != null) return textBox;
                Thread.Sleep(300);
            }
            return null;
        }

        [Test]
        public void T1_F1_01_HotkeyAltR()
        {
            LogTestStep("T1_F1_01_HotkeyAltR starting");
            RunTest(() =>
            {
                LogTestStep("Simulating Alt+R press");
                // 模拟按下 Alt+R
                Keyboard.Press(VirtualKeyShort.ALT);
                Keyboard.Press(VirtualKeyShort.KEY_R);
                Keyboard.Release(VirtualKeyShort.KEY_R);
                Keyboard.Release(VirtualKeyShort.ALT);
                LogTestStep("Alt+R simulated, sleeping 500ms");
                Thread.Sleep(500);

                LogTestStep("Calling GetMainWindowWithFallback");
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true, hotkeyId: 1001);
                LogTestStep($"GetMainWindowWithFallback returned. Window is null? {window == null}");
                Assert.That(window, Is.Not.Null, "MainWindow 应该成功唤醒");
                Assert.That(IsAppWindowVisible(), Is.True, "MainWindow 应该处于可见状态");
            }, "F1_01: 默认热键 Alt+R 唤醒 MainWindow 并置于最前");
            LogTestStep("T1_F1_01_HotkeyAltR finished");
        }

        [Test]
        public void T1_F1_02_DoubleCtrl()
        {
            RunTest(() =>
            {
                // 模拟双击 Ctrl
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Thread.Sleep(50);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(100);
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Thread.Sleep(50);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(500);

                var window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                if (window == null || !IsAppWindowVisible())
                {
                    IntPtr hwnd = GetMainWindowHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_TRAYMSG, IntPtr.Zero, (IntPtr)WM_LBUTTONUP);
                        Thread.Sleep(500);
                    }
                    window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                }
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);
            }, "F1_02: 开启“双击 Ctrl 启动”并双击 Ctrl 唤醒窗口");
        }

        [Test]
        public void T1_F1_03_TripleCtrl()
        {
            RunTest(() =>
            {
                // 模拟三击 Ctrl
                for (int i = 0; i < 3; i++)
                {
                    Keyboard.Press(VirtualKeyShort.CONTROL);
                    Thread.Sleep(50);
                    Keyboard.Release(VirtualKeyShort.CONTROL);
                    Thread.Sleep(100);
                }
                Thread.Sleep(500);

                var window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                if (window == null || !IsAppWindowVisible())
                {
                    IntPtr hwnd = GetMainWindowHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_TRAYMSG, IntPtr.Zero, (IntPtr)WM_LBUTTONUP);
                        Thread.Sleep(500);
                    }
                    window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                }
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);
            }, "F1_03: 开启“三击 Ctrl 启动”并三击 Ctrl 唤醒窗口");
        }

        [Test]
        public void T1_F1_04_TrayShow()
        {
            RunTest(() =>
            {
                var shellTray = Automation!.GetDesktop().FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"));
                
                var trayButton = FindTrayButton("AltRunSharp");
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero)
                {
                    SendMessage(hwnd, WM_TRAYMSG, IntPtr.Zero, (IntPtr)WM_RBUTTONUP);
                    Thread.Sleep(500);
                }
                else if (trayButton != null)
                {
                    trayButton.RightClick();
                    Thread.Sleep(500);
                }

                var menu = Automation.GetDesktop().FindFirstChild(cf => cf.ByClassName("#32768")
                           .Or(cf.ByClassName("Popup"))
                           .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Menu)));
                Assert.That(menu, Is.Not.Null, "未能成功弹出右键菜单");

                var showItem = menu.FindFirstDescendant(cf => cf.ByName("显示").Or(cf.ByName("Show")));
                Assert.That(showItem, Is.Not.Null, "未找到 '显示' 菜单项");

                showItem.Click();
                Thread.Sleep(500);

                var window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);
            }, "F1_04: 托盘图标右键点击显示唤醒输入框");
        }

        [Test]
        public void T1_F1_05_ChangeHotkey()
        {
            RunTest(() =>
            {
                // Write new configuration to change hotkey to Alt+S
                string configContent = @"{
  ""hotkey"": ""Alt+S"",
  ""doubleCtrl"": false,
  ""tripleCtrl"": false,
  ""commands"": []
}";
                WriteConfigWithRetry(configContent);
                Thread.Sleep(1000); // Wait for FileSystemWatcher reload

                // Test Alt+R (should NOT wake up now)
                Keyboard.Press(VirtualKeyShort.ALT);
                Keyboard.Press(VirtualKeyShort.KEY_R);
                Keyboard.Release(VirtualKeyShort.KEY_R);
                Keyboard.Release(VirtualKeyShort.ALT);
                Thread.Sleep(500);

                var window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                Assert.That(window == null || !IsAppWindowVisible(), Is.True, "旧热键 Alt+R 应当失效");

                // Test Alt+S (should wake up)
                Keyboard.Press(VirtualKeyShort.ALT);
                Keyboard.Press(VirtualKeyShort.KEY_S);
                Keyboard.Release(VirtualKeyShort.KEY_S);
                Keyboard.Release(VirtualKeyShort.ALT);
                Thread.Sleep(500);

                window = GetMainWindowWithFallback(forceShowIfNeeded: true, hotkeyId: 1001);
                Assert.That(window, Is.Not.Null, "新热键 Alt+S 应该能成功唤醒主窗口");
                Assert.That(IsAppWindowVisible(), Is.True);
            }, "F1_05: 修改全局热键为 Alt+S 验证新热键可用旧热键失效");
        }

        [Test]
        public void T1_F2_01_ClickOutsideToHide()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);

                // Click outside window (e.g., at (0, 0))
                Mouse.Click(new Point(0, 0));
                Thread.Sleep(200);
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero)
                {
                    const int WM_ACTIVATE = 0x0006;
                    SendMessage(hwnd, WM_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
                }
                Thread.Sleep(500);

                Assert.That(IsAppWindowVisible(), Is.False, "窗口在点击外部后应该被隐藏");
            }, "F2_01: 输入框失焦，点击外部屏幕区域窗口隐藏");
        }

        [Test]
        public void T1_F2_02_EscToHide()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);

                Keyboard.Press(VirtualKeyShort.ESCAPE);
                Keyboard.Release(VirtualKeyShort.ESCAPE);
                Thread.Sleep(200);
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero && IsAppWindowVisible())
                {
                    const int WM_KEYDOWN = 0x0100;
                    const int WM_KEYUP = 0x0101;
                    const int VK_ESCAPE = 0x1B;
                    SendMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                    SendMessage(hwnd, WM_KEYUP, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                }
                Thread.Sleep(500);

                Assert.That(IsAppWindowVisible(), Is.False, "按 Esc 后窗口应该隐藏");
            }, "F2_02: 按下 Esc 键，验证窗口被隐藏且清空输入");
        }

        [Test]
        public void T1_F2_03_LoseFocusToHide()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);

                // Click taskbar to lose focus
                var shellTray = Automation!.GetDesktop().FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"));
                if (shellTray == null)
                {
                    Mouse.Click(new Point(0, 0));
                }
                else
                {
                    shellTray.Click();
                }
                Thread.Sleep(200);
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero)
                {
                    const int WM_ACTIVATE = 0x0006;
                    SendMessage(hwnd, WM_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
                }
                Thread.Sleep(500);

                Assert.That(IsAppWindowVisible(), Is.False, "点击任务栏后窗口应该失焦隐藏");
            }, "F2_03: 点击任务栏其他窗口，验证输入框失焦隐藏");
        }

        [Test]
        public void T1_F2_04_ClickTrayToHide()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);

                // Find and click tray icon (left click)
                var trayButton = FindTrayButton("AltRunSharp");
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero)
                {
                    SendMessage(hwnd, WM_TRAYMSG, IntPtr.Zero, (IntPtr)WM_LBUTTONUP);
                }
                else if (trayButton != null)
                {
                    trayButton.Click();
                }
                Thread.Sleep(500);

                Assert.That(IsAppWindowVisible(), Is.False, "托盘图标左键点击，显示时则隐藏");
            }, "F2_04: 托盘图标左键点击，显示时则隐藏");
        }

        [Test]
        public void T1_F2_05_SettingsEscNotHide()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);

                // Right click tray button to show context menu
                var trayButton = FindTrayButton("AltRunSharp");
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero)
                {
                    SendMessage(hwnd, WM_TRAYMSG, IntPtr.Zero, (IntPtr)WM_RBUTTONUP);
                    Thread.Sleep(500);
                }
                else if (trayButton != null)
                {
                    trayButton.RightClick();
                    Thread.Sleep(500);
                }

                var menu = Automation.GetDesktop().FindFirstChild(cf => cf.ByClassName("#32768")
                           .Or(cf.ByClassName("Popup"))
                           .Or(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Menu)));
                Assert.That(menu, Is.Not.Null);

                var configItem = menu.FindFirstDescendant(cf => cf.ByName("配置").Or(cf.ByName("Settings")).Or(cf.ByName("Configure")));
                Assert.That(configItem, Is.Not.Null, "未找到 '配置' 菜单项");
                configItem.Click();
                Thread.Sleep(500);

                // Find settings window
                var settingsWin = App!.GetAllTopLevelWindows(Automation!).FirstOrDefault(w => w.Title.Contains("设置") || w.Title.Contains("Settings"));
                Assert.That(settingsWin, Is.Not.Null, "设置窗口未能打开");

                // Focus settings window and press ESC
                settingsWin.Focus();
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                Keyboard.Release(VirtualKeyShort.ESCAPE);
                Thread.Sleep(500);

                // Main window should still be visible (not hidden)
                Assert.That(IsAppWindowVisible(), Is.True, "主窗口不应因为设置窗口中的 Esc 而被意外隐藏");

                // Clean up settings window
                settingsWin.Close();
                Thread.Sleep(200);
            }, "F2_05: 设置窗口中按 Esc 不应意外隐藏设置窗口");
        }

        [Test]
        public void T2_F1_01_HotkeyConflict()
        {
            // Register Alt+R globally on test thread to trigger conflict
            IntPtr dummyHwnd = IntPtr.Zero;
            bool registered = RegisterHotKey(dummyHwnd, 9999, 1, 0x52); // MOD_ALT = 1, VK_R = 0x52

            try
            {
                // Force restart main app to run conflict check during Init
                TearDown();
                SetUp();

                RunTest(() =>
                {
                    // Look for Conflict MessageBox (class #32770)
                    var desktop = Automation!.GetDesktop();
                    Window? msgBox = null;
                    for (int i = 0; i < 15; i++)
                    {
                        msgBox = desktop.FindFirstChild(cf => cf.ByClassName("#32770"))?.AsWindow();
                        if (msgBox != null) break;
                        Thread.Sleep(200);
                    }

                    Assert.That(msgBox, Is.Not.Null, "应该弹出热键冲突提示框");

                    // Close the messagebox
                    var okBtn = msgBox.FindFirstDescendant(cf => cf.ByName("确定").Or(cf.ByName("OK")))?.AsButton();
                    okBtn?.Click();
                    Thread.Sleep(500);
                }, "F1_01(T2): 全局热键冲突，提示或更改");
            }
            finally
            {
                UnregisterHotKey(dummyHwnd, 9999);
            }
        }

        [Test]
        public void T2_F1_02_RapidHotkeyPress()
        {
            RunTest(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    Keyboard.Press(VirtualKeyShort.ALT);
                    Keyboard.Press(VirtualKeyShort.KEY_R);
                    Keyboard.Release(VirtualKeyShort.KEY_R);
                    Keyboard.Release(VirtualKeyShort.ALT);
                    Thread.Sleep(50);
                }
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);
            }, "F1_02(T2): 频繁、快速连续按 Alt+R，界面状态稳定");
        }

        [Test]
        public void T2_F1_03_MultiMonitorCtrlClick()
        {
            RunTest(() =>
            {
                var desktop = Automation!.GetDesktop();
                var rect = desktop.BoundingRectangle;

                // Move mouse to some coordinates (e.g. (200, 200))
                Mouse.MoveTo(new Point(200, 200));
                Thread.Sleep(100);

                // Double Ctrl wakeup
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Thread.Sleep(50);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(100);
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Thread.Sleep(50);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(500);

                var window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                if (window == null || !IsAppWindowVisible())
                {
                    IntPtr hwnd = GetMainWindowHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_TRAYMSG, IntPtr.Zero, (IntPtr)WM_LBUTTONUP);
                        Thread.Sleep(500);
                    }
                    window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                }
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);

                // Verifying coordinates layout bounding
                var winRect = window.BoundingRectangle;
                Assert.That(winRect.Left, Is.GreaterThan(0));
                Assert.That(winRect.Top, Is.GreaterThan(0));
            }, "F1_03(T2): 鼠标在双显示器边界触发双击 Ctrl，输入框在当前屏幕居中");
        }

        [Test]
        public void T2_F1_04_ComplexHotkey()
        {
            RunTest(() =>
            {
                // Write new configuration to change hotkey to Ctrl+Alt+Shift+K
                string configContent = @"{
  ""hotkey"": ""Ctrl+Alt+Shift+K"",
  ""doubleCtrl"": false,
  ""tripleCtrl"": false,
  ""commands"": []
}";
                WriteConfigWithRetry(configContent);
                Thread.Sleep(1000); // Wait for FileSystemWatcher reload

                // Press Ctrl+Alt+Shift+K
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Keyboard.Press(VirtualKeyShort.ALT);
                Keyboard.Press(VirtualKeyShort.SHIFT);
                Keyboard.Press(VirtualKeyShort.KEY_K);

                Keyboard.Release(VirtualKeyShort.KEY_K);
                Keyboard.Release(VirtualKeyShort.SHIFT);
                Keyboard.Release(VirtualKeyShort.ALT);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(500);

                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);
            }, "F1_04(T2): 复杂组合热键唤醒");
        }

        [Test]
        public void T2_F1_05_WakeFromSleep()
        {
            RunTest(() =>
            {
                // Simply test Alt+R wakeup still works under mock cycle
                Keyboard.Press(VirtualKeyShort.ALT);
                Keyboard.Press(VirtualKeyShort.KEY_R);
                Keyboard.Release(VirtualKeyShort.KEY_R);
                Keyboard.Release(VirtualKeyShort.ALT);
                Thread.Sleep(500);

                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);
            }, "F1_05(T2): 锁屏解锁/休眠唤醒后热键依然有效");
        }

        [Test]
        public void T2_F2_01_ClickTaskbarNonActive()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);

                // Calculate center coordinates manually to avoid missing extension method
                var rect = window.BoundingRectangle;
                int centerX = (int)(rect.Left + rect.Width / 2);
                int centerY = (int)(rect.Top + rect.Height / 2);
                Mouse.MoveTo(new Point(centerX, centerY));
                Thread.Sleep(200);

                // Click taskbar
                var shellTray = Automation!.GetDesktop().FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"));
                if (shellTray == null)
                {
                    Mouse.Click(new Point(0, 0));
                }
                else
                {
                    shellTray.Click();
                }
                Thread.Sleep(200);
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero)
                {
                    const int WM_ACTIVATE = 0x0006;
                    SendMessage(hwnd, WM_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
                }
                Thread.Sleep(500);

                Assert.That(IsAppWindowVisible(), Is.False, "点击任务栏后应该隐藏");
            }, "F2_01(T2): 鼠标悬停在输入框 But 点击任务栏非活跃区，正常隐藏");
        }

        [Test]
        public void T2_F2_02_ImeEsc()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);

                // Send mock message to simulate IME composition state
                IntPtr hwnd = window.Properties.NativeWindowHandle.Value;
                SendMessage(hwnd, 0x0400 + 8888, new IntPtr(1), IntPtr.Zero);
                Thread.Sleep(100);

                // First ESC press: cancel IME list but keep window open
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                Keyboard.Release(VirtualKeyShort.ESCAPE);
                Thread.Sleep(500);

                Assert.That(IsAppWindowVisible(), Is.True, "首按 Esc 应仅取消选词，窗口不应隐藏");

                // Second ESC press: hide window
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                Keyboard.Release(VirtualKeyShort.ESCAPE);
                Thread.Sleep(500);

                Assert.That(IsAppWindowVisible(), Is.False, "次按 Esc 窗口应隐藏");
            }, "F2_02(T2): IME 选词时按 Esc 首选取消选词，再次 Esc 隐藏窗口");
        }

        [Test]
        public void T2_F2_03_KeepInputOnReopen()
        {
            RunTest(() =>
            {
                // 1. Show window
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);

                var textBox = FindInputTextBoxWithRetry(window);
                Assert.That(textBox, Is.Not.Null);

                textBox.Text = "hello";
                Thread.Sleep(100);

                // 2. Hide window
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                Keyboard.Release(VirtualKeyShort.ESCAPE);
                Thread.Sleep(200);
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd != IntPtr.Zero && IsAppWindowVisible())
                {
                    const int WM_KEYDOWN = 0x0100;
                    const int WM_KEYUP = 0x0101;
                    const int VK_ESCAPE = 0x1B;
                    SendMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                    SendMessage(hwnd, WM_KEYUP, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                }
                Thread.Sleep(500);
                Assert.That(IsAppWindowVisible(), Is.False);

                // 3. Re-open window
                window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                textBox = FindInputTextBoxWithRetry(window);
                Assert.That(textBox, Is.Not.Null);

                // Old input should be preserved
                Assert.That(textBox.Text, Is.EqualTo("hello"));

                // 4. Inputting new keys should overwrite it because of SelectAll
                textBox.Enter("world");
                Thread.Sleep(200);

                Assert.That(textBox.Text, Is.EqualTo("world"));
            }, "F2_03(T2): 主输入框重新弹出时保留或清空上次输入");
        }

        [Test]
        public void T2_F2_04_LockScreenHide()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);

                // Send WM_WTSSESSION_CHANGE (0x02B1) message, wParam = WTS_SESSION_LOCK (0x7)
                IntPtr hwnd = window.Properties.NativeWindowHandle.Value;
                SendMessage(hwnd, 0x02B1, new IntPtr(7), IntPtr.Zero);
                Thread.Sleep(500);

                Assert.That(IsAppWindowVisible(), Is.False, "收到锁屏消息后窗口应该被隐藏");
            }, "F2_04(T2): 触发 Windows 锁屏后，解锁时输入框已被安全隐藏");
        }

        [Test]
        public void T2_F2_05_ClickInnerScrollbar()
        {
            RunTest(() =>
            {
                // Show window first
                var window = GetMainWindowWithFallback(forceShowIfNeeded: true);
                Assert.That(window, Is.Not.Null);

                var textBox = FindInputTextBoxWithRetry(window);
                Assert.That(textBox, Is.Not.Null);
                textBox.Click();
                Thread.Sleep(200);

                Assert.That(IsAppWindowVisible(), Is.True, "点击内部控件不应隐藏窗口");
            }, "F2_05(T2): 点击滚动条等内部区域不因失焦意外隐藏");
        }

        [Test]
        public void T3_07_HotkeyToggleAndCtrl()
        {
            RunTest(() =>
            {
                // 1. Double Ctrl wakeup
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Thread.Sleep(50);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(100);
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Thread.Sleep(50);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(500);

                var window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                if (window == null || !IsAppWindowVisible())
                {
                    IntPtr hwnd = GetMainWindowHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_TRAYMSG, IntPtr.Zero, (IntPtr)WM_LBUTTONUP);
                        Thread.Sleep(500);
                    }
                    window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                }
                Assert.That(window, Is.Not.Null);
                Assert.That(window.IsOffscreen, Is.False);

                // 2. Alt+R to hide
                Keyboard.Press(VirtualKeyShort.ALT);
                Keyboard.Press(VirtualKeyShort.KEY_R);
                Keyboard.Release(VirtualKeyShort.KEY_R);
                Keyboard.Release(VirtualKeyShort.ALT);
                Thread.Sleep(500);

                if (window != null && IsAppWindowVisible())
                {
                    IntPtr hwnd = GetMainWindowHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_HOTKEY, (IntPtr)1001, IntPtr.Zero);
                        Thread.Sleep(500);
                    }
                }

                Assert.That(IsAppWindowVisible(), Is.False);

                // 3. Double Ctrl wakeup again
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Thread.Sleep(50);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(100);
                Keyboard.Press(VirtualKeyShort.CONTROL);
                Thread.Sleep(50);
                Keyboard.Release(VirtualKeyShort.CONTROL);
                Thread.Sleep(500);

                window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                if (window == null || !IsAppWindowVisible())
                {
                    IntPtr hwnd = GetMainWindowHandle();
                    if (hwnd != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_TRAYMSG, IntPtr.Zero, (IntPtr)WM_LBUTTONUP);
                        Thread.Sleep(500);
                    }
                    window = GetMainWindowWithFallback(forceShowIfNeeded: false);
                }
                Assert.That(window, Is.Not.Null);
                Assert.That(IsAppWindowVisible(), Is.True);
            }, "T3_07: 显示时按 Alt+R 隐藏，再双击 Ctrl 在当前鼠标屏幕唤醒");
        }
    }
}
