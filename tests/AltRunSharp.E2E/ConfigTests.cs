using System;
using System.IO;
using NUnit.Framework;
using FlaUI.Core.AutomationElements;

namespace AltRunSharp.E2E
{
    [TestFixture]
    public class ConfigTests : E2ETestBase
    {
        [Test]
        public void T1_F7_01_AddLaunchItem()
        {
            RunTest(() =>
            {
                throw new InvalidOperationException("设置窗口添加启动项未实现");
            }, "F7_01: 设置界面添加快速启动项，关闭设置窗口，验证 config.json");
        }

        [Test]
        public void T1_F7_02_ManualEditConfig()
        {
            RunTest(() =>
            {
                string testConfig = @"{
  ""hotkey"": ""Alt+R"",
  ""doubleCtrl"": false,
  ""tripleCtrl"": false,
  ""commands"": [
    {
      ""name"": ""custom_calc"",
      ""path"": ""calc.exe"",
      ""type"": ""exe""
    }
  ]
}";
                WriteConfigWithRetry(testConfig);

                TearDown();
                SetUp();

                throw new InvalidOperationException("输入框搜索手动添加的快捷命令未实现");
            }, "F7_02: 修改 config.json 增加快捷命令，验证重启后可搜索到");
        }

        [Test]
        public void T1_F7_03_DeleteLaunchItem()
        {
            RunTest(() =>
            {
                throw new InvalidOperationException("设置窗口删除启动项未实现");
            }, "F7_03: 在设置界面删除快捷启动项，验证 config.json 清除");
        }

        [Test]
        public void T1_F7_04_ChangeHotkeyConfig()
        {
            RunTest(() =>
            {
                throw new InvalidOperationException("热键设置保存未实现");
            }, "F7_04: 修改全局热键设置并保存，验证 config.json 字段更新");
        }

        [Test]
        public void T1_F7_05_CorruptConfigDowngrade()
        {
            RunTest(() =>
            {
                WriteConfigWithRetry("{ corrupt json }");

                TearDown();
                SetUp();

                var window = App!.GetMainWindow(Automation!);
                Assert.That(window, Is.Not.Null);
            }, "F7_05: 损坏的 config.json (不合法格式)，程序启动优雅降级");
        }

        [Test]
        public void T2_F7_01_ReadOnlyConfig()
        {
            RunTest(() =>
            {
                throw new InvalidOperationException("设置保存失败提示未实现");
            }, "F7_01(T2): config.json 只读状态修改设置，给出保存失败弹窗且不崩溃");
        }

        [Test]
        public void T2_F7_02_LockedConfig()
        {
            RunTest(() =>
            {
                throw new NotImplementedException("配置文件进程锁定重试未实现");
            }, "F7_02(T2): config.json 被占用锁定，读取时重试或使用备用缓存");
        }

        [Test]
        public void T2_F7_03_ConcurrentConfigWrites()
        {
            RunTest(() =>
            {
                throw new NotImplementedException("并发配置修改与写入保护未实现");
            }, "F7_03(T2): 多次反复并发修改配置并保存，数据不截断或为空");
        }

        [Test]
        public void T2_F7_04_HugeConfigPerformance()
        {
            RunTest(() =>
            {
                throw new NotImplementedException("大规模配置加载性能优化未实现");
            }, "F7_04(T2): 数千个命令的大文件，加载时间在 1 秒以内");
        }

        [Test]
        public void T2_F7_05_CustomConfigPath()
        {
            RunTest(() =>
            {
                throw new NotImplementedException("自定义配置路径未实现");
            }, "F7_05(T2): 修改配置保存路径，正确在新路径读写");
        }

        [Test]
        public void T3_01_ChangeHotkeyReload()
        {
            RunTest(() =>
            {
                throw new InvalidOperationException("修改热键重载配置交互未实现");
            }, "T3_01: 修改全局快捷键 -> 保存配置 -> 重新加载配置 -> 验证新热键可用旧热键失效");
        }
    }
}
