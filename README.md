# WinUIClash

> 将 [FlClash](https://github.com/chen08209/FlClash) 的 Flutter UI 迁移至 [WinUI 3](https://github.com/sandhope/WinUIClash) 的实验性项目。

## 项目目标

- **1:1 还原** — 保持 FlClash 的 UI 功能不变，不增加也不减少任何功能点
- **技术验证** — 验证 WinUI 3 在开发效率、性能和稳定性方面的表现

## 技术栈

| 组件 | 说明 |
|------|------|
| 框架 | .NET 8 + Windows App SDK 2.2 |
| UI | WinUI 3 (XAML)，Mica 背景材质 |
| 目标平台 | Windows 10 1809+ (`10.0.17763.0`) |
| 架构 | x86 / x64 / ARM64 |

## 参考项目

- **FlClash** — 基于 Flutter + ClashMeta 的多平台代理客户端
  - 仓库：<https://github.com/chen08209/FlClash>
  - 本地参考路径：`D:\code\refs\FlClash`

## 开发环境

1. Visual Studio 2022（需安装 **Windows App SDK** 工作负载）
2. .NET 8 SDK
3. 打开 `WinUIClash.slnx` 即可开始开发

## 构建与运行

使用 Visual Studio

## 许可证

本项目遵循 [LICENSE.txt](LICENSE.txt) 中的许可条款。
