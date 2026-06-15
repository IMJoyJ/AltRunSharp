// test_env.cs — 输出环境变量和命令行参数（用 dotnet run 执行）
using System;
using System.Runtime.InteropServices;

var cmdArgs = Environment.GetCommandLineArgs();
Console.WriteLine("=== AltRunSharp C# 脚本测试 ===");
Console.WriteLine($"运行时: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"收到 {cmdArgs.Length - 1} 个参数:");
for (int i = 1; i < cmdArgs.Length; i++)
    Console.WriteLine($"  [{i - 1}] {cmdArgs[i]}");

Console.WriteLine("\n部分环境变量:");
foreach (var key in new[] { "USERNAME", "COMPUTERNAME", "OS", "TEMP" })
    Console.WriteLine($"  {key} = {Environment.GetEnvironmentVariable(key) ?? "(未设置)"}");

Console.WriteLine("完成。");
