# SPEC v2.0.0 — URL 类型 / 开始菜单索引 / 拼音·英文首字母搜索 / 全屏抑制 / ComboBox 对比度

> 唯一对齐依据。所有车道先读本文件，再读各自车道内的文件。
> 项目：AltRunSharp（WPF，net10.0-windows，Nullable enable，ImplicitUsings enable）
> 配色基调：Catppuccin Mocha（详见 `App.xaml` 调色板）

## 文件车道归属表

| 车道 | 独占文件 |
| :--- | :--- |
| A（搜索域） | `LauncherViewModel.cs`、**新建** `StartMenuIndexService.cs`、**新建** `SearchMatcher.cs`、`AltRunSharp.csproj`（仅允许加 PackageReference） |
| B（URL+全屏+UI） | `AppConfig.cs`、`RunnerService.cs`、`SettingsWindow.xaml`、`SettingsWindow.xaml.cs`、`MainWindow.xaml.cs`、`App.xaml`、**新建** `FullscreenDetector.cs`（可选——也可内联进 MainWindow，由实现者决定） |

禁止越出车道修改其他文件。两个车道可完全并行。

## 跨车道冻结接口（双方对着实现，不互相等）

1. `AppConfig` 新增属性（**车道 B 落地**）：
   ```csharp
   /// <summary>When true, the launcher will not popup while a fullscreen app (game) is focused.</summary>
   public bool SuppressWhenFullscreen { get; set; } = false;
   ```
2. URL 判定约定（双方共用）：`Path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || Path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)`。
3. 若自己车道构建时遇到对方车道符号（如 `SuppressWhenFullscreen`）暂未落地导致的编译错误，属预期瞬态；在报告中注明即可，最终由协调者做集成构建收口。**严禁**为此去改对方车道的文件。

---

## 任务 A：开始菜单索引 + 拼音/英文首字母搜索（车道 A）

### A.1 新建 `StartMenuIndexService.cs`

职责：索引开始菜单快捷方式（.lnk），并用 FileSystemWatcher 自动刷新。

- 索引根目录（两个，都存在才加）：
  - `Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)`（= `%APPDATA%\Microsoft\Windows\Start Menu`，用户要求的路径）
  - `Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)`（= `C:\ProgramData\Microsoft\Windows\Start Menu`，大多数快捷方式在此）
- 递归（含子目录，重点关注 `Programs` 下）收集全部 `*.lnk`。
- 每个条目生成一个 `LaunchItem`：`Name = 文件名去扩展名`，`Path = .lnk 完整路径`，`Description = "开始菜单"`。
- 按 Name 去重（忽略大小写，用户目录优先）。
- 每个根目录一个 `FileSystemWatcher`：`IncludeSubdirectories = true`，`Filter = "*.lnk"`，订阅 Created/Deleted/Renamed/Changed；事件去抖（约 500ms，`System.Threading.Timer` 即可）后后台重建索引并触发 `event Action? IndexChanged`。
- 首次索引在后台线程（`Task.Run`）构建，构建完成后触发 `IndexChanged`。所有 IO 都要 try/catch 兜底（权限/目录不存在不得崩溃）。
- `IDisposable`：Dispose 停掉 watcher 和 timer。

### A.2 新建 `SearchMatcher.cs`

静态匹配工具，namespace `AltRunSharp`：

```csharp
public static class SearchMatcher
{
    /// <summary>拼音首字母：把文本中每个汉字转为拼音首字母（小写），非汉字字母/数字原样保留并小写，其他字符去掉。如 "设置" → "sz"，"QQ音乐" → "qqyy"。</summary>
    public static string GetPinyinInitials(string text);

    /// <summary>英文首字母：按空格分词，取每个词首字符（无视大小写，返回小写拼接）。如 "Visual Studio Code" → "vsc"。</summary>
    public static string GetEnglishInitials(string text);

    /// <summary>首字母匹配：query 归一化（去空格、小写）后，若是 candidate 的拼音首字母串或英文首字母串的子串（Contains）即命中。query 为空或含中文时直接返回 false（中文走原有子串匹配）。</summary>
    public static bool MatchesInitials(string candidate, string query);
}
```

