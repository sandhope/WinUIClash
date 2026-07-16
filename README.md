# WinUIClash

English | [中文](README.zh-CN.md)

> An experimental project that migrates the Flutter UI of [FlClash](https://github.com/chen08209/FlClash) to [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml).

## Project Goals

- **1:1 Replication** — Keep FlClash's UI functionality unchanged, without adding or removing any features
- **Technical Validation** — Validate WinUI 3's performance in development efficiency, performance, and stability

## Tech Stack

| Component | Description |
|-----------|-------------|
| Framework | .NET 10 + Windows App SDK 2.2 |
| UI | WinUI 3 (XAML) |
| Target Platform | Windows 10 1809+ (`10.0.17763.0`) |
| Architecture | x86 / x64 / ARM64 |

## Development Environment

1. Visual Studio 2022 (with **Windows App SDK** workload installed)
2. .NET 8 SDK
3. Open `WinUIClash.slnx` to start development

## Build and Run

Choose one of the two development methods. Running in Visual Studio may show errors.

### Recommended
Use Visual Studio

### Command Line

Refer to [CommandLineStart.md](docs/CommandLineStart.md)

```bash
dotnet run --project WinUIClash\WinUIClash.csproj --verbose
dotnet run --project WinUIClash\WinUIClash.csproj -c Debug -r win-x64 --verbose
```

## License

This project is licensed under the terms in [LICENSE](LICENSE).