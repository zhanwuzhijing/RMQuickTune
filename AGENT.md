# AGENT.md

> 本文件是项目的「交接档案」：**需求、修改记录、关键代码要点**都记录在这里。
> 每次改代码都必须同步更新本文件对应小节（尤其是 §4 修改记录）。
>
> 详细需求原文（用户口述、按时间顺序）见 [`docs/需求记录.md`](docs/需求记录.md)。

---

## 1. 项目是什么

**赛事引擎向导 · RMGuide** —— RoboMaster 赛事引擎的现场配置检查工具。Windows / C# / .NET 8 WinForms。

名字取自「麻瓜与向导」：不懂电脑的同学是麻瓜，这软件就是递到手上的那根魔杖/向导，让他们不用懂 IP、进程、版本号也能把赛事引擎跑起来。Windows 里「向导（Wizard）」本身也是「一步步带你做」的意思。

使用场景是赛场现场：选手或裁判的电脑出问题时，几秒内判断「环境哪里不对」，而不是人肉翻任务管理器、查 IP、比版本号。

两条硬约束，任何改动都不能破坏：

1. **免依赖**：Release 为自包含单文件 exe，目标机不装 .NET 运行时、双击即用（现场没网、没管理员权限、没时间装环境）。
2. **普通人看得懂**：颜色即结论（绿=对、红=必须改、黄=注意），不做日志式输出。

---

## 2. 需求

### 2.1 已实现的功能需求

| 模块 | 需求 |
| --- | --- |
| 运行状态（`ProcessStatusPage`） | 监控 10 个进程：裁判端 4（`AdapterSvrS0` / `RMServer` / `RMServerLogClient` / `RoboMasterEngine`）+ 选手端 6（`RMClientDaemon` / `RMClientAssist_VideoRecord` / `_MarkRecognition` / `_SerialPort` / `_RemoteSerialPort` / `RoboMasterClient`）；2 秒刷新；**默认只显示两类总览（运行正常 / 部分运行 / 未启动），且不出现子进程名**；**「一键关闭」两种视图都常驻**；右下角「异常检测」按钮才展开子进程明细 |
| Engine 版本与归属 | 从运行中进程的 `RoboMasterEngine_Data\globalgamemanagers` 读四段式版本号；校验 `RMServer.exe` 是否在当前 engine 的 `RoboMasterEngine_Data\StreamingAssets\RMServer` 树下 |
| 云端版本校验 | `https://edu.dji.com/api/rm/firmwares_versions`；`scene` 推导赛事；只比学生版 `referee&server` + `client` 两项；云端更高=红、更低=黄；带本地缓存与更新时间 |
| 网络配置（`NetworkConfigPage`） | 两个模块（裁判端 / 选手端），判断「该改哪块网卡」并一键应用；无线可兼容但提示；支持存档点与一键重置 |

### 2.2 明确的边界（**不要擅自改回去**）

- 只做**学生版**（`_Student`），不做比赛版映射与版本类型判断。
- Server **只做归属校验**，不显示、不比对 server 版本号（学生版 server 已并入云端 `referee&server` 一条）。
- 版本号只认四段式 `x.x.x.x`，**不兼容**老格式。
- 不显示工具类程序（`RMUploadTool.exe` 已移除）。
- 窗口不自动最大化；卡片小、一屏可见。
- 云端数据：启动自动拉一次，之后用缓存渲染，点刷新才重新拉。
- 网络配置：**无线允许使用**，只提示「建议有线，可能影响稳定性」，不判错。
- 网络配置：**选手端能省则省**——选手端默认「同局域网直连」，不产生任何静态配置；只有学生确实要接 `192.168.12.1` 时才去选第二块网卡。
- 网络配置：**裁判端局域网网卡 = 选手端赛事引擎网卡**，同一张、**双向联动**（改一侧另一侧跟着变），不是两个独立选择器默认同步。
- 网络配置：选手端网卡上留着裁判端配置（`192.168.1.2`）时**只警告不报错**——同机同时开裁判端与选手端是合法用法，能正常工作。
- 界面文案：**不出现「手册」字样**，直接说要求本身；**提示语必须完整显示，不允许省略号截断**；网卡下拉**不设「自动推荐」**，只列真实网卡。
- 命名：涉及选手端的客户端一律写「**自定义客户端**」，不要简写成「客户端」。

