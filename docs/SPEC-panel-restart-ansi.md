# SPEC — /panel 内置命令 + 服务重启按钮 + ANSI 输出支持

> 唯一对齐依据。所有车道先读本文件，再读各自车道内的文件。
> 项目：AltRunSharp（WPF，net10.0-windows，Nullable enable，ImplicitUsings enable）
> 配色基调：Catppuccin Mocha（正文 #CDD6F4、红 #F38BA8、绿 #A6E3A1、黄 #F9E2AF、蓝 #89B4FA、紫 #CBA6F7、青 #94E2D5、底 #1E1E2E）

## 文件车道归属表

| 车道 | 独占文件 |
| :--- | :--- |
| A（/panel） | `LauncherViewModel.cs`、`MainWindow.xaml.cs` |
| B（重启按钮） | `ServiceManager.cs`、`SettingsWindow.xaml`、`SettingsWindow.xaml.cs` |
| C（ANSI） | `OutputWindow.xaml.cs`、**新建** `AnsiParser.cs` |

禁止越出车道修改其他文件。三个车道互相独立、可并行。

---

## 任务 A：内置 `/panel` 命令（打开设置面板）

### A.1 `LauncherViewModel.cs`

1. `SearchResult` 新增属性：
   ```csharp
   /// <summary>Built-in command id when Kind == "builtin" (e.g. "panel").</summary>
   public string? BuiltinCommand { get; set; }
   ```
2. `SearchResult.Kind` 注释更新为 `"launch" | "script" | "builtin"`。
3. `KindLabel` 增加分支：`Kind == "builtin"` 时返回 `"内置"`（保持 launch→程序、script→脚本 不变）。
4. `LauncherViewModel` 新增内置命令表：
   ```csharp
   /// <summary>Built-in "/" commands (not backed by ScriptItem).</summary>
   public static readonly IReadOnlyList<(string Name, string Description)> BuiltinCommands =
       new (string, string)[] { ("panel", "打开设置面板") };
   ```
5. `Search(string query)`：在现有逻辑基础上——当 `query` 以 `/` 开头时，把内置命令按「去掉 `/` 后的 query 片段是命令名的子串（忽略大小写）」匹配，生成 `SearchResult { Name = "/" + Name, Description = Description, Kind = "builtin", BuiltinCommand = Name }` 并入结果（放在脚本结果之前即可）。`query == "/"` 时应列出全部内置命令。query 不以 `/` 开头时不混入内置命令（保持现有行为）。
6. `FindScriptByName(string cmdName)`：先对 `BuiltinCommands` 做命令名精确匹配（忽略大小写），命中则返回上述 builtin SearchResult；未命中再走现有 ScriptItem 逻辑。方法名保持不变。

### A.2 `MainWindow.xaml.cs`

`ExecuteSelected()` 中，在现有 `launch` / `script` 分支后新增：
```csharp
else if (result.Kind == "builtin" && result.BuiltinCommand == "panel")
{
    OpenSettings();
}
```
（`HideWindow()` 已在方法开头调用，无需重复。）

### A.3 验收

- 输入 `/p`、`/panel` 均出现「/panel — 打开设置面板（内置）」条目；回车后设置窗口打开、启动器隐藏。
- 输入 `/panel xxx`（带空格参数）时通过 `FindScriptByName` 精确命中，回车同样打开设置面板。
- 普通搜索（不以 `/` 开头）结果不受任何影响。

---

## 任务 B：服务管理面板「重启」按钮

### B.1 `ServiceManager.cs`

1. `ServiceEntry` 新增字段：
   ```csharp
   /// <summary>Manual restart requested (skip the 10s countdown, restart immediately).</summary>
   public bool RestartRequested { get; set; }
   ```
