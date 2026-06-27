# PaneWorks

<p align="center">
  <img src="docs/images/PaneWorks-home.png" width="220" alt="PaneWorks 项目图标" />
</p>

PaneWorks 是一个面向 Windows 的桌面工作区分割与窗口吸附工具。

当前版本已经支持：

- 透明全屏布局编辑
- 递归分割、删除分割、拖动分割线调比例
- 布局保存、加载、删除
- 自定义快捷键触发窗口吸附
- 托盘最小化与托盘快速切换布局
- 多显示器布局合并保存与跨屏吸附预览
- 吸附窗口拖出后立即恢复吸附前尺寸
- 已吸附窗口共享边缘的运行时联动调整
- Windows 自包含免依赖发布

## 仓库结构

```text
PaneWorks/
  src/
    PaneWorks.App/             WPF 桌面应用
    PaneWorks.Core/            布局模型与核心逻辑
    PaneWorks.Infrastructure/  持久化与 Win32 集成
  docs/
    technical-design.md
    technical-design.zh-CN.md
    portable-readme.zh-CN.md
    release-notes-v0.1.0.zh-CN.md
    github-release-guide.zh-CN.md
  scripts/
    package_portable.ps1
  artifacts/                   发布产物输出目录（已忽略）
```

## 本地开发环境

- Windows 10 / 11
- .NET 8 SDK

## 本地运行

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\PaneWorks.sln
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\src\PaneWorks.App\PaneWorks.App.csproj
```

## 版本迭代方式

PaneWorks 现在已经适合按“小步发布”的方式持续迭代。推荐约定如下：

1. 只在 [Directory.Build.props](./Directory.Build.props) 里维护版本号。
2. 每次发布前只做三件事：
   - 修改 `VersionPrefix`
   - 运行打包脚本
   - 更新 `CHANGELOG.md` 和 GitHub Release 说明
3. 对外发布说明默认写中文，不只写技术改动，也说明用户能感知到的变化。

版本号建议：

- `0.1.x`：原型打磨阶段
- `0.2.x`：多显示器、窗口兼容性、区域绑定等核心能力补齐
- `1.0.0`：日常使用稳定后正式进入第一版

## 免依赖打包

项目已经带好一键打包脚本：

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -ExecutionPolicy Bypass -File .\scripts\package_portable.ps1
```

默认行为：

- 发布 `win-x64`
- `Release` 自包含
- 单文件 exe
- 自动输出 zip

发布产物目录：

```text
artifacts/
  releases/
    v0.1.0/
      PaneWorks-v0.1.0-win-x64-portable.zip
```

## GitHub 更新与上传

完整中文操作说明见：

- [GitHub 更新与发布指南](./docs/github-release-guide.zh-CN.md)

源码仓库建议上传：

- `src/`
- `docs/`
- `scripts/`
- `README.md`
- `CHANGELOG.md`
- `PaneWorks.sln`
- `Directory.Build.props`

GitHub Release 建议额外上传：

- `PaneWorks-v0.1.0-win-x64-portable.zip`

这样开发者可以直接拉源码继续开发，普通用户也可以直接下载 zip 解压运行，不需要额外安装 .NET。
