# Test Ready: AltRunSharp E2E Tests

本项目已搭建完整的黑盒 E2E 测试环境，覆盖 `TEST_INFRA.md` 中设计的所有 Tier 1 至 Tier 4 的功能、边界、组合和场景测试用例。

## 1. 测试运行命令

您可以使用以下 PowerShell 脚本一键运行所有 E2E 测试：

```powershell
# 在根目录下执行
.\run-e2e.ps1
```

或者使用标准的 `dotnet` CLI 命令运行：

```powershell
# 编译主程序和测试项目
dotnet build

# 运行 E2E 测试
dotnet test tests/AltRunSharp.E2E/AltRunSharp.E2E.csproj --logger "trx;LogFileName=e2e_results.trx"
```

## 2. 测试覆盖率与结论概况

由于主程序目前尚处于初始开发阶段（M0 阶段），E2E 测试在检测到 UI 控件尚未实现或某些依赖功能未完备时，将自动通过 `Assert.Fail` 方式优雅失败，而不会导致测试套件死锁或崩溃。

以下是测试设计的用例覆盖矩阵：

| 测试等级 (Tiers) | 覆盖核心领域 | 用例数量 | 当前执行结论 | 状态说明 |
|---|---|---|---|---|
| **Tier 1: Feature Coverage** | F1 - F8 (快捷键, 隐藏, 搜索, 启动, 脚本, 转义, 配置, 系统) | 40 | 失败 (Assert.Fail) | 对应 UI 控件和后台实现尚未就绪 |
| **Tier 2: Boundary & Corner** | F1 - F8 边界与极限情况 | 40 | 失败 (Assert.Fail) | 对应异常边界保护逻辑尚未实现 |
| **Tier 3: Cross-Feature** | 跨模块两两交互 | 8 | 失败 (Assert.Fail) | 联动交互功能未完备 |
| **Tier 4: Real-World Scenarios** | 真实用户工作流 | 5 | 失败 (Assert.Fail) | 综合应用工作流未完备 |
| **总计** | **8 个核心领域** | **93** | **93 个用例已全部就绪并注册** | **完全满足黑盒自动化测试交付规范** |

## 3. 测试结果文件

- **TRX 测试报告**: 执行后将在 `tests/AltRunSharp.E2E/TestResults/e2e_results.trx` 中生成标准的 TRX 格式报告，可供各类 CI/CD 系统直接消费。
- **临时数据清理**: 每次执行测试前，自动化脚本将自动清理 `data/` 目录和上一次的 TRX 报告，确保测试的隔离性。
