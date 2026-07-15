# WinUIClash 发布指南

本文档说明如何使用 [.github/workflows/release.yml](../.github/workflows/release.yml) 自动化流水线发布 WinUIClash 的 Windows Unpackaged 版本。

---

## 一、发布产物

每次成功的 Release 会附带以下 6 个文件（每种架构 3 个）：

| 文件                                              | 说明                                                                              |
| ------------------------------------------------- | --------------------------------------------------------------------------------- |
| `WinUIClash-Setup-<version>-win-x64.exe`          | x64 双击安装器（Inno Setup 生成，需管理员权限）                                   |
| `WinUIClash-Setup-<version>-win-x64.exe.sha256`   | 上一项的 SHA256 校验值                                                            |
| `WinUIClash-<version>-win-x64.zip`                | x64（AMD64）自包含发布包，含 mihomo x64 核心 + x64 Helper Service（免安装绿色版） |
| `WinUIClash-<version>-win-x64.zip.sha256`         | 上一项的 SHA256 校验值                                                            |
| `WinUIClash-Setup-<version>-win-arm64.exe`        | ARM64 双击安装器                                                                  |
| `WinUIClash-Setup-<version>-win-arm64.exe.sha256` | 上一项的 SHA256 校验值                                                            |
| `WinUIClash-<version>-win-arm64.zip`              | ARM64 自包含发布包，含 mihomo ARM64 核心 + ARM64 Helper Service                   |
| `WinUIClash-<version>-win-arm64.zip.sha256`       | 上一项的 SHA256 校验值                                                            |

不再单独发布 32 位（x86）版本。

**如何选择**：

- 普通用户 → 下载 `WinUIClash-Setup-*.exe`，双击安装，从开始菜单启动，可从控制面板卸载
- 追求便携 / 免安装 → 下载对应 zip 解压即可运行

**安装器行为**：

- 默认安装位置 `C:\Program Files\WinUIClash`
- 创建开始菜单快捷方式；桌面快捷方式默认关闭，可在向导中勾选
- 需要管理员权限（因为 Helper Service 卸载时要 `sc delete`）
- 卸载时自动 `sc stop WinUIClashHelperService` + `sc delete WinUIClashHelperService`

**无需预装运行时**：发布包采用 .NET Self-Contained + [Windows App SDK Self-Contained](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)（pubxml 里 `WindowsAppSDKSelfContained=true`），用户机器**不需要**预装 .NET Runtime 或 Windows App Runtime。代价是单个发布包体积会大一些（多出~40–100 MB）。

zip 内目录结构（解压后）：

```
WinUIClash.exe
Microsoft.WindowsAppSDK.Runtime.dll
... (其余 .NET / WinUI 依赖)
Core/
  mihomo.exe          # 或 mihomo-arm64.exe
  config.yaml
  mihomo-LICENSE.txt
  mihomo-NOTICE.txt
  WinUIClash.HelperService/
    WinUIClash.HelperService.exe
    ...
```

---

## 二、触发方式

流水线支持两种触发：

### 1. Tag 触发（正式发布）

推送形如 `v*` 的 tag 时，自动构建两个架构的包并创建 GitHub Release。

```powershell
# 例：发布 v1.2.0
git tag v1.2.0
git push origin v1.2.0
```

规则：

- Tag 必须以 `v` 开头（如 `v1.0.0`、`v0.3.5`）
- 带连字符的 tag 会被标记为 **prerelease**（例如 `v1.0.0-rc1`、`v1.0.0-beta.2`）
- 不带连字符的 tag 会作为正式版发布（例如 `v1.0.0`）
- Release 名称为 `WinUIClash <tag>`，Release Notes 由 GitHub 根据自上一个 tag 以来的 commit / PR 自动生成
- 产物文件名中的版本号会去掉 `v` 前缀，例如 tag `v1.2.0` → `WinUIClash-1.2.0-win-x64.zip`

### 2. 手动触发（测试构建）

在 GitHub 仓库 **Actions** 页面选择 `Build & Release` 工作流，点击 **Run workflow**：

