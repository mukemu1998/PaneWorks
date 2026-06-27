# GitHub 更新与发布指南

这份文档用于说明 PaneWorks 后续如何用中文流程持续更新 GitHub 仓库与 Release。

## 一次常规更新的顺序

建议每次版本更新都按这个顺序走：

1. 修改代码
2. 本地编译确认无误
3. 更新版本号
4. 更新 `CHANGELOG.md`
5. 生成便携版压缩包
6. 提交并推送源码到 GitHub
7. 在 GitHub Release 页面上传 zip 与校验文件，并填写中文发布说明

补充建议：

- 提交信息尽量直接写中文，仓库文件列表页会更直观。

## 1. 修改版本号

版本号统一在：

- [Directory.Build.props](../Directory.Build.props)

只需要修改：

```xml
<VersionPrefix>0.1.0</VersionPrefix>
```

例如下一次发版可以改成：

```xml
<VersionPrefix>0.1.2</VersionPrefix>
```

## 2. 本地编译

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\PaneWorks.sln
```

如果当前程序还在运行，先关闭它再编译。

## 3. 更新记录

在：

- [CHANGELOG.md](../CHANGELOG.md)

追加一个新版本条目，建议按下面这种中文结构写：

```md
## v0.1.1

### 新增

- 某某功能

### 优化

- 某某体验优化

### 修复

- 某某问题修复
```

## 4. 打包免依赖版本

运行：

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -ExecutionPolicy Bypass -File .\scripts\package_portable.ps1
```

成功后会在这里产出发布目录：

```text
artifacts\releases\v版本号\
```

例如：

```text
artifacts\releases\v0.1.1\PaneWorks-v0.1.1-win-x64-portable.zip
artifacts\releases\v0.1.1\PaneWorks-v0.1.1-win-x64-portable.zip.sha256
```

## 5. 提交并推送源码

```powershell
& 'C:\Program Files\Git\cmd\git.exe' add .
& 'C:\Program Files\Git\cmd\git.exe' commit -m "feat: 用中文写这里的更新摘要"
& 'C:\Program Files\Git\cmd\git.exe' push
```

提交说明也建议保持简洁明确。

常见写法：

- `feat:` 新功能
- `fix:` 问题修复
- `refactor:` 重构
- `docs:` 文档更新
- `chore:` 杂项整理

## 6. GitHub Release 上传方式

进入仓库：

- [mukemu1998/PaneWorks](https://github.com/mukemu1998/PaneWorks)

然后：

1. 打开 `Releases`
2. 点击 `Draft a new release`
3. Tag 建议用版本号，例如 `v0.1.1`
4. Title 建议写：

```text
PaneWorks v0.1.1
```

5. 发布说明用中文填写
6. 上传对应版本的 `zip` 与 `.sha256` 文件
7. 点击发布

## 7. 发布说明建议写法

建议使用下面这个中文结构：

```md
## 本次更新

- 新增：
- 优化：
- 修复：

## 使用方式

- 下载压缩包
- 解压后运行 `PaneWorks.App.exe`
- 无需额外安装 .NET Runtime

## 当前已知事项

- ...
```

最近一次发布说明可以直接参考：

- [release-notes-v0.1.1.zh-CN.md](./release-notes-v0.1.1.zh-CN.md)

## 8. 推荐习惯

- 源码说明统一写中文
- Release 说明统一写中文
- 每次发版前先更新 `CHANGELOG.md`
- 每次发版后保留对应版本的 zip 产物
- 每次发版后保留对应版本的 `.sha256` 校验文件
- 不要把 `artifacts/` 整个提交进 Git 仓库
- 如果线上已有同版本 Release，但本地代码已经明显超前，优先升一个新版本号，不要直接拿新产物覆盖旧版本语义

## 9. 最短发布流程

如果只是一次小更新，最短可以只做这几步：

1. 改代码
2. `build`
3. 更新 `CHANGELOG.md`
4. 跑 `package_portable.ps1`
5. `git add / commit / push`
6. 去 GitHub Release 上传 zip 与 `.sha256` 并填写中文说明