### 2.3 网络配置的手册依据

来源：《RoboMaster 赛事引擎联网操作手册（202604）》《…自定义客户端配置说明手册（202604）》。

| 模块 | 网卡角色 | 配置 |
| --- | --- | --- |
| 裁判端 · 方案 1 | 局域网网卡（单网卡直连路由器） | 静态 `192.168.1.2 / 255.255.255.0`，网关 `192.168.1.1`，DNS `192.168.1.1` |
| 裁判端 · 方案 2 | 局域网网卡（双网卡） | 静态 `192.168.1.2 / 24`，网关留空，跃点 20 |
| | 外网网卡 | 自动获取，跃点 10，网段避开 `192.168.1.0/24`、`192.168.12.0/24` |
| 选手端 | 赛事引擎网卡 | 自动获取 IP（DHCP） |
| | 自定义客户端网卡 | 静态 `192.168.12.1 / 24`，网关留空；客户端设备设 `192.168.12.2` |
| 选手端（可选） | 不使用第二块网卡 | 客户端设备接入同一赛事引擎局域网，用本机 DHCP IP 通信 |
| 公共 | 路由器 | LAN 口 `192.168.1.1`、开 DHCP、2.4G、WPA2 |
| 公共 | 防火墙 | 两本手册均要求关闭 |

### 2.4 尚未实现

- 依赖程序检测（运行库、驱动）
- 其他系统设置检查
- 网络模块的进一步形态调整（模块是否拆成侧边栏独立页 / 是否单独做「赛事引擎·路由器」模块 —— 待定）

---

## 3. 关键代码要点

### 3.1 结构

```
src/RMGuide/
├─ Program.cs             入口；含 --netsh-helper 提权分支
├─ MainForm.cs            侧边栏 + 内容区；页面在 RegisterPages() 注册
├─ Controls/              全自绘：NavButton / RoundButton / ProcessCard / CategoryColumn / InfoCard
│                         对话框：CountdownDialog（倒计时）/ RolePickDialog（选择网卡用途）
├─ Core/
│  ├─ Theme.cs            统一配色、字体、圆角路径
│  ├─ PageBase.cs         页面基类（DisplayName / OnActivated / OnDeactivated）
│  ├─ ProcessMonitor.cs   进程清单、快照检测、批量结束
│  ├─ EngineLocator.cs    Win32 取进程路径、读 globalgamemanagers、Server 归属、GameSystemConfig
│  ├─ CloudVersionChecker.cs  云端拉取 + 本地缓存
│  ├─ CloudVersionAudit.cs    赛事推导 + 版本对比
│  ├─ NetworkInspector.cs 网卡枚举（地址/网关/DNS/DHCP）、虚拟网卡过滤、通用连通性探测
│  ├─ NetAdvisor.cs       手册规则：角色推荐、目标生成、差异判定、模块探测
│  ├─ NetConfigApplier.cs netsh 命令构建 + 管理员检测 + UAC 提权执行
│  └─ SnapshotStore.cs    存档点（JSON）
└─ Pages/
   ├─ ProcessStatusPage.cs  运行状态页
   └─ NetworkConfigPage.cs  网络配置页
```

新增页面：继承 `PageBase` → 实现 `DisplayName` → `MainForm.RegisterPages()` 里加一行。

### 3.2 易错点（都踩过）

