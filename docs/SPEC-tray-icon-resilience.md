# SPEC — 托盘图标韧性修复（tray icon resilience）

> 状态：实施规范（唯一对齐依据）
> 日期：2026-09-27
> 文件车道归属：**仅 `MainWindow.xaml.cs`**（其余文件一律禁改）

## 1. 问题与根因

现象：程序运行数日后右下角托盘图标丢失，所有后台服务仍正常。

### R1（本次事故的直接根因，已实锤）

Explorer.exe 于 2026-09-23 23:42 重启（进程启动时间取证），而 AltRunSharp 主进程（PID 30212）自 2026-09-14 09:20 起持续运行。Explorer 重建任务栏时会广播 `RegisterWindowMessage("TaskbarCreated")`，**所有托盘图标必须重新 NIM_ADD 注册**，否则永久丢失。

当前代码（`MainWindow.xaml.cs`）：
- `InitTray()` 只在启动时调用一次 `Shell_NotifyIcon(NIM_ADD, ...)`；
- `WndProc` **没有**处理 TaskbarCreated 消息；
- `NIM_ADD` 返回值被忽略（若启动早于 shell 就绪，图标永远不会出现）。

### R2（潜伏缺陷，必须一并修复）

`LoadTrayIcon()` 返回 `new System.Drawing.Icon(stream).Handle` 后，`Icon` 托管对象无人持有。**GC 终结器会随时 `DestroyIcon` 该 HICON**——shell 端不会复制图标句柄，句柄一毁图标即消失/变空白。这与 GC 时机相关，符合"偶现"特征，是独立于 R1 的第二致失路径。

### 用户线索说明

"其他程序终止子服务进程"与托盘图标**无关**：图标归主进程所有，子服务进程生死不影响它。本次事故由 R1 解释；R2 是代码审查发现的独立潜伏缺陷。

## 2. 修复方案（全部在 `MainWindow.xaml.cs` 内）

### 2.1 新增 P/Invoke 与字段

```csharp
[DllImport("user32.dll", CharSet = CharSet.Auto)]
private static extern int RegisterWindowMessage(string lpString);

// 类字段
private static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
private System.Drawing.Icon? _trayIcon;          // 当前持有（拥有所有权）的图标对象
private DispatcherTimer? _trayRetryTimer;
private int _trayRetryCount;
private const int MaxTrayRetries = 12;           // 5s × 12 ≈ 1 分钟退避窗口
```

### 2.2 `LoadTrayIcon()` 改为返回**拥有所有权的 Icon 对象**

```csharp
private static System.Drawing.Icon LoadTrayIcon()
{
    try
    {
        var uri = new Uri("pack://application:,,,/icon.ico");
        using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
        if (stream != null)
            return new System.Drawing.Icon(stream);
    }
    catch { }
    // 注意：SystemIcons.Application 是共享句柄，禁止销毁；必须 Clone 出私有副本
    return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
}
```

### 2.3 抽出 `AddTrayIcon()`（幂等重注册入口）

```csharp
private void AddTrayIcon()
{
    var newIcon = LoadTrayIcon();
    _nid.hIcon = newIcon.Handle;
    if (Shell_NotifyIcon(NIM_ADD, ref _nid))
    {
        _trayIcon?.Dispose();      // 销毁旧句柄（确定性释放，不等 GC）
        _trayIcon = newIcon;
        StopTrayRetry();
    }
    else
    {
        newIcon.Dispose();         // 未被 shell 采纳，立即销毁
        StartTrayRetry();
    }
}
```

### 2.4 失败重试（应对启动早于 shell / TaskbarCreated 到达时 shell 未就绪）

```csharp
private void StartTrayRetry()
{
    _trayRetryTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
    _trayRetryTimer.Tick -= TrayRetryTick;
    _trayRetryTimer.Tick += TrayRetryTick;
    _trayRetryCount = 0;
    _trayRetryTimer.Start();
}

private void TrayRetryTick(object? sender, EventArgs e)
{
    if (++_trayRetryCount > MaxTrayRetries) { StopTrayRetry(); return; }
    AddTrayIcon();
}

private void StopTrayRetry() => _trayRetryTimer?.Stop();
```

### 2.5 `InitTray()` 改造

保留 `_nid` 初始化（hIcon 字段可留 `IntPtr.Zero`，由 AddTrayIcon 填充）与菜单构建；
**删除**原来的 `Shell_NotifyIcon(NIM_ADD, ...)` 直调，改为末尾调用 `AddTrayIcon()`。

### 2.6 `WndProc` 顶部新增

```csharp
if (msg == WM_TASKBARCREATED)
{
    // Explorer 重启（崩溃/更新/手动）→ 任务栏重建，托盘图标必须重新注册
    AddTrayIcon();
}
```

注意：这是**广播消息**，不得设置 `handled = true`、不得吞掉（return IntPtr.Zero 即可）。

### 2.7 `Cleanup()` 收尾

```csharp
StopTrayRetry();
Shell_NotifyIcon(NIM_DELETE, ref _nid);
_trayIcon?.Dispose();
_trayIcon = null;
```

## 3. 红线

- 只改 `MainWindow.xaml.cs`；不新增文件、不动 csproj、不动其他 .cs。
- 不改变现有托盘行为语义（左右键、菜单、提示文本）。
- 不引入新包依赖（`System.Drawing` 已在用）。
- `SystemIcons` 共享句柄严禁直接 Dispose/DestroyIcon——必须走 Clone 副本。

## 4. 验收标准

1. `dotnet build AltRunSharp.csproj -c Release` 0 错误 0 警告。
2. 代码审查对照本 spec §2 逐项落实。
3. 机制实测（协调者执行）：临时副本（改 Mutex 名）启动 → 广播 TaskbarCreated → 图标仍在/重现。
4. 部署后下次 Explorer 重启时图标自动恢复（用户侧自然验证）。
