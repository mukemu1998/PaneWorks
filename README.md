# PaneWorks

PaneWorks 是一个面向 Windows 的桌面工作区分割与窗口吸附工具。

当前版本已经支持：

- 透明全屏布局编辑
- 递归分割、删除分割、拖动分割线调比例
- 布局保存、加载、删除
- Shift 等自定义快捷键触发窗口吸附
- 托盘最小化与托盘快速切换布局
- Windows 自包含免依赖发布

## 仓库结构

```text
PaneWorks/
  src/
    PaneWorks.App/             WPF 桌面应用
    PaneWorks.Core/            布局模型与核心编辑逻辑
    PaneWorks.Infrastructure/  持久化与 Win32 集成
  docs/
    technical-design.md
    technical-design.zh-CN.md
  scripts/
    package_portable.ps1
  artifacts/                   发布产物输出目录（已忽略）
```

## 本地开发

环境：

- Windows 10/11
- .NET 8 SDK

运行：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\PaneWorks.sln
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\src\PaneWorks.App\PaneWorks.App.csproj
```

## 版本迭代建议

PaneWorks 现在已经适合按“小步发布”的方式慢慢加功能。推荐约定：

1. 在 [Directory.Build.props](E:\GITHUB\PaneWorks\Directory.Build.props) 里只维护一个 `VersionPrefix`。
2. 每次发布前只做三件事：
   - 改版本号
   - 跑打包脚本
   - 把源码和 `artifacts/releases/` 里的 zip 一起发到 GitHub
3. 发布说明按用户看得懂的方式写，不要只写技术改动。

版本号建议：

- `0.1.x`：当前原型持续打磨阶段
- `0.2.x`：多显示器、窗口兼容性、布局绑定等核心能力补齐
- `1.0.0`：功能和稳定性都够日常使用后再上

## 免依赖打包

项目已经带好打包脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package_portable.ps1
```

默认行为：

- 发布 `win-x64`
- `Release` 自包含
- 单文件 exe
- 自动输出 zip

产物目录：

```text
artifacts/
  releases/
    v0.1.0/
      PaneWorks-v0.1.0-win-x64-portable.zip
```

## GitHub 上传建议

源码仓库建议上传：

- `src/`
- `docs/`
- `scripts/`
- `README.md`
- `PaneWorks.sln`
- `Directory.Build.props`

Release 页面建议额外上传：

- `PaneWorks-v0.1.0-win-x64-portable.zip`

这样别人拿源码可以继续开发，普通用户直接下载 zip 解压就能用，不需要再安装 .NET。
