# WinUIClash 系统集成与退出契约（WinUIClash_INTEGRATION）

> 本文档专门描述 **WinUIClashHelperService（系统服务）** 与 **mihomo 核心进程** 在 App 退出时的处理契约，
> 是工业级 VPN 客户端的标准做法。它与 `REFACTOR_GUIDE.md`（总体开发指南）互为补充：
> 本文件聚焦“**服务常驻 / 核心随 UI 销毁**”这一不可逾越的边界。
>
> **核心铁律：服务常驻、核心随 UI 销毁。**

---

## 0. 总览：两条不可逾越的边界

| 对象                                    | App 彻底退出（主 UI 关闭）时        | 原因                                                                                                         |
| --------------------------------------- | ----------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| **WinUIClashHelperService（系统服务）** | **保留常驻，绝对不卸载**            | 服务的本职是开机自启、后台待命；低权限 UI 退出时既无权也无需销毁 SYSTEM 服务。常驻可消灭“下次打开再弹 UAC”。 |
| **mihomo 核心进程**                     | **彻底 Kill（无论是否由服务拉起）** | 释放 7890/9090 等端口，自动卸载 TUN 虚拟网卡，恢复系统原生网络拓扑，避免用户断网与下次启动端口冲突。         |

---

## 3. 核心生命周期与销毁机制

### 3.1【CoreProcessService】核心进程销毁机制

- **App 启动时**：无条件拉起 `mihomo.exe`，默认 `direct` 模式，核心常驻后台。
- **App 退出时（彻底退出）**：主 UI 向系统服务发送关闭信号（`POST /stop`）。**系统服务必须无条件强行杀死（Kill）其拉起的 `mihomo.exe` 核心进程**，以释放本地网络端口（如 7890/9090）并自动卸载 TUN 虚拟网卡，恢复系统原生网络拓扑。
  - **兜底策略**（`CoreProcessService.KillByBinaryPathAsync`）：即便 Helper Service 不可达 / 崩溃，UI 也会按二进制完整路径扫描并强制结束本 App 对应的 `mihomo` 进程，保证核心一定被清理。
- **系统服务生命周期**：`WinUIClashHelperService` 服务在软件安装时一次性注册，**开机常驻后台，App 退出时保持常驻，不进行卸载**。低权限 UI 退出时无权也无需销毁该服务。
- **只有 App 彻底退出才允许杀核心进程**；核心常驻期间（最小化到托盘 / 仅断连代理）绝不杀进程。

```csharp
// ClashOrchestrator.ShutdownAsync() —— 全退出清理入口
if (_launchedViaHelper)
    await _helperServiceManager.StopCoreViaHelperAsync(); // 经 SYSTEM 服务 Kill 核心（服务本身不退出）
else
    await _processService.StopAsync();                    // 用户态降级：直接 Kill 本地进程
await _processService.KillByBinaryPathAsync();            // 兜底：按路径强制结束残留 mihomo
```

> 注意：本工程使用 `mihomo` 作为核心（对应通用文档中的 `sing-box` 概念）。两者在退出契约上完全一致——均由 SYSTEM 服务拉起、由 UI 退出信号强杀。

---

## 5. 完美的退出工作流

### 5.1 触发条件

- 托盘右键「退出」→ `ExitApp()` → `PerformCleanupAsync()`。
- 或 窗口关闭且 `MinimizeOnExit == false` → `PerformCleanupAsync()`。
- （若 `MinimizeOnExit == true`，点击窗口 X 仅 `Hide()` 到托盘，**不杀核心、不清理**，App 仍在后台运行。）

### 5.2【系统服务接管】服务常驻与退出工作流

当用户在 UI 上点击“彻底退出软件”时，程序内部执行以下严格顺序：

**步骤 1 — UI 向系统服务发送「退出通知」**
主 UI 进程在关闭前，通过本地 IPC（HTTP，127.0.0.1:47891）给常驻后台服务发送关闭指令：
`POST /stop?token=WinUIClashHelper`（业务语义等价于 `{"cmd":"shutdown_core"}`）。

**步骤 2 — 系统服务负责干掉核心进程**
`WinUIClashHelperService` 收到指令后（其 `StopMihomo()`）：

1. 调用 `Process.Kill(true)` 彻底杀死由它拉起的 `mihomo.exe` 进程；
2. 核心退出后自动清理其占用的端口与 Wintun 虚拟网卡；
3. **服务自身继续保持运行（监听状态）**，等待 UI 下一次被打开（无需再次弹 UAC）。

**步骤 3 — UI 清理系统代理并退出**
主 UI 进程关闭前，最后调用 `SystemProxyService.EnsureDisabledOnExit()` 关闭 Windows 注册表系统代理，确保浏览器不会指向已死的本地代理端口；随后 UI 进程正式结束生命周期。

```text
ExitApp()
  └─ PerformCleanupAsync()
       ├─ clash.ShutdownAsync()
       │    ├─ (via Helper) POST /stop  → Helper.StopMihomo() → Kill(mihomo)   [Helper 仍常驻]
       │    └─ KillByBinaryPathAsync()  → 兜底强杀残留核心
       └─ SystemProxyService.EnsureDisabledOnExit()  → 关闭注册表系统代理
  └─ Application.Exit()
```

---

## 6. 为什么这样设计（对照工业级 VPN 客户端）

1. **服务常驻 = 永不重复弹 UAC**：保留常驻意味着下次双击打开主 UI 时，系统服务已在后台就绪，UI 可瞬间通信，永远不需要再弹 UAC 提权窗。
2. **空载几乎不占资源**：主 UI 退出后，服务进入挂起/空载状态，仅监听本地端口，不处理流量、不加载驱动，内存仅几 MB、CPU 占用 0%。
3. **核心必杀 = 自愈网络**：TUN 模式下流量全在虚拟网卡；UI 退出必须 Kill 核心让 wintun 网卡销毁，Windows 网络栈自动接管恢复物理网卡上网，否则用户会直接断网。
4. **端口释放 = 避免下次闪退**：核心占用 7890/9090，若不杀，下次启动新核心会因“Address already in use”崩溃闪退。

---

_最后更新：2026-07-13_
_维护者：WinUIClash 开发团队_