### A.3 `AltRunSharp.csproj`：加拼音包

- 首选 `TinyPinyin.Net` v1.0.2（netstandard2.0，兼容 net10；API 形如 `TinyPinyin.PinyinHelper.GetPinyinInitials`）。
- **实现者必须先 `dotnet restore` 后确认真实 API**（命名空间/方法名/返回大小写/非汉字处理方式），据实封装到 `SearchMatcher.GetPinyinInitials`；若该包不可用或 API 不符，改用 `hyjiacan.pinyin4net` v4.1.1（netstandard2.0）。
- csproj 只允许新增 `<PackageReference>`，其他内容一字不动。

### A.4 `LauncherViewModel.cs` 改造

1. **持有开始菜单索引**：`LauncherViewModel` 构造时创建 `StartMenuIndexService` 并订阅 `IndexChanged`；内部用不可变快照（`volatile` 引用或 lock）保存最新 `LaunchItem[]`。
2. `Search(string query)` 改造：
   - 结果集 = 现有 config 启动项 + 脚本 + 内置命令 + **开始菜单条目**（`SearchResult { Kind = "launch", LaunchItem = item }`，与 config 启动项走同一执行路径，无需 MainWindow 改动）。
   - 匹配条件扩展：对每个候选，除现有子串匹配外，**Name 与 Description 额外做首字母匹配**——`SearchMatcher.MatchesInitials(name, query) || SearchMatcher.MatchesInitials(description, query)`；别名（Aliases）保持现有子串匹配，可顺带加首字母匹配（推荐，非强制）。
   - 现有行为全部保留：空 query 返回空、`/` 开头走内置命令与脚本逻辑不变。
3. `SearchResult.KindLabel`：launch 项中 `LaunchItem.Path` 为 URL（见冻结接口 2）时显示 `"网址"`；开始菜单条目仍显示 `"程序"`。
4. `UpdateConfig` 语义不变。

### A.5 验收

- 搜索 `qq` 能命中名称/描述含「QQ」拼音首字母或子串的条目；搜索 `sz` 命中描述含「设置」的条目；搜索 `vsc` 命中 "Visual Studio Code"。
- 搜索开始菜单里真实存在的程序名（如 `edge`、`微信` 首字母 `wx`）能出现对应 .lnk 条目（描述「开始菜单」），回车用默认方式打开。
- 在 `%APPDATA%\Microsoft\Windows\Start Menu\Programs` 新建/删除一个 .lnk，约 1 秒内索引自动更新（无需重启应用）。
- 普通子串搜索行为与 v1.3.0 完全一致（回归）。

---

## 任务 B：URL 类型 + 全屏抑制 + ComboBox 对比度（车道 B）

### B.1 URL 类型启动项

1. `RunnerService.RunLaunchItem`：开头检测 URL（冻结接口 2），命中则：
   ```csharp
   try { Process.Start(new ProcessStartInfo { FileName = item.Path, UseShellExecute = true }); }
   catch (Exception ex) { /* 沿用现有 MessageBox 错误提示风格 */ }
   return;
   ```
   跳过 workDir 解析（URL 没有目录）与 Args 拼接。
2. `SettingsWindow.xaml` 快速启动页：
   - 「程序路径」标签改为「程序路径 / 网址」；`LaunchPathBox` 加 ToolTip：`支持 http:// 或 https:// 网址，将使用默认浏览器打开`。
   - 「启动参数（可选）」标签下加一行小字提示（Foreground #6C7086, FontSize 11）：`网址类型条目忽略启动参数`。
3. `SettingsWindow.xaml.cs` `LaunchSave_Click`：路径为 URL 且名称为空或「新程序」时，自动用 URL 的 Host 作为名称（可选增强，做了更好）。

### B.2 全屏抑制设置（游戏模式）

