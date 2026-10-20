# 赛事引擎向导 · RMGuide

> 让不熟悉电脑的人（麻瓜），也能把 RoboMaster 赛事引擎跑起来。

## 名字的由来

魔法世界里，麻瓜用不了魔法——**除非有人递给他一根魔杖，或者给他一个向导。**

赛场上的情况一模一样：选手只要知道「双击、看颜色、点确定」，不该被 IP、子网掩码、网关、跃点数、进程树这些东西拦住。这个程序做的事，就是把赛事引擎那套环境要求翻译成三步：

1. **看一眼**：页面上是绿的还是红的
2. **点一下**：需要改的，一键改好
3. **反悔**：改完不满意，一键恢复原样

Windows 里的「向导（Wizard）」本来就是这个意思——一步步带你做，不用懂原理。所以中文名取「赛事引擎向导」，工程名 `RMGuide`。

## 它解决什么问题

RoboMaster 赛事引擎对运行环境有一堆要求：哪些程序要在跑、引擎和客户端版本必须和云端一致、网卡要配成特定的静态地址、防火墙要关掉……这些在手册里写得很清楚，但对第一次参赛的同学来说，等于一本天书。

现场出问题时，最常见的场景是：**比赛快开始了，机器连不上，没人知道该改哪。**

RMGuide 的做法是把它变成体检：

| 页面 | 做什么 |
| --- | --- |
| 运行状态 | 裁判端 / 选手端是否正常，一眼看颜色；缺什么点「异常检测」才展开，不吵人；两个分类的「一键关闭」常驻 |
| 网络配置 | 按官方两份配置说明，判断「该改哪块网卡」，一键写入正确配置；应用前自动存档，可一键恢复默认或回到存档点 |

## 设计原则

- **免依赖**：自包含单文件 exe，目标电脑不装 .NET 运行时，双击即用（现场没网、没管理员权限、没时间装环境）
- **颜色即结论**：绿=对，黄=注意，红=必须改，不做日志式输出
- **不吓人**：能自动判断的绝不让人选；判断不了才给选项，并且选项写人话
- **可反悔**：任何修改系统配置的操作，都会先存档

## 技术栈

- C# + .NET 8 (LTS) + WinForms，Windows x64，`PerMonitorV2` 高 DPI
- 自包含单文件发布（SelfContained + PublishSingleFile + 压缩，不产出 pdb）
- 全自绘控件 + 统一 `Theme`，不依赖第三方控件库

选型说明：WinForms 比 WPF 对老机器 / 低端显卡更友好、体积更小；自包含发布保证目标机无需装运行时。

## 目录结构

```
RMGuide/
├─ RMGuide.sln
├─ start.bat                      # 启动脚本：优先跑已构建 exe，否则回退 dotnet run
├─ AGENT.md                       # 需求 / 修改记录 / 关键代码要点（交接档案）
├─ docs/需求记录.md                # 需求原文（按时间顺序）
├─ src/RMGuide/
│  ├─ Program.cs                  # 入口；含 --netsh-helper 提权分支
│  ├─ MainForm.cs                 # 侧边栏 + 内容区，页面在 RegisterPages 注册
│  ├─ Controls/                   # 自绘控件与对话框
│  └─ Core/                       # 进程 / 引擎 / 云端 / 网络 的检测与执行
└─ .gitignore
```

新增功能页：继承 `PageBase` 实现 `DisplayName`，在 `MainForm.RegisterPages()` 里 `AddPage(...)` 一行即可出现在侧边栏。

## 运行时数据

- 云端版本缓存：`%LOCALAPPDATA%\RMGuide\cloud_versions.json`
- 网络配置存档点：`%LOCALAPPDATA%\RMGuide\snapshots\`（保留最近 20 个）

删除这些文件不影响功能，只是下次启动需要重新联网 / 没有可恢复的存档点。

## 开发环境

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（更高版本 SDK 也能编译 net8.0 目标）
- Windows 10 / 11

## 构建与运行

```bash
dotnet run --project src/RMGuide        # 开发调试
dotnet publish src/RMGuide -c Release   # 免依赖单文件 exe
```

或直接双击 `start.bat`。发布产物在 `src/RMGuide/bin/Release/net8.0-windows/win-x64/publish/RMGuide.exe`。

## 许可证

待定。