2. `ServiceManager` 新增公开方法：
   ```csharp
   /// <summary>Restart a running instance with the same script and same extra args.</summary>
   public void RestartService(string instanceId)
   {
       if (!_services.TryGetValue(instanceId, out var entry)) return;
       entry.RestartRequested = true;
       try { entry.Process?.Kill(entireProcessTree: true); } catch { }
       NotifyChanged();
   }
   ```
   说明：服务运行中 → Kill 触发 `WaitForExit` 返回，RunLoop 进入倒计时段；倒计时循环条件改为同时检查 `RestartRequested`，立即跳出并重跑循环体（同一 `entry`，同样的 `Item` + `ExtraArgs`），即「用同样的参数重启」。服务正处于 10 秒等待重启状态时 → 无进程可杀，`RestartRequested` 直接打断倒计时立即重启。
3. `RunLoop` 倒计时段修改：
   - `for` 循环内 `if (entry.StopRequested) break;` 改为 `if (entry.StopRequested || entry.RestartRequested) break;`
   - `for` 循环结束后、`IsWaitingRestart = false` 附近，重置 `entry.RestartRequested = false;`
   - 倒计时前的日志行可顺带：当 `entry.RestartRequested` 为 true 时写 `=== 手动重启 ===` 而非 `=== 10秒后自动重启 ===`（可选，写了更好）。
4. 不改 `StopService`/`StopAllServices`/`StartService` 现有语义。

### B.2 `SettingsWindow.xaml`

服务管理页 `DataTemplate` 内按钮行（`Grid.Column="2"` 的 StackPanel），在「日志」按钮**左侧**插入：
```xml
<Button Content="重启"
        Tag="{Binding InstanceId}"
        Click="RestartServiceInstance_Click"
        Style="{StaticResource SmallActionButtonStyle}"
        Margin="0,0,6,0"/>
```

### B.3 `SettingsWindow.xaml.cs`

新增处理器（放在 `StopServiceInstance_Click` 附近）：
```csharp
private void RestartServiceInstance_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is string instanceId)
        _serviceManager.RestartService(instanceId);
}
```

### B.4 验收

- 每个服务实例行显示三个按钮，顺序：重启 | 日志 | 停止。
- 点击「重启」：运行中服务立即被杀并用原参数（含 ExtraArgs）重新拉起；日志文件追加新的启动段；UI 状态经 `ServicesChanged` 刷新。
- 处于「等待重启」状态的服务点「重启」立即重启，不再等倒计时。

---

## 任务 C：直接运行输出窗口完整支持 ANSI 转义序列

现状：`OutputWindow.AppendLine` 把整行作为一个单色 `Run` 加入 `OutputText.Inlines`，ANSI 序列（如 `ESC[1m ESC[33m ... ESC[39m`，显示为 `[1m[33m[39m` 乱码）原样输出。

### C.1 新建 `AnsiParser.cs`（namespace `AltRunSharp`）

职责：把一行含 ANSI 转义序列的文本解析为带样式的片段序列，且 **SGR 状态跨行保持**（实例持有状态，`ParseLine` 可被反复调用）。

建议形状（可按实现微调，但能力不许缩水）：
```csharp
public class AnsiSegment
{
    public string Text { get; set; } = string.Empty;
    public Color? Foreground { get; set; }   // null = 使用调用方默认色
    public Color? Background { get; set; }   // null = 透明
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
}

public sealed class AnsiParser
{
    public AnsiParser() { /* 状态 = 默认 */ }
    public List<AnsiSegment> ParseLine(string line);   // 解析并推进内部 SGR 状态
    public void Reset();
}
```

必须支持的序列：

1. **CSI SGR**（`ESC [ params m`），参数以 `;` 分隔，空参数按 0 处理：
   - `0` 全部重置；`1` Bold；`2` 暗淡（可将前景向默认背景色方向变暗或简单忽略，但不得报错）；`3` Italic；`4` Underline
   - `22` 关 Bold/暗淡；`23` 关 Italic；`24` 关 Underline
   - `30–37` 标准前景色、`38` 扩展前景（`38;5;n` 256 色、`38;2;r;g;b` 真彩，参数不足时安全跳过）、`39` 恢复默认前景
   - `40–47` 标准背景色、`48` 扩展背景（同 38 规则）、`49` 恢复默认背景
   - `90–97` 高亮前景、`100–107` 高亮背景
   - 未知 SGR 参数：忽略，不抛异常
