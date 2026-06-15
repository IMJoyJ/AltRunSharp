// test_args.js — 输出收到的所有命令行参数
const args = process.argv.slice(2);
console.log("=== AltRunSharp 脚本参数测试 ===");
console.log(`收到 ${args.length} 个参数:`);
args.forEach((a, i) => console.log(`  [${i}] ${JSON.stringify(a)}`));
console.log("NODE_PATH:", process.env.NODE_PATH ?? "(未设置)");
console.log("完成。");