1. **虚拟网卡不参与自动识别**。VMware / VMnet / Wi-Fi Direct / TAP / 蓝牙都会出现在网卡列表里且有地址。自动推荐只用物理网卡（`NetworkInspector.VirtualKeywords` 判定），但界面**会把虚拟网卡列出来并标注「虚拟」**，允许手动选择（此时给出「不适合作为赛事网络接口」的黄色提示）。
2. **裁判端单/双网卡是两种配置**。单网卡（方案 1）必须带网关 + DNS，否则外网测试过不了；双网卡（方案 2）局域网网卡网关必须留空。由「有没有指定第二块网卡」自动判定。
3. **改网络必须管理员**。`NetConfigApplier.IsAdmin` 判定，非管理员时把命令写成 JSON，用 `runas` 重启自身 `--netsh-helper` 执行，结果落文件回读。`Program.Main` 必须先处理这个分支，不能进 WinForms。
4. **异步刷新要用轮次号**。网络页有 1~3 秒的探测，快速切模块会并发多次刷新；`_epoch` 递增判定，旧结果一律丢弃。早期实现清空 `_adapters` 导致旧请求返回后渲染成「未检测到网卡」。
5. **`TcpClient.ConnectAsync` 返回 `ValueTask`**，`Task.WhenAny` 前要 `.AsTask()`。
6. **`Console.OutputEncoding` 在 WinExe 下会抛异常**（没有控制台），调试输出写文件。
7. **netsh 参数**：静态 `interface ipv4 set address name="X" static <ip> <mask> [gw]`；DHCP 用 `source=dhcp`；DNS 自动 `source=dhcp`，清空 `source=static address=none`；跃点 `set interface name="X" metric=20|automatic`。静态目标 `Dns=null` 表示不动现有 DNS，空列表才清空。
8. **`globalgamemanagers` 是二进制**，取产品名字符串之后第一个匹配 `^\d+\.\d+\.\d+\.\d+$` 的 ASCII 串；不要读 `server_version`。

### 3.3 运行时数据位置

- 云端缓存：`%LOCALAPPDATA%\RMGuide\cloud_versions.json`
- 网络存档点：`%LOCALAPPDATA%\RMGuide\snapshots\snapshot-yyyyMMdd-HHmmss.json`（保留最近 20 个）

### 3.4 项目档案（路径 / 仓库 / 环境）

**仓库**

| 项 | 值 |
|---|---|
| 远程 | `https://github.com/zhanwuzhijing/RMQuickTune.git`（仓库名沿用旧名，未改） |
| 分支 | `main`（跟踪 `origin/main`） |
| 提交身份 | `Zhan_Kong <ldzhankong@foxmail.com>` |
| 忽略规则 | `.gitignore` 已忽略 `.workbuddy/`、`.opencode/`、`.claude/`、`.cursor/`、`CLAUDE.md`、`AGENTS.md`、`bin/`、`obj/`、`*.user` |

**本地路径**

| 路径 | 说明 |
|---|---|
| `D:\RMGuide` | 项目本体（本地仓库根） |
| `D:\RMQuickTune` | **旧路径遗留的空目录**，内建联接指向上表：`src`/`docs`/`.git`/`.workbuddy` 为目录联接（Junction），`.gitignore`/`AGENT.md`/`README.md`/`RMGuide.sln`/`start.bat` 为硬链接。两路径内容同一份，**不是副本**。 |

**工具链**

| 项 | 值 |
|---|---|
| SDK | .NET SDK 10.0.402（本机），目标框架 `net8.0-windows` |
| 输出类型 | `WinExe`（无控制台窗口，调试信息只能写文件） |
| RID | `win-x64` |
| 第三方依赖 | **无**，只用 BCL + WinForms + `System.Management`/P/Invoke |
| Release 发布 | `SelfContained` + `PublishSingleFile` + 压缩，目标机免装运行时 |
| 版本 | `0.1.0` |

**常用命令**（均在项目根执行）

```bash
dotnet build RMGuide.sln -c Debug            # 日常编译
dotnet run --project src/RMGuide -c Debug    # 直接运行
dotnet publish src/RMGuide -c Release        # 单文件发布 → bin/Release/.../publish/RMGuide.exe
start.bat                                    # 优先跑已构建的 exe，找不到才回退 dotnet run
```

**约定**