1. `AppConfig.cs`：按冻结接口 1 新增 `SuppressWhenFullscreen`。
2. `MainWindow.xaml.cs`：
   - 新增 P/Invoke：`GetWindowRect`（RECT 复用现有结构）、`MonitorFromWindow(IntPtr, uint)`。
   - 新增辅助方法（可内联或放新建的 `FullscreenDetector.cs`）：
     ```csharp
     /// <summary>前台窗口是否铺满其所在显示器（无边框与独占全屏都满足）。排除本进程窗口与桌面/任务栏（Progman/WorkerW/Shell_TrayWnd/Shell_SecondaryTrayWnd）。</summary>
     private bool IsForegroundFullscreen();
     ```
     判定：`GetForegroundWindow` → 排除本进程（`GetWindowThreadProcessId` 对比）与上述 shell 类名 → `GetWindowRect` 与 `MonitorFromWindow(hwnd, 2)` 的 `rcMonitor` 比较，四边容差 2px 内铺满即视为全屏。
   - `ShowLauncher()` 最前面加门控：
     ```csharp
     if (this.Visibility != Visibility.Visible &&
         _config.SuppressWhenFullscreen &&
         IsForegroundFullscreen())
         return;
     ```
     语义：仅在「窗口当前隐藏」时拦截弹出；已显示时不拦截（保证能正常隐藏/聚焦，不会卡死状态）。`ToggleWindow` 不用改（显示路径自然被 ShowLauncher 拦住，隐藏路径不受影响）。托盘菜单「显示」与热键都走 ShowLauncher，统一生效。
3. `SettingsWindow.xaml` 配置页：在「系统集成」区的右键菜单 Grid 之后、软件更新分隔线之前，新增一个与「开机自动启动」相同结构的 Grid：
   - 标题：`全屏应用时不弹出（游戏模式）`
   - 副标题：`检测到前台应用全屏（无边框或独占全屏，如游戏）时，快捷键与托盘点击不再弹出启动器`
   - `ToggleButton x:Name="FullscreenToggle" Style="{StaticResource ToggleSwitchStyle}" Checked="FullscreenToggle_Checked" Unchecked="FullscreenToggle_Unchecked"`
4. `SettingsWindow.xaml.cs`：
   - `LoadConfigPage()` 中（`_suppressFieldEvents = true` 区间内）加 `FullscreenToggle.IsChecked = _config.SuppressWhenFullscreen;`
   - 两个 handler：置 `_config.SuppressWhenFullscreen = true/false` + `SaveConfig()`，注意 `_suppressFieldEvents` 早退（仿照 StartupToggle）。

### B.3 ComboBox 下拉项 hover/选中对比度修复（`App.xaml`）

现状：`FieldComboStyle` 内 ComboBoxItem 模板触发器为 hover 背景 `#45475A`（文字 `#CDD6F4`）、选中背景 `#3D3556` + 文字 `#CBA6F7`（紫底紫字， hue 相近，实测难以辨认）。

改为（只动 `ItemContainerStyle` 里的两个 Trigger）：
- `IsSelected`（放前面）：Background `#CBA6F7`、Foreground `#1E1E2E`（与主按钮同款高对比）。
- `IsHighlighted`（放后面，hover 优先于选中态显示）：Background `#585B70`、Foreground `#FFFFFF`。

### B.4 验收

- 快速启动项路径填 `https://github.com` 保存后，从启动器搜索并回车 → 默认浏览器打开该网址。
- 配置页出现「全屏应用时不弹出」开关；开启后，前台跑一个最大化铺满屏幕的窗口（如浏览器 F11 全屏）时按热键不弹出；关闭开关后恢复弹出；设置重启应用后保持。
- 设置页任意下拉框：hover 项白字亮灰底、选中项深字紫底，清晰可辨。

---

## 集成验收（协调者执行）

1. `dotnet build AltRunSharp.csproj` 0 错误 0 警告。
2. grep 核对本 SPEC 每个验收点对应代码真实存在。
3. 产物级抽查：csproj 包引用与版本、RunLauncher URL 分支、ShowLauncher 门控、App.xaml 触发器顺序、索引 watcher 参数。
4. 版本升级 v2.0.0（另发车道路）。分批提交：索引 / 首字母搜索 / URL / 游戏模式 / 设置UI+ComboBox / docs / bump，最后 tag `v2.0.0` 推送。
