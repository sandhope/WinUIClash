## 支持命令行启动

对比 [官方文档](https://learn.microsoft.com/zh-cn/windows/apps/get-started/start-here?tabs=command-line) 介绍的命令行使用方式

```
dotnet new winui -n WinUIClash
```

发现生成的配置不一样

```diff
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
-   <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
+   <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>WinUIClash</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x86;x64;ARM64</Platforms>
-   <RuntimeIdentifiers>win-x86;win-x64;win-arm64</RuntimeIdentifiers>
+   <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())</RuntimeIdentifier>
-   <PublishProfile>win-$(Platform).pubxml</PublishProfile>
+   <PublishProfile Condition="Exists('Properties\PublishProfiles\win-$(Platform).pubxml')">win-$(Platform).pubxml</PublishProfile>
    <UseWinUI>true</UseWinUI>
    <WinUISDKReferences>false</WinUISDKReferences>
    <EnableMsixTooling>true</EnableMsixTooling>
+   <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="Assets\SplashScreen.scale-200.png" />
    <Content Include="Assets\LockScreenLogo.scale-200.png" />
    <Content Include="Assets\Square150x150Logo.scale-200.png" />
    <Content Include="Assets\Square44x44Logo.scale-200.png" />
    <Content Include="Assets\Square44x44Logo.targetsize-24_altform-unplated.png" />
    <Content Include="Assets\StoreLogo.png" />
    <Content Include="Assets\Wide310x150Logo.scale-200.png" />
  </ItemGroup>

  <ItemGroup>
    <Manifest Include="$(ApplicationManifest)" />
  </ItemGroup>

  <!--
    Defining the "Msix" ProjectCapability here allows the Single-project MSIX Packaging
    Tools extension to be activated for this project even if the Windows App SDK Nuget
    package has not yet been restored.
  -->
  <ItemGroup Condition="'$(DisableMsixProjectCapabilityAddedByProject)'!='true' and '$(EnableMsixTooling)'=='true'">
    <ProjectCapability Include="Msix" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000.2270" />
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.2.0" />
+   <PackageReference Include="Microsoft.Windows.SDK.BuildTools.WinApp" Version="0.4.0" />
  </ItemGroup>

  <!--
    Defining the "HasPackageAndPublishMenuAddedByProject" property here allows the Solution
    Explorer "Package and Publish" context menu entry to be enabled for this project even if
    the Windows App SDK Nuget package has not yet been restored.
  -->
  <PropertyGroup Condition="'$(DisableHasPackageAndPublishMenuAddedByProject)'!='true' and '$(EnableMsixTooling)'=='true'">
    <HasPackageAndPublishMenu>true</HasPackageAndPublishMenu>
  </PropertyGroup>

  <!-- Publish Properties -->
  <PropertyGroup>
    <PublishReadyToRun Condition="'$(Configuration)' == 'Debug'">False</PublishReadyToRun>
    <PublishReadyToRun Condition="'$(Configuration)' != 'Debug'">True</PublishReadyToRun>
    <PublishTrimmed Condition="'$(Configuration)' == 'Debug'">False</PublishTrimmed>
    <PublishTrimmed Condition="'$(Configuration)' != 'Debug'">True</PublishTrimmed>
  </PropertyGroup>
</Project>
```

## 2. 核心差异对比

以下是项目配置文件中四个关键维度的变更详情：

### 变化一：目标框架升级

目标框架升级到 net10.0

```diff
- <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
+ <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
```

### 变化二：运行时架构标识符优化

- **精准编译**：开发阶段仅针对当前机器架构进行编译和依赖还原，彻底避免为 x86、ARM64 等非目标架构执行无用构建。
- **效率提升**：显著减少 NuGet 包还原数量与编译耗时，大幅提升本地开发迭代速度。

```diff
- <RuntimeIdentifiers>win-x86;win-x64;win-arm64</RuntimeIdentifiers>
+ <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-$(ProcessArchitecture)</RuntimeIdentifier>
```

### 变化三：发布配置文件条件加载

- **构建健壮性**：通过 `Exists()` 条件检查，确保仅在发布配置文件实际存在时才加载。
- **开发体验**：解决在命令行执行 `dotnet build` 或 `dotnet run` 时，因未生成发布配置而导致的构建报错问题，实现“开箱即用”的流畅体验。

```diff
- <PublishProfile>win-$(Platform).pubxml</PublishProfile>
+ <PublishProfile Condition="Exists('Properties\PublishProfiles\win-$(Platform).pubxml')">win-$(Platform).pubxml</PublishProfile>
```

### 变化四：启用隐式 Using 指令

- **代码精简**：自动引入 `System`、`System.Collections.Generic`、`System.Linq` 等高频命名空间。
- **减少冗余**：大幅减少手动编写 `using` 指令的工作量，使业务代码更加聚焦、整洁。

```diff
+ <ImplicitUsings>enable</ImplicitUsings>
```

### 变化五：添加命令行构建工具包

`Microsoft.Windows.SDK.BuildTools.WinApp`，是微软专门为 .NET 开发者提供的命令行开发与打包工具集

它会挂接到 .NET CLI run 目标，以注册调试标识并以 MSIX 包标识启动应用——无需手动部署。

```diff
+   <PackageReference Include="Microsoft.Windows.SDK.BuildTools.WinApp" Version="0.4.0" />
```

可以通过命令行运行

```bash
dotnet run --project WinUIClash\WinUIClash.csproj --verbose
dotnet run --project WinUIClash\WinUIClash.csproj -c Debug -r win-x64 --verbose
```
