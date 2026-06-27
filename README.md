# PaneWorks

<p align="center">
  <img src="docs/images/PaneWorks-home.png" width="220" alt="PaneWorks 项目图标" />
</p>

<p align="center">
  <strong>面向 Windows 的桌面工作区分割与窗口吸附工具</strong>
</p>

<p align="center">
  在完整桌面上编辑分割线，保存自定义多屏布局，并通过快捷键让窗口按当前布局即时吸附。
</p>

<p align="center">
  <a href="https://github.com/mukemu1998/PaneWorks/releases">下载发布版</a> ·
  <a href="./CHANGELOG.md">更新记录</a> ·
  <a href="./docs/technical-design.zh-CN.md">技术设计</a> ·
  <a href="./docs/github-release-guide.zh-CN.md">发布指南</a>
</p>

<p align="center">
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-2d7dff" />
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512bd4" />
  <img alt="WPF" src="https://img.shields.io/badge/UI-WPF-111827" />
  <img alt="Version 0.1.1" src="https://img.shields.io/badge/Version-0.1.1-22c55e" />
</p>

## 项目简介

PaneWorks 不是固定模板式分屏工具，而是一个允许用户自己定义桌面工作区边界的 Windows 应用。它围绕“先搭布局，再吸附窗口”的工作流设计，重点放在透明桌面编辑、跨屏布局、托盘切换和稳定的窗口吸附体验上。

## 核心能力

| 能力 | 说明 |
| --- | --- |
| 透明桌面编辑 | 直接在桌面透明分割层上编辑布局，只显示分割线，不局限在小工具窗口内部。 |
| 自定义区域分割 | 支持递归横向、纵向、二等分、三等分分割，并可删除分割、拖动边界调比例。 |
| 多显示器合并布局 | 多个屏幕的布局可保存到同一个文件中，切换后可同时在多个屏幕生效。 |
| 快捷键吸附 | 按住自定义触发键拖动窗口时显示布局预览，进入区域后自动高亮并完成吸附。 |
| 运行时联动调整 | 已吸附窗口支持共享边缘联动调整，不改写原始吸附布局。 |
| 自包含发布 | 提供 Windows 自包含便携包，普通用户无需额外安装 .NET Runtime。 |

## 适用场景

- 需要比 Windows 自带布局更自由的分屏方式
- 使用多显示器进行浏览器、文档、聊天、终端协同工作
- 希望保留自己的工作区模板，并在不同布局之间快速切换
- 想要可直接分发、开箱即用的 Windows 桌面工具

## 快速开始

### 普通使用

1. 前往 [Releases](https://github.com/mukemu1998/PaneWorks/releases) 下载最新便携版压缩包。
2. 解压后双击 `PaneWorks.App.exe`。
3. 首次运行后，布局与设置会保存到 `%AppData%\PaneWorks\`。

### 本地开发

- 系统环境：Windows 10 / 11
- SDK 要求：.NET 8 SDK

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\PaneWorks.sln
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\src\PaneWorks.App\PaneWorks.App.csproj
```

### 便携版打包

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -ExecutionPolicy Bypass -File .\scripts\package_portable.ps1
```

默认输出内容：

```text
artifacts/
  releases/
    v0.1.1/
      PaneWorks-v0.1.1-win-x64-portable.zip
      PaneWorks-v0.1.1-win-x64-portable.zip.sha256
      PaneWorks-v0.1.1-win-x64-portable/
        PaneWorks.App.exe
        README.md
        VERSION
```

## 项目结构

```text
PaneWorks/
  src/
    PaneWorks.App/             WPF 桌面应用
    PaneWorks.Core/            布局模型与核心逻辑
    PaneWorks.Infrastructure/  持久化与 Win32 集成
  docs/                        中文说明、设计文档与发布说明
  scripts/                     打包与辅助脚本
```

## 当前版本

当前版本为 `0.1.1`，处于原型打磨与持续迭代阶段。用户自建布局与个人设置保存在 `%AppData%\PaneWorks\`，不会写入仓库源码目录。
