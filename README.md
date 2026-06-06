# RMQuickTune

为 **RoboMaster 赛事引擎** 设计的配置检查工具，用于检查并调整网络配置、依赖程序及其他系统设置。

## 项目目标

- 帮助选手 / 现场人员快速检查电脑是否满足赛事引擎运行所需的环境配置
- 提供可视化操作面板，普通用户也能轻松使用
- **免依赖运行**：自包含打包，目标电脑无需安装任何运行时，双击即用
- 尽量兼容更多 Windows 电脑（含较老旧设备）

## 计划功能

> 具体需求逐步完善中，以下为规划方向。

- 网络配置检查（IP / 子网 / 网关 / DNS / 连通性等）
- 依赖程序检测（运行库、驱动等是否已安装）
- 其他系统设置检查
- （可选）一键修复 / 写入正确配置

## 技术栈

- **语言 / 框架**：C# + .NET 8 (LTS) + WinForms
- **运行环境**：Windows (x64)
- **发布方式**：自包含单文件（Self-contained Single File），免运行时依赖

选型说明：WinForms 相比 WPF 对老旧电脑 / 低端显卡兼容性更好、体积更小；自包含发布让用户无需安装 .NET 运行时即可运行。

## 目录结构

```
RMQuickTune/
├─ RMQuickTune.sln              # 解决方案
├─ src/
│  └─ RMQuickTune/              # 主程序（WinForms）
│     ├─ Program.cs
│     ├─ Form1.cs
│     └─ RMQuickTune.csproj
├─ .gitignore
└─ README.md
```

## 开发环境

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 / 11

## 构建与运行

开发调试：

```bash
dotnet run --project src/RMQuickTune
```

发布为免依赖单文件 exe（输出在 `bin/Release/net8.0-windows/win-x64/publish/`）：

```bash
dotnet publish src/RMQuickTune -c Release
```

生成的 `RMQuickTune.exe` 可直接拷贝到目标电脑运行，无需安装 .NET 运行时。

## 许可证

待定。
