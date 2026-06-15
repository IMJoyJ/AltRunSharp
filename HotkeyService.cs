using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AltRunSharp
{
    public class HotkeyService : IHotkeyService
    {
        private readonly IntPtr _hWnd;
        private readonly Dispatcher _dispatcher;

        // ── RegisterHotKey ───────────────────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_NOREPEAT = 0x4000;
        private readonly int _hotkeyId = 1001;

        // ── Low-level keyboard hook ──────────────────────────────────────────
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN    = 0x0100;
        private const int WM_KEYUP      = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP   = 0x0105;

        private const uint VK_LSHIFT   = 0xA0;
        private const uint VK_RSHIFT   = 0xA1;
        private const uint VK_LCONTROL = 0xA2;
        private const uint VK_RCONTROL = 0xA3;
        private const uint VK_LMENU    = 0xA4;  // Left Alt
        private const uint VK_RMENU    = 0xA5;  // Right Alt
        private const uint VK_LWIN     = 0x5B;
        private const uint VK_RWIN     = 0x5C;
        private const uint VK_SHIFT    = 0x10;
        private const uint VK_CONTROL  = 0x11;
        private const uint VK_MENU     = 0x12;  // Alt

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private static LowLevelKeyboardProc? _hookDelegate;
        private IntPtr _hhk = IntPtr.Zero;

        // ── Current hotkey configuration ─────────────────────────────────────
        private Action? _callback;
        private int _targetCount = 1;            // 1=single,2=double,3=triple
        private bool _isModifierOnly = false;
        private uint _modOnlyVK = 0;             // VK of the single modifier key

        // ── Consecutive-activation counting ─────────────────────────────────
        private int _activationCount = 0;
        private DateTime _lastActivationTime = DateTime.MinValue;
        private const int MULTI_WINDOW_MS = 500;

        // ── Modifier-only hook state ─────────────────────────────────────────
        // Track whether any non-modifier key was pressed since the target modifier went down
        private bool _modDownClean = false;

        // ────────────────────────────────────────────────────────────────────

        public HotkeyService(IntPtr hWnd)
        {
            _hWnd = hWnd;
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        public void RegisterGlobalHotkey(string keyCombination, string clickMode, Action callback)
        {
            _callback = callback;
            _targetCount = clickMode == "triple" ? 3 : clickMode == "double" ? 2 : 1;
            _activationCount = 0;
            _lastActivationTime = DateTime.MinValue;

            // Stop any previous hook / registration
            UnregisterHotKey(_hWnd, _hotkeyId);
            StopHook();

            if (string.IsNullOrWhiteSpace(keyCombination))
                return;

            bool parsed = ParseKeyCombination(keyCombination, out uint modifiers, out uint vk);
            _isModifierOnly = parsed && vk == 0;

            if (_isModifierOnly)
            {
                // Modifier-only (e.g. "Ctrl", "Alt", "Shift"): use keyboard hook
                _modOnlyVK = GetModifierFamilyVK(modifiers);
                _modDownClean = false;
                StartHook();
            }
            else if (parsed)
            {
                // Combination key: use RegisterHotKey (+ count in WM_HOTKEY for multi-click)
                bool ok = RegisterHotKey(_hWnd, _hotkeyId, modifiers | MOD_NOREPEAT, vk);
                if (!ok)
                {
                    var thread = new System.Threading.Thread(() =>
                    {
                        System.Windows.MessageBox.Show(
                            $"全局热键 [{keyCombination}] 注册冲突！请在配置中更改。",
                            "AltRunSharp - 热键冲突",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    });
                    thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    thread.IsBackground = true;
                    thread.Start();
                }
            }
        }

        public void HandleHotkeyMessage(int id)
        {
            if (id == _hotkeyId)
                TriggerActivation();
        }

        public void StopHook()
        {
            if (_hhk != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hhk);
                _hhk = IntPtr.Zero;
            }
            _hookDelegate = null;
        }

        // ── Activation counting ──────────────────────────────────────────────

        private void TriggerActivation()
        {
            var now = DateTime.UtcNow;
            double elapsed = (now - _lastActivationTime).TotalMilliseconds;

            if (elapsed > MULTI_WINDOW_MS)
                _activationCount = 1;
            else
                _activationCount++;

            _lastActivationTime = now;

            if (_activationCount >= _targetCount)
            {
                _activationCount = 0;
                _dispatcher.BeginInvoke(new Action(() => _callback?.Invoke()));
            }
        }

        // ── Low-level keyboard hook ──────────────────────────────────────────

        private void StartHook()
        {
            if (_hhk != IntPtr.Zero) return;

            _hookDelegate = HookCallback;
            // GetModuleHandle(null) returns the EXE module handle; works in both
            // normal and SingleFile publish (GetHINSTANCE is unreliable in SingleFile).
            IntPtr hMod = GetModuleHandle(null);

            _hhk = SetWindowsHookEx(WH_KEYBOARD_LL, _hookDelegate, hMod, 0);
            if (_hhk == IntPtr.Zero)
                _hhk = SetWindowsHookEx(WH_KEYBOARD_LL, _hookDelegate, IntPtr.Zero, 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && lParam != IntPtr.Zero)
            {
                try
                {
                    int msg = (int)wParam;
                    var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    uint vk = kb.vkCode;

                    bool isKeyDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                    bool isKeyUp   = (msg == WM_KEYUP   || msg == WM_SYSKEYUP);

                    // Is the pressed key our target modifier?
                    bool isTargetMod = IsTargetModifier(vk);
                    // Is it any modifier key at all?
                    bool isAnyMod = IsAnyModifier(vk);

                    if (isKeyDown)
                    {
                        if (isTargetMod)
                            _modDownClean = true;
                        else if (!isAnyMod)
                            _modDownClean = false; // non-modifier key pressed → not a clean modifier tap
                    }
                    else if (isKeyUp && isTargetMod)
                    {
                        if (_modDownClean)
                        {
                            _modDownClean = false;
                            // Dispatch to UI thread to count activation
                            _dispatcher.BeginInvoke(new Action(TriggerActivation));
                        }
                    }
                }
                catch { }
            }

            try { return CallNextHookEx(_hhk, nCode, wParam, lParam); }
            catch { return IntPtr.Zero; }
        }

        private bool IsTargetModifier(uint vk)
        {
            return _modOnlyVK switch
            {
                VK_CONTROL => vk == VK_LCONTROL || vk == VK_RCONTROL || vk == VK_CONTROL,
                VK_MENU    => vk == VK_LMENU    || vk == VK_RMENU    || vk == VK_MENU,
                VK_SHIFT   => vk == VK_LSHIFT   || vk == VK_RSHIFT   || vk == VK_SHIFT,
                VK_LWIN    => vk == VK_LWIN     || vk == VK_RWIN,
                _          => false
            };
        }

        private static bool IsAnyModifier(uint vk)
            => vk == VK_LSHIFT || vk == VK_RSHIFT || vk == VK_SHIFT ||
               vk == VK_LCONTROL || vk == VK_RCONTROL || vk == VK_CONTROL ||
               vk == VK_LMENU || vk == VK_RMENU || vk == VK_MENU ||
               vk == VK_LWIN || vk == VK_RWIN;

        /// <summary>Given modifier flags, return the representative VK for the target modifier.</summary>
        private static uint GetModifierFamilyVK(uint mods)
        {
            if ((mods & 0x0002) != 0) return VK_CONTROL;   // MOD_CONTROL
            if ((mods & 0x0001) != 0) return VK_MENU;      // MOD_ALT
            if ((mods & 0x0004) != 0) return VK_SHIFT;     // MOD_SHIFT
            if ((mods & 0x0008) != 0) return VK_LWIN;      // MOD_WIN
            return VK_CONTROL; // fallback
        }

        // ── Key parsing ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if parsed successfully.
        /// vk == 0 means modifier-only (e.g. "Ctrl", "Alt", "Shift").
        /// </summary>
        private static bool ParseKeyCombination(string combo, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;

            if (string.IsNullOrWhiteSpace(combo)) return false;

            foreach (var part in combo.Split('+'))
            {
                var t = part.Trim().ToUpperInvariant();
                switch (t)
                {
                    case "ALT":     modifiers |= 0x0001; break;
                    case "CTRL":
                    case "CONTROL": modifiers |= 0x0002; break;
                    case "SHIFT":   modifiers |= 0x0004; break;
                    case "WIN":
                    case "WINDOWS": modifiers |= 0x0008; break;
                    default:
                        if (t.Length == 1)
                            vk = (uint)t[0];
                        else if (t.StartsWith("F") && int.TryParse(t[1..], out int fn) && fn >= 1 && fn <= 12)
                            vk = (uint)(0x70 + fn - 1);
                        else
                            vk = t switch
                            {
                                "SPACE"  => 0x20,
                                "ENTER"  => 0x0D,
                                "ESC"    => 0x1B,
                                "ESCAPE" => 0x1B,
                                "TAB"    => 0x09,
                                _        => 0
                            };
                        break;
                }
            }

            // Valid if at least one modifier is set (vk can be 0 for modifier-only)
            return modifiers != 0 || vk != 0;
        }
    }
}
