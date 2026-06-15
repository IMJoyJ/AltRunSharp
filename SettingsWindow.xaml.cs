using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AltRunSharp
{
    public partial class SettingsWindow : Window
    {
        private readonly IConfigService _configService;
        private AppConfig _config;
        private readonly ServiceManager _serviceManager;
        private readonly ScheduleManager _scheduleManager;

        private LaunchItem? _editingLaunch;
        private ScriptItem? _editingScript;
        private ScheduledTask? _editingSchedule;
        private bool _suppressFieldEvents = false;
        private bool _recordingHotkey = false;
        private string _currentHotkeyMods = string.Empty;

        public event Action? ConfigSaved;

        public SettingsWindow(IConfigService configService, AppConfig config,
            ServiceManager serviceManager, ScheduleManager scheduleManager)
        {
            _configService = configService;
            _config = config;
            _serviceManager = serviceManager;
            _scheduleManager = scheduleManager;

            InitializeComponent();
            Loaded += SettingsWindow_Loaded;

            _serviceManager.ServicesChanged += RefreshServicePage;
            _scheduleManager.StateChanged   += RefreshScheduleStatus;

            ShowPage("launch");
            SetNavActive(NavLaunchBtn);
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshLaunchList();
            RefreshScriptList();
            LoadConfigPage();
        }

        protected override void OnClosed(EventArgs e)
        {
            _serviceManager.ServicesChanged -= RefreshServicePage;
            _scheduleManager.StateChanged   -= RefreshScheduleStatus;
            base.OnClosed(e);
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string tag = (string)btn.Tag;
            ShowPage(tag);
            SetNavActive(btn);
            if (tag == "service")  RefreshServicePage();
            if (tag == "schedule") RefreshScheduleStatus();
        }

        private void ShowPage(string page)
        {
            LaunchPage.Visibility   = page == "launch"   ? Visibility.Visible : Visibility.Collapsed;
            ScriptPage.Visibility   = page == "script"   ? Visibility.Visible : Visibility.Collapsed;
            ServicePage.Visibility  = page == "service"  ? Visibility.Visible : Visibility.Collapsed;
            SchedulePage.Visibility = page == "schedule" ? Visibility.Visible : Visibility.Collapsed;
            ConfigPage.Visibility   = page == "config"   ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetNavActive(Button active)
        {
            foreach (Button btn in new[] { NavLaunchBtn, NavScriptBtn, NavServiceBtn, NavScheduleBtn, NavConfigBtn })
            {
                btn.Background = btn == active
                    ? new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44))
                    : Brushes.Transparent;
                btn.Foreground = btn == active
                    ? new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7))
                    : new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
            }
        }

        private void HideToTray_Click(object sender, RoutedEventArgs e) => Hide();

        // ── Launch page ───────────────────────────────────────────────────────

        private void RefreshLaunchList()
        {
            LaunchList.ItemsSource = null;
            LaunchList.ItemsSource = _config.LaunchItems;
        }

        private void LaunchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _editingLaunch = LaunchList.SelectedItem as LaunchItem;
            LoadLaunchToForm(_editingLaunch);
            LaunchEditPanel.IsEnabled = _editingLaunch != null;
        }

        private void LoadLaunchToForm(LaunchItem? item)
        {
            _suppressFieldEvents = true;
            LaunchNameBox.Text = item?.Name ?? string.Empty;
            LaunchDescBox.Text = item?.Description ?? string.Empty;
            LaunchPathBox.Text = item?.Path ?? string.Empty;
            LaunchArgsBox.Text = item?.Args ?? string.Empty;
            LaunchAliasBox.Text = item != null
                ? string.Join(Environment.NewLine, item.Aliases)
                : string.Empty;
            _suppressFieldEvents = false;
        }

        private void LaunchField_Changed(object sender, TextChangedEventArgs e) { }

        private void LaunchAdd_Click(object sender, RoutedEventArgs e)
        {
            var item = new LaunchItem { Name = "新程序" };
            _config.LaunchItems.Add(item);
            RefreshLaunchList();
            LaunchList.SelectedItem = item;
        }

        private void LaunchDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_editingLaunch == null) return;
            _config.LaunchItems.Remove(_editingLaunch);
            _editingLaunch = null;
            RefreshLaunchList();
            LoadLaunchToForm(null);
            LaunchEditPanel.IsEnabled = false;
            SaveConfig();
        }

        private void LaunchBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择程序"
            };
            if (dlg.ShowDialog() == true)
            {
                LaunchPathBox.Text = dlg.FileName;
                if (string.IsNullOrWhiteSpace(LaunchNameBox.Text) || LaunchNameBox.Text == "新程序")
                    LaunchNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        private void LaunchSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editingLaunch == null) return;
            _editingLaunch.Name = LaunchNameBox.Text.Trim();
            _editingLaunch.Description = LaunchDescBox.Text.Trim();
            _editingLaunch.Path = LaunchPathBox.Text.Trim();
            _editingLaunch.Args = LaunchArgsBox.Text.Trim();
            _editingLaunch.Aliases = ParseLines(LaunchAliasBox.Text);
            RefreshLaunchList();
            SaveConfig();
        }

        // ── Script page ───────────────────────────────────────────────────────

        private void RefreshScriptList()
        {
            ScriptList.ItemsSource = null;
            ScriptList.ItemsSource = _config.ScriptItems;
        }

        private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _editingScript = ScriptList.SelectedItem as ScriptItem;
            LoadScriptToForm(_editingScript);
            ScriptEditPanel.IsEnabled = _editingScript != null;
        }

        private void LoadScriptToForm(ScriptItem? item)
        {
            _suppressFieldEvents = true;

            ScriptNameBox.Text = item?.Name ?? string.Empty;
            ScriptDescBox.Text = item?.Description ?? string.Empty;

            // ScriptType (js=0, cs=1, workflow=2)
            ScriptTypeBox.SelectedIndex = (item?.ScriptType ?? "js").ToLowerInvariant() switch
            {
                "cs"       => 1,
                "workflow" => 2,
                _          => 0
            };

            // LaunchMode (once=0, service=1)
            LaunchModeBox.SelectedIndex = (item?.LaunchMode ?? "once") == "service" ? 1 : 0;

            // Silent (explicit=0, silent=1)
            ScriptSilentBox.SelectedIndex = item?.Silent == true ? 1 : 0;

            // Boot start
            ServiceBootBox.IsChecked = item?.BootStart ?? false;

            // Script content (js/cs only)
            if (item != null && item.ScriptType != "workflow" && !string.IsNullOrEmpty(item.ScriptFileName))
            {
                string path = Path.Combine(GetScriptsDir(), item.ScriptFileName);
                ScriptContentBox.Text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            else
            {
                ScriptContentBox.Text = string.Empty;
            }

            // Workflow steps
            RefreshWorkflowStepList(item);
            RefreshWorkflowAddCombo();

            // Aliases
            ScriptAliasBox.Text = item != null
                ? string.Join(Environment.NewLine, item.Aliases)
                : string.Empty;

            _suppressFieldEvents = false;

            UpdateScriptSectionVisibility();
        }

        private void UpdateScriptSectionVisibility()
        {
            string scriptType = (ScriptTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "js";
            string launchMode = (LaunchModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "once";
            bool isWorkflow = scriptType == "workflow";
            bool isService  = !isWorkflow && launchMode == "service";
            bool isOnce     = !isWorkflow && !isService;

            LaunchModeSection.Visibility  = isWorkflow ? Visibility.Collapsed : Visibility.Visible;
            OnceModeSection.Visibility    = isOnce     ? Visibility.Visible   : Visibility.Collapsed;
            ServiceSection.Visibility     = isService  ? Visibility.Visible   : Visibility.Collapsed;
            ScriptCodeSection.Visibility  = isWorkflow ? Visibility.Collapsed : Visibility.Visible;
            WorkflowSection.Visibility    = isWorkflow ? Visibility.Visible   : Visibility.Collapsed;

            // Boot start warning
            if (isService && (ServiceBootBox.IsChecked == true) && !_config.StartupEnabled)
                ServiceBootWarning.Visibility = Visibility.Visible;
            else
                ServiceBootWarning.Visibility = Visibility.Collapsed;
        }

        private void ScriptField_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            UpdateScriptSectionVisibility();
            // If switching type to workflow, repopulate combo
            if (sender == ScriptTypeBox)
                RefreshWorkflowAddCombo();
        }

        private void ServiceBoot_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            UpdateScriptSectionVisibility();
        }

        private void ScriptAdd_Click(object sender, RoutedEventArgs e)
        {
            var item = new ScriptItem { Name = "new_script", ScriptType = "js" };
            _config.ScriptItems.Add(item);
            RefreshScriptList();
            ScriptList.SelectedItem = item;
        }

        private void ScriptDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_editingScript == null) return;
            if (!string.IsNullOrEmpty(_editingScript.ScriptFileName))
            {
                string path = Path.Combine(GetScriptsDir(), _editingScript.ScriptFileName);
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            _config.ScriptItems.Remove(_editingScript);
            _editingScript = null;
            RefreshScriptList();
            LoadScriptToForm(null);
            ScriptEditPanel.IsEnabled = false;
            SaveConfig();
        }

        private void ScriptSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editingScript == null) return;

            string name = ScriptNameBox.Text.Trim().Replace(" ", "_");
            if (string.IsNullOrEmpty(name)) { MessageBox.Show("命令名称不能为空", "AltRunSharp"); return; }

            string scriptType = (ScriptTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "js";
            string launchMode = (LaunchModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "once";
            bool silent = launchMode == "service" ||
                          ((ScriptSilentBox.SelectedItem as ComboBoxItem)?.Tag as string == "true");
            bool bootStart = ServiceBootBox.IsChecked == true;

            _editingScript.Name = name;
            _editingScript.Description = ScriptDescBox.Text.Trim();
            _editingScript.ScriptType = scriptType;
            _editingScript.LaunchMode = scriptType == "workflow" ? "once" : launchMode;
            _editingScript.Silent = silent;
            _editingScript.BootStart = bootStart;
            _editingScript.Aliases = ParseLines(ScriptAliasBox.Text);

            if (scriptType == "workflow")
            {
                // WorkflowSteps already updated live via Add/Remove buttons
                // Clear script file fields
                _editingScript.ScriptFileName = string.Empty;
            }
            else
            {
                // Determine file name
                string ext = scriptType == "cs" ? ".cs" : ".js";
                string fileName = name + ext;

                // Delete old file if renamed/type changed
                if (!string.IsNullOrEmpty(_editingScript.ScriptFileName) &&
                    _editingScript.ScriptFileName != fileName)
                {
                    string oldPath = Path.Combine(GetScriptsDir(), _editingScript.ScriptFileName);
                    try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
                }

                _editingScript.ScriptFileName = fileName;

                // Save script content
                string scriptsDir = GetScriptsDir();
                Directory.CreateDirectory(scriptsDir);
                File.WriteAllText(Path.Combine(scriptsDir, fileName), ScriptContentBox.Text);
            }

            RefreshScriptList();
            SaveConfig();
            MessageBox.Show("已保存。", "AltRunSharp", MessageBoxButton.OK, MessageBoxImage.None);
        }

        // ── Workflow step management ──────────────────────────────────────────

        private void RefreshWorkflowStepList(ScriptItem? item)
        {
            if (item == null || item.ScriptType != "workflow")
            {
                WorkflowStepList.ItemsSource = null;
                return;
            }

            // Validate references: auto-remove dead entries
            var valid = item.WorkflowSteps
                .Where(n => _config.ScriptItems.Any(s =>
                    s.Name == n && s.ScriptType != "workflow"))
                .ToList();

            if (valid.Count != item.WorkflowSteps.Count)
                item.WorkflowSteps = valid;

            WorkflowStepList.ItemsSource = null;
            WorkflowStepList.ItemsSource = item.WorkflowSteps;
        }

        private void RefreshWorkflowAddCombo()
        {
            // Populate with js/cs scripts only (no nested workflows)
            var available = _config.ScriptItems
                .Where(s => s.ScriptType != "workflow")
                .Select(s => s.Name)
                .ToList();

            WorkflowAddCombo.ItemsSource = available;
            if (available.Count > 0) WorkflowAddCombo.SelectedIndex = 0;
        }

        private void WorkflowAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_editingScript == null || _editingScript.ScriptType != "workflow") return;
            string? selected = WorkflowAddCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            _editingScript.WorkflowSteps.Add(selected);
            RefreshWorkflowStepList(_editingScript);
        }

        private void WorkflowRemove_Click(object sender, RoutedEventArgs e)
        {
            if (_editingScript == null || _editingScript.ScriptType != "workflow") return;
            string? selected = WorkflowStepList.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            _editingScript.WorkflowSteps.Remove(selected);
            RefreshWorkflowStepList(_editingScript);
        }

        // ── Service control ───────────────────────────────────────────────────

        private void StartService_Click(object sender, RoutedEventArgs e)
        {
            if (_editingScript == null) return;

            // Save first to ensure latest config is used
            ScriptSave_Click(sender, e);

            string extraArgs = ServiceArgsBox.Text.Trim();
            _serviceManager.StartService(_editingScript, extraArgs);

            // Switch to service management page to show the running instance
            ShowPage("service");
            SetNavActive(NavServiceBtn);
            RefreshServicePage();
        }

        // ── Service page ──────────────────────────────────────────────────────

        private void RefreshServicePage()
        {
            var services = _serviceManager.GetServices();
            ServiceListPanel.ItemsSource = services;
            NoServicesText.Visibility = services.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StopServiceInstance_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string instanceId)
                _serviceManager.StopService(instanceId);
        }

        private void StopAllServices_Click(object sender, RoutedEventArgs e)
        {
            _serviceManager.StopAllServices();
        }

        // ── Config page ───────────────────────────────────────────────────────

        private void LoadConfigPage()
        {
            _suppressFieldEvents = true;
            ClickModeBox.SelectedIndex = (_config.ClickMode ?? "single") switch
            {
                "double" => 1,
                "triple" => 2,
                _        => 0
            };
            HotkeyRecordBox.Text = _config.Hotkey ?? "Alt+R";
            StartupToggle.IsChecked = AdminHelper.IsStartupEnabled();
            ContextMenuToggle.IsChecked = AdminHelper.IsContextMenuEnabled();
            _suppressFieldEvents = false;
        }

        private void ConfigField_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            var tag = (ClickModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "single";
            _config.ClickMode = tag;
            SaveConfig();
        }

        // ── Hotkey recording ──────────────────────────────────────────────────

        private void HotkeyRecordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _recordingHotkey = true;
            HotkeyRecordBox.Text = "按下目标按键...";
            HotkeyRecordBox.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
        }

        private void HotkeyRecordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _recordingHotkey = false;
            _currentHotkeyMods = string.Empty;
            if (HotkeyRecordBox.Text == "按下目标按键...")
            {
                HotkeyRecordBox.Text = _config.Hotkey ?? "Alt+R";
                HotkeyRecordBox.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
            }
        }

        private void HotkeyRecordBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_recordingHotkey) return;
            e.Handled = true;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = new List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods.Add("Alt");
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods.Add("Ctrl");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods.Add("Shift");
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods.Add("Win");

            bool isModifierOnly =
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin;

            if (isModifierOnly)
            {
                HotkeyRecordBox.Text = string.Join("+", mods) + "+...";
                return;
            }

            bool isFunctionKey = key >= Key.F1 && key <= Key.F12;
            if (mods.Count == 0 && !isFunctionKey)
            {
                HotkeyRecordBox.Text = "需要至少一个修饰键（Alt/Ctrl/Shift）";
                return;
            }

            string keyStr = GetKeyString(key);
            if (string.IsNullOrEmpty(keyStr)) return;

            string hotkey = mods.Count > 0 ? string.Join("+", mods) + "+" + keyStr : keyStr;
            HotkeyRecordBox.Text = hotkey;
            HotkeyRecordBox.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
            _config.Hotkey = hotkey;
            _recordingHotkey = false;
            ClickModeBox.Focus();
            SaveConfig();
        }

        private void HotkeyRecordBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (!_recordingHotkey) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool isModifierOnly =
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt  || key == Key.RightAlt  ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin;
            if (!isModifierOnly) return;
            if (!HotkeyRecordBox.Text.EndsWith("+...")) return;

            string modName = key switch
            {
                Key.LeftCtrl  or Key.RightCtrl  => "Ctrl",
                Key.LeftAlt   or Key.RightAlt   => "Alt",
                Key.LeftShift or Key.RightShift => "Shift",
                Key.LWin      or Key.RWin       => "Win",
                _ => string.Empty
            };
            if (string.IsNullOrEmpty(modName)) return;

            HotkeyRecordBox.Text = modName;
            HotkeyRecordBox.Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
            _config.Hotkey = modName;
            _recordingHotkey = false;
            ClickModeBox.Focus();
            SaveConfig();
        }

        private static string GetKeyString(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return key.ToString();
            if (key >= Key.D0 && key <= Key.D9) return key.ToString().TrimStart('D');
            if (key >= Key.F1 && key <= Key.F12) return key.ToString();
            return key switch
            {
                Key.Space           => "Space",
                Key.Enter           => "Enter",
                Key.Escape          => "Escape",
                Key.Tab             => "Tab",
                Key.Back            => "Back",
                Key.Delete          => "Delete",
                Key.Insert          => "Insert",
                Key.Home            => "Home",
                Key.End             => "End",
                Key.PageUp          => "PageUp",
                Key.PageDown        => "PageDown",
                Key.OemSemicolon    => ";",
                Key.OemComma        => ",",
                Key.OemPeriod       => ".",
                Key.OemQuestion     => "/",
                Key.OemTilde        => "`",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe         => "\\",
                Key.OemQuotes       => "'",
                Key.OemMinus        => "-",
                Key.OemPlus         => "=",
                _ => string.Empty
            };
        }

        // ── Startup / Context menu toggles ────────────────────────────────────

        private void StartupToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            AdminHelper.EnableStartup();
            _config.StartupEnabled = true;
            SaveConfig();
        }

        private void StartupToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            AdminHelper.DisableStartup();
            _config.StartupEnabled = false;
            SaveConfig();
        }

        private void ContextMenuToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            if (AdminHelper.IsAdmin())
            {
                AdminHelper.EnableContextMenu();
                _config.ContextMenuEnabled = true;
                SaveConfig();
            }
            else
            {
                bool launched = AdminHelper.RelaunchAsAdmin("--reg-context-menu");
                if (!launched)
                {
                    _suppressFieldEvents = true;
                    ContextMenuToggle.IsChecked = false;
                    _suppressFieldEvents = false;
                }
            }
        }

        private void ContextMenuToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            if (AdminHelper.IsAdmin())
            {
                AdminHelper.DisableContextMenu();
                _config.ContextMenuEnabled = false;
                SaveConfig();
            }
            else
            {
                bool launched = AdminHelper.RelaunchAsAdmin("--unreg-context-menu");
                if (!launched)
                {
                    _suppressFieldEvents = true;
                    ContextMenuToggle.IsChecked = true;
                    _suppressFieldEvents = false;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetScriptsDir()
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "scripts");

        private static List<string> ParseLines(string text)
            => (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .ToList();

        private void SaveConfig()
        {
            _configService.SaveConfig(_config);
            ConfigSaved?.Invoke();
        }
        // ── Schedule page ─────────────────────────────────────────────────────

        private void RefreshScheduleList()
        {
            ScheduleList.ItemsSource = null;
            ScheduleList.ItemsSource = _config.ScheduledTasks;
        }

        private void RefreshScheduleStatus()
        {
            if (_editingSchedule == null) return;
            var states = _scheduleManager.GetTaskStates();
            var (_, state) = states.FirstOrDefault(x => x.Task.Name == _editingSchedule.Name);
            if (state != null)
                SchStatusText.Text = state.NextFireText(_editingSchedule);
        }

        private void ScheduleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _editingSchedule = ScheduleList.SelectedItem as ScheduledTask;
            LoadScheduleToForm(_editingSchedule);
            ScheduleEditPanel.IsEnabled = _editingSchedule != null;
        }

        private void LoadScheduleToForm(ScheduledTask? task)
        {
            _suppressFieldEvents = true;

            SchNameBox.Text = task?.Name ?? string.Empty;
            SchDescBox.Text = task?.Description ?? string.Empty;

            // Workflow combo
            var workflows = _config.ScriptItems.Where(s => s.ScriptType == "workflow").Select(s => s.Name).ToList();
            SchWorkflowCombo.ItemsSource = workflows;
            SchWorkflowCombo.SelectedItem = task?.WorkflowName;

            // Args script combo (add empty option at top)
            var scripts = new List<string> { "(无)" };
            scripts.AddRange(_config.ScriptItems
                .Where(s => s.ScriptType != "workflow")
                .Select(s => s.Name));
            SchArgsScriptCombo.ItemsSource = scripts;
            SchArgsScriptCombo.SelectedItem = string.IsNullOrEmpty(task?.ArgsScriptName)
                ? "(无)" : task.ArgsScriptName;

            // Trigger type
            SchTriggerTypeCombo.SelectedIndex = (task?.TriggerType ?? "interval") == "daily" ? 1 : 0;
            UpdateSchTriggerSectionVisibility();

            // Interval
            int secs = task?.IntervalSeconds ?? 3600;
            if (secs % 3600 == 0) { SchIntervalValueBox.Text = (secs / 3600).ToString(); SchIntervalUnitCombo.SelectedIndex = 0; }
            else if (secs % 60 == 0) { SchIntervalValueBox.Text = (secs / 60).ToString(); SchIntervalUnitCombo.SelectedIndex = 1; }
            else { SchIntervalValueBox.Text = secs.ToString(); SchIntervalUnitCombo.SelectedIndex = 2; }

            // Daily times
            var times = task?.DailyTimes ?? new List<string>();
            SchDailyTimesList.ItemsSource = null;
            SchDailyTimesList.ItemsSource = new List<string>(times);

            // Boot start
            SchBootBox.IsChecked = task?.BootStart ?? false;
            SchBootWarning.Visibility = (task?.BootStart == true && !_config.StartupEnabled)
                ? Visibility.Visible : Visibility.Collapsed;

            // Conflict
            SchConflictCombo.SelectedIndex = (task?.ConflictResolution ?? "skip") switch
            {
                "kill"     => 1,
                "parallel" => 2,
                _          => 0
            };

            SchEnabledBox.IsChecked = task?.Enabled ?? true;
            SchStatusText.Text = string.Empty;

            _suppressFieldEvents = false;
        }

        private void UpdateSchTriggerSectionVisibility()
        {
            string tag = (SchTriggerTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "interval";
            SchIntervalSection.Visibility = tag == "interval" ? Visibility.Visible : Visibility.Collapsed;
            SchDailySection.Visibility    = tag == "daily"    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SchTriggerType_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            UpdateSchTriggerSectionVisibility();
        }

        private void SchBoot_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            SchBootWarning.Visibility = (SchBootBox.IsChecked == true && !_config.StartupEnabled)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ScheduleAdd_Click(object sender, RoutedEventArgs e)
        {
            var task = new ScheduledTask { Name = "新计划任务" };
            _config.ScheduledTasks.Add(task);
            RefreshScheduleList();
            ScheduleList.SelectedItem = task;
        }

        private void ScheduleDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_editingSchedule == null) return;
            _scheduleManager.StopTask(_editingSchedule.Name);
            _config.ScheduledTasks.Remove(_editingSchedule);
            _editingSchedule = null;
            RefreshScheduleList();
            LoadScheduleToForm(null);
            ScheduleEditPanel.IsEnabled = false;
            SaveConfig();
        }

        private void SchAddTime_Click(object sender, RoutedEventArgs e)
        {
            string time = SchNewTimeBox.Text.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(time, @"^\d{2}:\d{2}$"))
            {
                MessageBox.Show("格式应为 HH:mm，例如 09:30", "AltRunSharp");
                return;
            }
            if (_editingSchedule != null && !_editingSchedule.DailyTimes.Contains(time))
            {
                _editingSchedule.DailyTimes.Add(time);
                _editingSchedule.DailyTimes.Sort();
                SchDailyTimesList.ItemsSource = null;
                SchDailyTimesList.ItemsSource = new List<string>(_editingSchedule.DailyTimes);
            }
            SchNewTimeBox.Text = string.Empty;
        }

        private void SchRemoveTime_Click(object sender, RoutedEventArgs e)
        {
            if (_editingSchedule == null) return;
            string? sel = SchDailyTimesList.SelectedItem as string;
            if (sel == null) return;
            _editingSchedule.DailyTimes.Remove(sel);
            SchDailyTimesList.ItemsSource = null;
            SchDailyTimesList.ItemsSource = new List<string>(_editingSchedule.DailyTimes);
        }

        private void ScheduleSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editingSchedule == null) return;
            string name = SchNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { MessageBox.Show("任务名称不能为空", "AltRunSharp"); return; }

            string triggerType = (SchTriggerTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "interval";

            int intervalSecs = 3600;
            if (triggerType == "interval")
            {
                if (!int.TryParse(SchIntervalValueBox.Text.Trim(), out int val) || val <= 0)
                { MessageBox.Show("间隔时间必须为正整数", "AltRunSharp"); return; }
                string unit = (SchIntervalUnitCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "h";
                intervalSecs = unit == "h" ? val * 3600 : unit == "m" ? val * 60 : val;
            }

            string argsScript = (SchArgsScriptCombo.SelectedItem as string) ?? string.Empty;
            if (argsScript == "(无)") argsScript = string.Empty;

            _editingSchedule.Name = name;
            _editingSchedule.Description = SchDescBox.Text.Trim();
            _editingSchedule.WorkflowName = (SchWorkflowCombo.SelectedItem as string) ?? string.Empty;
            _editingSchedule.ArgsScriptName = argsScript;
            _editingSchedule.TriggerType = triggerType;
            _editingSchedule.IntervalSeconds = intervalSecs;
            _editingSchedule.BootStart = SchBootBox.IsChecked == true;
            _editingSchedule.ConflictResolution = (SchConflictCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "skip";
            _editingSchedule.Enabled = SchEnabledBox.IsChecked == true;

            _scheduleManager.UpdateConfig(_config);
            RefreshScheduleList();
            SaveConfig();
            MessageBox.Show("已保存。", "AltRunSharp", MessageBoxButton.OK, MessageBoxImage.None);
        }

        private void ScheduleFireNow_Click(object sender, RoutedEventArgs e)
        {
            if (_editingSchedule == null) return;
            ScheduleSave_Click(sender, e);
            _scheduleManager.FireNow(_editingSchedule.Name);
        }

        private void ScheduleStop_Click(object sender, RoutedEventArgs e)
        {
            if (_editingSchedule == null) return;
            _scheduleManager.StopTask(_editingSchedule.Name);
        }
    }
}