- 语言：界面文案全中文，**不出现「手册」字样**，不用省略号截断提示；「自定义客户端」永远写全称。
- 每个功能页 = 一个 `PageBase` 子类 + `MainForm.RegisterPages()` 里加一行。
- 颜色语义固定：绿=正常/符合，黄=注意/建议改，红=错误/必须改，灰=未启动/未知。
- 改动后必须同步更新本文件 §2（需求/边界）、§3（要点）、§4（修改记录）。

---

## 4. 修改记录

### 2026-10-20（第十一轮 · 23:14）

**补录项目档案，并把文档纳入仓库**

- 新增 §3.4「项目档案（路径 / 仓库 / 环境）」：远程地址、分支、提交身份、忽略规则、本地新旧两条路径的关系（联接，非副本）、SDK 与目标框架、无第三方依赖、Release 单文件发布参数、常用命令、命名与文案约定。
- 首次把项目文档提交进仓库：`AGENT.md`、`docs/需求记录.md`、`README.md`（此前仓库里只有代码）。
- 本地目录 `D:\RMQuickTune` 已退化为联接壳，真实路径为 `D:\RMGuide`。

### 2026-10-20（第十轮 · 23:02）

**改名：RMQuickTune → 赛事引擎向导 / RMGuide**

- 立意：麻瓜（不懂电脑的同学）需要「向导」才能用赛事引擎；Windows 的「向导 / Wizard」本身就是一步步带做，双关。
- 已改：`src/RMGuide/`、`RMGuide.csproj`、`RMGuide.sln`、命名空间 `RMGuide`、`AssemblyName`/`RootNamespace`/`Product`/`Description`、主窗口标题、侧边栏品牌（「引擎向导」）、`start.bat`、`README.md`、本文件、`docs/需求记录.md`。
- 运行时数据目录改为 `%LOCALAPPDATA%\RMGuide`（旧 `%LOCALAPPDATA%\RMQuickTune` 下的缓存与存档不再被读取）。
- **项目根目录已改名为 `D:\RMGuide`**（23:02 完成，`.git` / `.workbuddy` / `docs` / `src` 全部随迁，`git log` 与 `dotnet build` 均正常）。
- 遗留：`D:\RMQuickTune` 现在是一个**空目录**（WorkBuddy 旧工作区路径被自动重建），不是项目本体，删除与否不影响项目。若想让旧路径仍可用，可用目录联接指回新目录：
  ```cmd
  rmdir "D:\RMQuickTune"
  mklink /J "D:\RMQuickTune" "D:\RMGuide"
  ```

### 2026-10-20（第九轮 · 22:59）

**「一键关闭」常驻**

- 第七轮把子进程卡片藏起来后，一键关闭也跟着没了。改为在总览卡下方常驻两个「一键关闭」（裁判端 / 选手端），与明细视图里的按钮并存，两种视图都随时可用。
- 总览每个单元格改成一个 Panel：`InfoCard`（Dock=Top）+ 按钮，按钮位置跟随卡片高度变化（`card.SizeChanged` / `host.Resize` 时重排）。

### 2026-10-20（第八轮 · 22:57）

**「未运行」不算异常；总览不写子进程名**

- 纠正第七轮：现场本来就只跑其中一部分程序，缺几个不应该标红「运行异常」。
- 总览卡改为三态：全部在运行 → 绿色「运行正常」；部分在运行 → 灰色「部分运行」；全部没运行 → 灰色「未启动」。只显示「3 / 4 个程序在运行」这种数量，**不写任何子进程名**。
- 子进程名只出现在点开「异常检测」后的明细里。

### 2026-10-20（第七轮 · 22:54）

**运行状态页改为「总览 + 角落明细」**

- 默认**不再展示 10 个子进程的卡片**，只显示两张总览卡：裁判端 / 选手端。
  - 全部在运行 → 绿色「运行正常」+「4 / 4 个程序在运行」
  - 部分在运行 → 灰色「部分运行」+「3 / 4 个程序在运行」（**不列进程名**，见第八轮修正）
  - 全部没运行 → 灰色「未启动」