- `version` 输入：用于产物文件名，默认 `dev`（例：`WinUIClash-dev-win-x64.zip`）
- 只会上传 Actions artifact，**不会**创建 GitHub Release
- artifact 保留 14 天，可在 Run 详情页下载

---

## 三、常规发布流程

以发布 `v1.2.0` 为例：

1. **本地验证**（可选但推荐）

```powershell
# 拉核心到 WinUIClash/Core/
./download-core.ps1

# 本地 publish x64 验证一次
dotnet publish WinUIClash/WinUIClash.csproj -c Release -p:Platform=x64 `
      -p:PublishProfile=Properties/PublishProfiles/win-x64.pubxml

// 不使用Properties里面的文件
// dotnet publish WinUIClash/WinUIClash.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false
// 或者 -o 指定输出目录
// dotnet publish WinUIClash/WinUIClash.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -o ./publish
```

确认 `WinUIClash/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/` 下能正常启动。

2. **合并所有变更到默认分支**，确保 CI 通过。

3. **更新版本号**（如需要）
   - 目前流水线的产物版本号来自 tag，因此 `Package.appxmanifest` 中的版本号不影响 zip 命名；但如希望应用内“关于”页面显示的版本号一致，请手动同步。

4. **打 tag 并推送**

   ```powershell
   git tag v1.2.0 -m "Release v1.2.0"
   git push origin v1.2.0
   ```

5. **在 Actions 页面观察进度**
   - `Build x64` 与 `Build arm64` 并行执行
   - 两个 build job 全部成功后 `Publish GitHub Release` 才会运行
   - 总耗时约 8~15 分钟（取决于 runner 与缓存状态）

6. **完成后到 Releases 页面**
   - 检查 8 个附件是否齐全（每个架构：`.exe` 安装器 + `.exe.sha256` + `.zip` 便携包 + `.zip.sha256`）
   - 检查自动生成的 Release Notes 是否需要补充
   - 如需修改说明文字，直接在 GitHub 上编辑该 Release

---

## 四、版本号规范建议

推荐遵循 [SemVer](https://semver.org/lang/zh-CN/)：

| 场景         | tag 示例                    | 是否 prerelease  |
| ------------ | --------------------------- | ---------------- |
| 正式版       | `v1.0.0`、`v1.2.3`          | 否               |
| 候选版       | `v1.0.0-rc1`、`v1.0.0-rc.2` | 是               |
| Beta         | `v1.0.0-beta.1`             | 是               |
| Alpha / 内测 | `v1.0.0-alpha.1`            | 是               |
| CI 冒烟      | `v0.0.0-ci-test`            | 是（用完请删除） |

---

## 五、发布前自检清单

- [ ] `download-core.ps1` 可以在本地跑通（网络受限时优先跑一次确认上游 asset 命名未变）
- [ ] `WinUIClash/Core/config.yaml` 无本地测试残留（密钥、订阅、私有节点等）
- [ ] `Package.appxmanifest` 版本号已同步（如需在 UI 中显示）
- [ ] 所有 PR 已合并、CI 通过
- [ ] 本地 `dotnet publish` x64 至少验证过一次
- [ ] Release Notes 关键变更已在 commit / PR 标题里体现（便于自动生成）

---

## 六、故障排查

### 1. `Setup .NET 10 SDK` 步骤失败

**现象**：`setup-dotnet@v4` 找不到 `10.0.x` 版本。

**处理**：

- 在 [.github/workflows/release.yml](../.github/workflows/release.yml) 的对应步骤把 `dotnet-version: '10.0.x'` 改为具体的 GA 版本（例如 `10.0.100`），或加上 `dotnet-quality: preview`。

### 2. `Download mihomo core binaries` 失败

**现象**：`No matching asset found in release ...` 或 API 限流。

**处理**：

- 流水线已通过 `secrets.GITHUB_TOKEN` 认证，正常不会触发限流。
- 若是 mihomo 上游改了资产命名（如去掉了 `compatible` 后缀），请更新 [download-core.ps1](../download-core.ps1) 里 `AssetPatterns` 的正则。
- 也可以在 tag 中锁定 mihomo 版本：编辑脚本或改为流水线里显式传 `-Version v1.19.0`。

### 3. `Publish WinUIClash` 失败提示 `Platform` 无效

**现象**：MSBuild 报 `Platform x64` 不存在等。

**处理**：

- 确认 [WinUIClash.csproj](../WinUIClash/WinUIClash.csproj) 的 `<Platforms>x86;x64;ARM64</Platforms>` 中大小写与 matrix 里 `msbuildPlat` 一致（`x64` 小写、`ARM64` 大写）。

### 4. ARM64 包里的 HelperService 不是 ARM64

**现象**：解压 ARM64 zip 后，`Core/WinUIClash.HelperService/WinUIClash.HelperService.exe` 用 `dumpbin /headers` 查看，`machine` 不是 `AA64`。

**处理**：

- 检查流水线日志中 `Rebuild HelperService for ARM64` 这一步是否被执行、是否报错。
- 该步骤依赖 CLI `-r win-arm64` 覆盖 [HelperService.csproj](../WinUIClash.HelperService/WinUIClash.HelperService.csproj) 中的 `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`；如未来把 RID 硬编码方式改动过，需同步调整。

### 5. Release job 提示权限不足

**现象**：`softprops/action-gh-release` 提示 403 / 无法创建 release。

**处理**：

- 确认仓库 **Settings → Actions → General → Workflow permissions** 至少为 **Read and write permissions**。
- 工作流已声明 `permissions: contents: write`，通常无需额外配置。

### 6. 安装器构建失败：找不到 ISCC.exe

**现象**：`Build installer (Inno Setup)` 步骤日志显示 `ISCC.exe still not found after install`。

**处理**：

- 首选：`windows-latest` runner 一般预装 Inno Setup 6，直接可用。
- 若 runner 版本更新导致预装消失，流水线会自动 fallback 到 `choco install innosetup`。如仍失败，可考虑换用 GitHub Marketplace Action `Minionguyjpro/Inno-Setup-Action` 或改用 `winget install JRSoftware.InnoSetup`。

### 7. 安装器提示 "This installation can only run on 64-bit / ARM64 Windows."

**现象**：把 ARM64 安装器拿到 x64 机器上运行（或反之）会被 Inno Setup 直接拒绝。

**处理**：

- 这是[installer/WinUIClash.iss](../installer/WinUIClash.iss) 里 `ArchitecturesAllowed` 的预期行为。请下载与目标机器架构一致的安装器。

---

## 七、撤回 / 重发布

- **撤回一个 Release**：在 GitHub Releases 页删除该 Release，然后 `git push --delete origin v1.2.0` 删除远端 tag。
- **同一 tag 重新构建**：先删除远端 tag 与对应 Release，再重新打 tag 推送。**不要**在同一 tag 上强制推送后期望流水线复跑，`softprops/action-gh-release` 对已存在的 Release 默认不会覆盖附件。

---

## 八、后续可扩展

以下能力当前流水线未实现，如后续有需要可增量添加：

- **代码签名**：作为独立 job，在 `Package zip` / `Build installer` 前用 `signtool` 对 `WinUIClash.exe`、`WinUIClash.HelperService.exe`、`WinUIClash-Setup-*.exe` 签名（需要证书 secret）。
- **MSIX 打包版本**：如恢复 Packaged 发布，可新增 job 生成 `.msix` / `.msixbundle`。
- **自动更新元数据**：生成 `latest.yml` 或类似清单，供应用内更新检测使用。
- **本地打安装器**：不通过 CI 想在本地打一个安装器时，先本地跑一次 `dotnet publish` 得到 `publish/` 目录，然后：

```powershell
# ISCC.exe 路径取决于安装方式：
#   winget 安装: $env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe
#   官网安装:    C:\Program Files (x86)\Inno Setup 6\ISCC.exe
$iscc = if (Test-Path "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
} else { "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" }

& $iscc `
   "/DMyAppVersion=1.2.0" `
   "/DMyAppArch=x64" `
   "/DSourceDir=$PWD\WinUIClash\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish" `
   "/DOutputDir=$PWD\dist" `
   "installer\WinUIClash.iss"
```
