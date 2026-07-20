# PaneWorks 便携版

这是 PaneWorks 的 Windows 自包含便携包。

## 三步使用

1. 解压整个压缩包到任意目录
2. 双击 `PaneWorks.exe`
3. 后续布局和设置会自动保存到 `%AppData%\PaneWorks\`

## 这个包适合什么人

- 想直接下载后解压运行
- 不想额外安装 .NET Runtime
- 想把成品包放到 GitHub Release 分发

## 包内文件说明

- `PaneWorks.exe`：主程序
- `README.md`：当前这份快速使用说明
- `LICENSE`：MIT 开源协议
- `VERSION`：当前便携包版本号

## 使用提示

- 推荐在 Windows 10 / 11 下使用
- 如果被系统拦截，请在文件属性中允许执行
- 可在“设置”中开启或关闭“启动时自动检查更新”；也可以在“关于”中手动检查
- 检测到新版本并确认后，PaneWorks 会自动下载、校验、覆盖更新并重新启动；用户布局和设置不保存在程序目录内