- 右下角保留一个按钮「异常检测」，点开才切换到原来的子进程卡片列表，再点变「收起明细」。

**识别网卡：10 秒倒计时 + 由用户选择用途**

- 倒计时窗（`Controls/CountdownDialog`）显示「剩余 N 秒」，10 秒后自动关闭，期间 UI 正常刷新。
- 识别到唯一变化的网卡后，**不再默认设为某个角色**，而是弹 `Controls/RolePickDialog` 让用户选：
  - 裁判端：设为「局域网网卡」/ 设为「外网网卡」
  - 选手端：设为「赛事引擎网卡」/ 设为「自定义客户端网卡」
- 未选择时「确定」按钮禁用；取消则不改变任何设置。

### 2026-10-20（第六轮 · 22:46）

**虚拟网卡改为「列出并标注」**

- 学生疑惑「为什么网卡下拉只有以太网和 WLAN」。原因是虚拟网卡被整体过滤，但界面没解释。
- 改为：下拉与左侧列表**全部列出**，虚拟网卡显示为「名称（虚拟）」、徽章标「虚拟」，排在物理网卡之后；计数显示「N 块 + M 虚拟」。
- **自动识别仍然排除虚拟网卡**（`DetectEventLanAdapter` 只用物理网卡），只是允许手动选择。
- 提示语区分两种警告：虚拟网卡 → 「不适合作为赛事网络接口，建议改选物理网卡」；无线 → 「建议改用网线」。之前虚拟网卡会被误报成「通过无线连接」。

### 2026-10-20（第五轮 · 22:41）

**裁判端局域网网卡 ↔ 选手端赛事引擎网卡：双向联动**

- 用户要求「绑死」，且「选择一个另一个也会动」。实现为**单一状态** `_lanNic`：两个模块的第一角色读写同一个字段，任一侧改动，另一侧下拉、方案、检查项同时跟着变。
- 切模块时**不再重置** `_lanNic`（它就是共用的那张卡）；只重置第二角色。
- 选手端的赛事引擎网卡行恢复显示（不再隐藏），标签写明「赛事引擎网卡（与裁判端联动）」。
- 第二角色下拉里**排除**已选作第一角色的网卡，同一张卡不能既当引擎网卡又当自定义客户端网卡。

### 2026-10-20（第四轮 · 22:34）

**文案与网卡识别的调整**

- 界面里**不出现「手册」字样**。规则直接写成要求本身（「外网网卡必须避开这两个网段」），不引用出处。
- 提示语**必须完整显示，不许出现省略号**：`InfoCard` 改为按宽度自动换行 + `AutoFit()` 按内容撑高，`ListColumn.ResizeCards()` 里统一调用。
- 网卡下拉**去掉「自动推荐」选项**，只列真实网卡；默认选中检测到的那张（`DetectEventLanAdapter`）。
- 帮助学生对号入座：网卡卡片增加 **MAC 地址、型号、速率、连接状态**；新增 **「识别网卡」** 按钮——让用户拔网线/关 WiFi，程序比对 3 秒内的 Up/地址变化，指出是哪块，并可直接设为当前角色。
- 无线/有线判定不再只靠 `NetworkInterfaceType`：类型为 `Wireless80211`，或名称/型号含 `wi-fi / wifi / wlan / wireless / 802.11 / 无线` 都算无线。
- 选手端文案统一写「**自定义客户端**」，不再简写成「客户端」。第二块网卡的下拉标签为「自定义客户端网卡」。
- 选手端新增「**自定义客户端直连 IP**」：选了同局域网 → 显示本机在该局域网的地址；选了独立网卡 → 显示那张网卡当前的 IP（应用后为 `192.168.12.1`），并提示对端设备应设为 `192.168.12.2`。

### 2026-10-20（第三轮 · 22:28）

**裁判端局域网网卡 = 选手端赛事引擎网卡**

- 新增 `NetAdvisor.DetectEventLanAdapter()`：识别「接赛事引擎局域网的那块网卡」，裁判端局域网网卡与选手端赛事引擎网卡共用同一套判定，保证默认指向同一张。
- 页面记住用户手动选过的这张网卡（`_eventNicPreference`），切换模块时延续，不会一个模块选了 A、另一个默认成 B。