2. **其他 CSI 序列**（`ESC [ ... 非m 结尾字节`，如 `K`、`?25l`、光标移动）：整条吞掉，不产生输出
3. **OSC 序列**（`ESC ] ... BEL` 或 `ESC ] ... ESC \`）：整条吞掉
4. 其他 `ESC + 单字符`（如 `ESC(c` 之外的简写）：吞掉
5. 行尾未闭合的残缺转义序列：吞掉，不崩溃
6. 文本中的 `\r`、`\x07`（BEL）、`\x08`（退格）等控制字符：吞掉（`\n` 不会出现——输入按行传入）

颜色表（标准 8 色用 Catppuccin Mocha，高亮 8 色用对应亮色）：

| 码 | 标准 | 高亮 |
| :- | :- | :- |
| 黑 30/90 | `#45475A` | `#585B70` |
| 红 31/91 | `#F38BA8` | `#F5A3B7` |
| 绿 32/92 | `#A6E3A1` | `#B5E8B0` |
| 黄 33/93 | `#F9E2AF` | `#FAE7C3` |
| 蓝 34/94 | `#89B4FA` | `#A5C5FB` |
| 品红 35/95 | `#CBA6F7` | `#D8BFF9` |
| 青 36/96 | `#94E2D5` | `#ABE9DE` |
| 白 37/97 | `#BAC2DE` | `#CDD6F4` |

256 色（`38;5;n`）：0–7 标准色、8–15 高亮色；16–231 为 6×6×6 立方（每通道级别 0–5 → 0/95/135/175/215/255）；232–255 灰阶（8 + 10×(n-232)）。背景 `48;5;n` 同理。

### C.2 `OutputWindow.xaml.cs`

1. 持有一个 `AnsiParser` 实例（两个构造函数各自初始化即可，普通字段）。
2. `AppendLine(string text, bool isError)` 重写：调用 `_ansi.ParseLine(text)`，对每个 `AnsiSegment` 建 `Run`：
   - `Foreground` = 段内前景色 ??（`isError` ? `#F38BA8` : `#CDD6F4`）——即 stderr 默认红保留为兜底色，但 ANSI 显式颜色优先
   - `Background` = 段内背景色（null 不设）
   - Bold → `FontWeights.Bold`；Italic → `FontStyles.Italic`；Underline → `TextDecorations.Underline`
   - 最后一个段之后照常补 `"\n"`（把换行附加到最后一个 Run 的文本，或单独一个仅含 `"\n"` 的 Run）
   - 保持 `Dispatcher.Invoke` + `OutputScroll.ScrollToEnd()` 语义不变
3. 空段（Text 为空）跳过；整行无可见文本时也要补换行（否则空行被吞）。
4. 删除/保留 `AppendText` 兼容包装均可（保留更稳）。

### C.3 验收

- 运行输出含 `ESC[1mESC[33mhelloESC[39mESC[0m` 的脚本：窗口中 `hello` 显示为加粗黄色，无任何 `[1m` 之类残留字符。
- 跨行颜色：`ESC[31m` 后未重置的后续行保持红色，直到遇到 `ESC[0m`/`ESC[39m`。
- 含 `ESC[K`、`ESC[?25l` 等控制序列的输出不残留可见乱码。
- stderr 无 ANSI 颜色的行仍为红色兜底；有 ANSI 颜色的 stderr 行用 ANSI 色。
- 工作流构造路径（多步）同样生效（共用 AppendLine）。

---

## 集成验收（协调者执行）

1. `dotnet build AltRunSharp.csproj` 0 错误 0 新增警告。
2. grep 核对本 SPEC 每个验收点对应代码真实存在。
3. 产物级抽查：XAML 按钮顺序、AnsiParser 颜色表、RunLoop 重启路径。
