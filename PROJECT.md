# Project: AltRunSharp

## Architecture
AltRunSharp 采用模块化设计，主要分为核心逻辑层与 UI 展现层。
- **AltRunSharp.Core (核心模块)**:
  - `Hotkey`: Win32 RegisterHotKey 与键盘钩子 WH_KEYBOARD_LL（检测 Ctrl 双击/三击），激活主窗口。
  - `Search`: 实现模糊搜索算法，匹配快捷命令和脚本。
  - `Config`: 负责 `data\config.json` 的读取、更新和序列化。
  - `Script`: 参数解析器（符合转义/引号/空格规则），JS/C# 脚本执行引擎（Process 启动，输出重定向）。
  - `Registry`: 系统提权逻辑，负责写入开机自启和 exe 右键菜单注册表。
- **AltRunSharp (WPF 界面)**:
  - `MainWindow`: 快速启动输入框（无边框，当前显示器居中弹出，失焦隐藏，按 Esc 隐藏）。
  - `Tray`: 托盘图标、右键菜单（显示/隐藏/配置/退出）。
  - `SettingsWindow`: 配置界面（包含快速启动、脚本执行、系统配置三个标签页，右侧显示详情）。
  - `OutputWindow`: 实时重定向脚本输出展示的精美控制台窗口。

## Milestones
| # | Name | Scope | Dependencies | Status | Conversation ID |
|---|------|-------|--------------|--------|-----------------|
| 0 | TestInfra | 设计并编写 E2E 测试框架，发布 TEST_READY.md | None | DONE | 21fcbb37-1095-47d0-8920-5696ea064e6c |
| 1 | HotkeyUI | 键盘钩子检测，托盘，鼠标屏幕居中弹出输入框 | M0 | IN_PROGRESS | 0eec6041-ef58-4d6d-926c-dbd1d772e0f0 |
| 2 | SearchPersist | 快速启动数据持久化，模糊搜索，列表上下键选中运行 | M1 | PLANNED | |
| 3 | ScriptEngine | 参数解析，Node.js/C# 执行，静默/显式输出重定向 | M2 | PLANNED | |
| 4 | RegistryUAC | 设置页面，开机自启，右键菜单，UAC 提权集成 | M3 | PLANNED | |
| 5 | E2EIntegration | 运行 Tiers 1-4 自动化测试并修复，Challenger 进行 Tier 5 变态测试与覆盖率审计 | M4 | PLANNED | |

## Interface Contracts
### `IHotkeyService`
- `void RegisterGlobalHotkey(string keyCombination, Action callback)`
- `void StartDoubleTripleCtrlHook(Action doubleClickCallback, Action tripleClickCallback)`
- `void StopHook()`

### `ISearchService`
- `List<SearchItem> Search(string query, List<SearchItem> source)`

### `IScriptExecutor`
- `Task<ExecutionResult> ExecuteAsync(ScriptCommand command, string rawArgs, Action<string> onOutputReceived)`
- `List<string> ParseArguments(string rawArgs)` (参数解析器接口)

### `IConfigService`
- `AppConfig LoadConfig()`
- `void SaveConfig(AppConfig config)`

### `IRegistryService`
- `bool SetAutoStart(bool enable)`
- `bool SetRightClickMenu(bool enable)`
- `bool IsRunningAsAdmin()`
- `void RestartAsAdmin(string args)`

## Code Layout
- `src/AltRunSharp.Core/` — 核心类库（Hotkey, Search, Config, Script, Registry）
- `src/AltRunSharp/` — WPF 主程序（UI 窗体，资源文件，Tray）
- `tests/AltRunSharp.Tests/` — 单元测试
- `tests/AltRunSharp.E2E/` — E2E 测试代码与测试运行器
- `data/config.json` — 默认配置文件
- `data/scripts/` — 用户脚本保存目录
- `data/logs/` — 静默脚本运行日志目录