**选手端网卡上是裁判端配置时，只警告不报错**

- 场景：有人把裁判端和选手端开在同一台机器上，网卡保持 `192.168.1.2`。这不符合选手端手册，但**可以正常使用**，所以判 `Warn`（黄），文案说明「一起开就保持原样，只跑选手端时再改自动获取」，不推着用户改。

### 2026-10-20（第二轮 · 22:24）

**选手端配置按「C 方案」省略**

- 决策：选手端没有特殊配置项，能不填就不填。选手端页内**隐藏「赛事引擎网卡」选择行**（它必然是 DHCP，没有可选项），只保留真正需要决定的「客户端网卡」。
- 默认即「不使用（同局域网直连）」：不产生任何静态配置，页面直接显示本机 DHCP IP，学生无需选择。
- `ListColumn.SetExtraHeight()` 让右栏顶部选择区可随模块收缩（裁判端 86px / 选手端 54px）。

### 2026-10-20

**网络配置模块（新增）**

- 读两本 202604 版官方手册，将参数与规则固化到 `NetAdvisor`。
- `NetworkInspector`：网卡枚举 + 虚拟网卡过滤 + 网关/DNS 探测。
- `NetAdvisor`：角色推荐 → 目标配置 → 差异判定；裁判端区分方案 1/方案 2；选手端支持双网卡与「同局域网直连」。
- `NetConfigApplier`：netsh 命令构建、管理员检测、UAC 提权重启自身执行（`--netsh-helper`）。
- `SnapshotStore`：应用/重置前自动存档，支持手动存档、恢复默认（全 DHCP）、恢复存档点。
- `NetworkConfigPage`：左右两栏（网卡状态 / 配置方案）、模块切换、角色下拉（含「不使用」项）、目标对比、存档菜单、关闭防火墙、复制诊断。
- `InfoCard` 通用信息卡片控件。
- 修正 1：虚拟网卡（VMware / Wi-Fi Direct / TAP）曾被推荐成「外网网卡」。
- 修正 2：补上手册「方案 1」——单网卡时局域网网卡需带网关 `192.168.1.1` 与 DNS。
- 修正 3：**无线兼容**——无线可作角色网卡，仅提示不判错。
- 修正 4：选手端支持**同局域网直连**，不需要第二块网卡，页面显示本机 DHCP IP。
- 修正 5：**快速切换模块会显示「未检测到网卡」**——切换时清空 `_adapters` 且旧异步结果未丢弃；改为轮次号 `_epoch` 判定 + 切换时不清空数据。

**工程**

- 建立 `docs/需求记录.md`：把首轮对话中 32 条需求原文整理成文档。
- 建立 `AGENT.md`（本文件）。
- `.gitignore` 增加 agent 痕迹忽略段（`.workbuddy/` 等）。

### 2026-06-06（首个 agent，opencode）

- 初始化仓库（WinForms + .NET 8 + 自包含单文件发布）、`start.bat`、侧边栏外壳。
- 运行状态页：10 个进程监控、裁判端/选手端分栏、一键关闭、主题化自绘控件。
- Engine 版本读取 + Server 归属校验 + 云端版本校验（学生版两项对比）。
- 对话原文可在 `%USERPROFILE%\.local\share\opencode\opencode.db` 查阅（会话 `ses_163d1f9a8f…`）。

---

## 5. 构建与运行

```bash
dotnet run --project src/RMGuide     # 开发运行
dotnet publish src/RMGuide -c Release # 自包含单文件 exe
```

或双击 `start.bat`（优先跑已构建 exe）。

---

## 6. 维护约定

- 改代码后更新 §2 需求（若涉及）、§3 关键要点（若新增坑）、§4 修改记录（必填）。
- 修改记录按日期倒序，写清「改了什么、为什么」。
- 新增页面/模块时，同步补 §3.1 结构图。
