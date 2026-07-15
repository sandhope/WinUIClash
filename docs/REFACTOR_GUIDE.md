# WinUIClash 开发指南

> **本文件是 WinUIClash 后续开发的唯一权威文档。**  
> 所有代码改动前必须先阅读本文件，确保理解架构边界、业务逻辑和已知问题。  
> 之前的重构方向由于盲目模仿移动端 FFI/命名管道架构而导致复杂度失控，现已全面拨乱反正，回归标准的通用核心与 REST API 架构。

---

## 目录

1. [项目目标](#1-项目目标)
2. [架构设计与核心常驻策略](#2-架构设计与核心常驻策略)
3. [核心业务逻辑边界铁律](#3-核心业务逻辑边界铁律)
4. [已知 Bug 清单与修复方向](#4-已知-bug-清单与修复方向)
5. [有价值的技术经验](#5-有价值的技术经验)
6. [精简重构任务列表](#6-精简重构任务列表)
7. [构建与验证](#7-构建与验证)

---

## 1. 项目目标

**在 Windows 平台上 1:1 还原体验模型**，使用 WinUI 3 (C#) 作为 UI 框架，配合通用的 `mihomo.exe` 核心，实现超低延迟、丝滑响应的 VPN 客户端。

---

## 2. 架构设计与核心常驻策略

### 2.1 避坑分析（为什么放弃 FlClash 的命名管道/FFI 架构）

FlClash 源码中之所以大费周章地编译自定义 Go 二进制并使用命名管道控制 `startListener` / `stopListener`，是为了适配 **Android/iOS 移动端**严格的网络权限限制。
在 Windows 桌面端，**完全不需要**这种魔改。通用的 `mihomo.exe` 内置了极为完善的本地 REST API（默认 9090 端口），利用其自带的 `Mode`（运行模式）切换，即可完美实现“内核常驻后台，0延迟开关流量”的需求。

### 2.2 基于 REST API 的流量控制原理

Mihomo 支持动态修改运行模式（Mode）：

- **`direct`（直连模式）**：核心虽然在后台监听端口，但它**不处理、不接管、不代理任何流量**，所有流量原路放行。这就是我们期望的“断开/空载”状态。
- **`rule` / `global`（分流/全局模式）**：核心开始接管流量并按照配置文件进行代理。这就是“已连接”状态。

### 2.3 修正后的三阶段生命周期

```

App 启动
│
▼
【启动进程】后台静默拉起通用 mihomo.exe 进程
│
▼
【初始化】传入默认配置，但将 mode 默认设为 "direct"
│ (此时核心已常驻在内存中，不代理任何流量，0 性能开销)
▼
┌───────────────────────────────────────────────┐
│  isStart = false (界面显示 Play 图标)          │
│  UI 的系统代理开关 = 关闭 / TUN 模式 = 关闭     │
│  流量直连网络，不经过代理                       │
└───────────────────────────────────────────────┘
│
│ 用户点击 UI 上的「开始连接」按钮
▼
【激活代理】

1. UI 发送 PATCH /configs 改变 mode 为 "rule" (或用户的选择)
2. UI 修改 Windows 注册表开启系统代理 (或通过 API 激活 TUN)
│
▼
┌───────────────────────────────────────────────┐
│  isStart = true (界面显示 Pause 图标)         │
│  流量开始经过核心并进行代理分流                │
└───────────────────────────────────────────────┘
│
│ 用户点击 UI 上的「断开连接」按钮
▼
【断开代理】
3. UI 发送 PATCH /configs 将 mode 切回 "direct"
4. UI 关闭 Windows 系统代理 (或通过 API 关闭 TUN)
│ (核心继续常驻，配置在内存中，等待下一次秒开)
▼
┌───────────────────────────────────────────────┐
│  isStart = false (界面显示 Play 图标)          │
└───────────────────────────────────────────────┘
│
│ 用户彻底退出 App
▼
【进程清理】UI 主进程向内核发送信号，并彻底杀掉 mihomo.exe 进程

```

---

## 3. 核心业务逻辑边界（铁律）

### 3.1 核心生命周期

- **App 启动时**：无条件启动 `mihomo.exe` 进程，初始化成功后默认处于 `direct` 模式。
- **App 退出时（彻底退出）**：通过 Helper Service IPC（`POST /stop`）由 SYSTEM 服务**强行 Kill 其拉起的 `mihomo.exe` 核心进程**，释放 7890/9090 端口并自动卸载 TUN 虚拟网卡；若 Helper 不可达，UI 兜底按二进制路径强杀核心。**只有 App 彻底退出才允许杀核心进程。**
  - **WinUIClashHelperService 服务本身保持常驻（开机自启），App 退出时绝不卸载**——低权限 UI 既无权也无需销毁 SYSTEM 服务；常驻可消灭“下次打开再弹 UAC”。完整退出契约见 `WinUIClash_INTEGRATION.md` 5.2。
- **最小化到托盘（MinimizeOnExit=true）**：仅 `Hide()` 窗口，**不杀核心、不卸载服务**，App 仍在后台运行（核心继续常驻）。
- **开始按钮**：切换 `mode`（`direct` $\leftrightarrow$ `rule`），同步切换系统代理/TUN 的开关。**绝对不 toggle 进程**。
- **开始按钮默认状态**：非运行（Play 图标），因为初始化 `isStart` 默认为 `false`。

### 3.2 系统代理（System Proxy）

- **代理开关与开始按钮解耦但联动**：
  - 点击开始按钮 $\rightarrow$ 激活代理模式，同时修改注册表开启 Windows 系统代理。
  - 点击断开按钮 $\rightarrow$ 切回直连模式，同时修改注册表关闭 Windows 系统代理（避免指向死端口）。
- **系统代理端口**：指向 mixed-port（默认 7890）。

### 3.3 TUN 模式与系统服务架构

- **零 UAC 弹窗设计**：为了 1:1 还原丝滑体验，严禁在用户点击 TUN 开关时弹出系统 UAC 提权窗。
- **系统服务接管**：提权由常驻的 Windows 系统服务 `WinUIClashHelper` 代为执行。该服务在软件安装时注册为 SYSTEM 权限服务。
- **动态无感启用**：主 UI 进程仅作为控制端，通过本地 IPC 向后台服务发送启用/禁用指令。服务在后台完成驱动加载与网卡创建，UI 进程和核心进程无需重启，实现秒级无缝切换。
- **退出工作流（服务常驻 / 核心随 UI 销毁）**：App 彻底退出时，UI 向 Helper 发送 `shutdown_core`（`POST /stop`）→ Helper 强制 Kill 核心并清理 TUN/端口，但**服务自身保持常驻**；UI 随后 `SystemProxyService.EnsureDisabledOnExit()` 关闭系统代理并退出。完整流程见 `WinUIClash_INTEGRATION.md`。

### 3.4 后台线程安全（WinRT COM 铁律）

- **后台线程严禁直接操作 WinUI 3 控件或 XAML 对象**，否则必崩（报 `RPC_E_WRONG_THREAD` 错误）。
- 所有来自 WebSocket 日志、流量统计等后台线程的通知，必须经由 `DispatcherQueue.TryEnqueue` 派发回 UI 线程进行渲染。

---

## 4. 已知 Bug 清单与修复方向

### BUG-001: 系统托盘菜单操作全部不生效 [严重]

- **现象**：主界面操作按钮（如系统代理 toggle），托盘图标状态可以同步变化；但在托盘菜单上点击任何按钮（显示主界面、切换代理、退出等），UI 界面无响应，逻辑不触发。
- **疑似根因**：`H.NotifyIcon` 库的 `TaskbarIcon.ContextFlyout` 在 WinUI 3 环境下可能存在事件路由断裂，或者事件绑定的 Lambda 闭包未被保持引用导致被 GC 回收。
- **排查与修复方向**：
  1. 在托盘 Click 处理器第一行加 `Debug.WriteLine` 确认事件是否送达。
  2. 如果不触发，放弃 XAML 中的 `MenuFlyout` 绑定，改用 `H.NotifyIcon` 的原生 `LeftClickCommand` / `RightClickCommand` 在后台手动弹窗或触发逻辑。

### BUG-002: App 启动时开始按钮错误地显示为红色运行状态 [结构性挂起]

- **现象**：软件启动后，仪表盘右下角开始按钮是运行状态（红色/Pause 图标），而此时用户并未点击连接。
- **根因**：之前的代码在 `App.xaml.cs` 启动时调用了 `clash.StartAsync()`，导致直接把 `IsRunning` 置为了 `true`。
- **修复方案**：引入 `mode: direct` 机制后，App 启动拉起进程后将 `IsRunning`（或 `isStart`）设为 `false`，仅当点击开始按钮将模式切为 `rule` 时，才将状态置为 `true`。

### 4.3 核心组件发布约定

- **发布模式**：一律采用 **Unpackaged (免打包)** 模式发布。放弃 MSIX 容器，以规避其注册表虚拟化导致 Windows 系统代理不生效的底层硬伤。
- **交付与部署**：使用传统安装包工具（如 Inno Setup）承载软件安装。在安装阶段由安装程序（具有管理员权限）将 `HelperService.exe` 注册为 Windows 系统服务，确保运行时主 UI 进程及核心进程无 UAC 弹窗，秒开 TUN 模式。

---

## 5. 有价值的技术经验

### 5.1 内置 Geo 数据（geoip / geosite）

Mihomo 启动时若发现 MMDB 缺失，会强制尝试从网络下载，**下载期间核心不监听任何端口，会导致 UI 连接超时**。必须将 `geoip.metadb` 和 `geosite.dat` 内置在 App 的 `Core\` 目录中，在拉起进程前确保它们已被复制到核心的工作目录下。

### 5.2 HttpClient 禁用系统代理

UI 用于连接本地控制端口（如 `127.0.0.1:9090`）的 `HttpClient` 必须设置 `UseProxy = false`，否则当系统代理开启时，控制命令会产生循环代理依赖，导致软件死锁。

---

## 6. 精简重构任务列表

由于放弃了命名管道架构，原有的 `HttpClashService.cs`（基于 REST API 和 WebSocket）得以**全量保留**。重构工作大幅简化为以下几步：

- [ ] **T1**: 确保 `Core\` 目录下打包的是通用的官方 `mihomo.exe`。
- [ ] **T2**: 修改 `ConfigBuildService`，确保导出的初始配置中 `mode: direct`（直连模式）。
- [ ] **T3**: 修改 `ClashOrchestrator` / `CoreProcessService`：
  - `StartAsync()`：仅负责拉起进程，并验证 9090 端口 API 是否畅通，**不改变 `IsRunning` 状态**。
  - `StopAsync()`：重命名为 `ShutdownAsync()`，仅在整个 App 退出时调用。
- [ ] **T4**: 重构 `DashboardViewModel.ToggleCoreAsync`（开始/停止按钮的核心逻辑）：
  - 当 `isStart == true`（用户点击开始）：
    1. 调用 `HttpClashService` 发送 `PATCH /configs`，将 `mode` 修改为 `"rule"`（或用户选定的代理模式）。
    2. 调用 `SystemProxyService.Enable()` 开启 Windows 系统代理（或激活 TUN）。
    3. 将 UI 状态更新为 Running（显示 Pause 图标）。
  - 当 `isStart == false`（用户点击断开）：
    1. 调用 `HttpClashService` 发送 `PATCH /configs`，将 `mode` 修改为 `"direct"`。
    2. 调用 `SystemProxyService.Disable()` 关闭 Windows 系统代理（或关闭 TUN）。
    3. 将 UI 状态更新为 Stopped（显示 Play 图标）。
- [ ] **T5**: 修复 **BUG-001**，确保系统托盘的点击事件能够正确调用上述 `ToggleCoreAsync` 逻辑。

---

## 7. 构建与验证

### 7.1 编译与运行

```bash
# 编译 WinUIClash 桌面端 (x64)
cd D:\code\WinUIClash
dotnet build WinUIClash/WinUIClash.csproj -c Debug -p:Platform=x64

```

### 7.2 运行验证清单

1. App 启动 $\rightarrow$ `mihomo.exe` 在后台静默启动 $\rightarrow$ 此时检查浏览器，流量完全直连（未被代理）。
2. 仪表盘的连接按钮默认显示为 **Play 图标（非运行状态）**。
3. 点击开始连接 $\rightarrow$ 按钮**瞬间**变更为 Pause 图标（无任何延迟） $\rightarrow$ 观察浏览器，流量已成功经过代理。
4. 点击断开连接 $\rightarrow$ 按钮**瞬间**变回 Play 图标 $\rightarrow$ 流量恢复直连，且后台 `mihomo.exe` 进程**依然存活**。
5. 彻底关闭整个 App $\rightarrow$ 任务管理器中 `mihomo.exe` 进程被干净地销毁。

---

_最后更新：2026-07-12_

_维护者：WinUIClash 开发团队_
