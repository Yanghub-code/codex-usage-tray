# Codex Usage Tray

[![Build](https://github.com/Yanghub-code/codex-usage-tray/actions/workflows/build.yml/badge.svg)](https://github.com/Yanghub-code/codex-usage-tray/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Yanghub-code/codex-usage-tray)](https://github.com/Yanghub-code/codex-usage-tray/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

一个轻量、开源的 Windows 任务栏监控工具：用两个常驻电池条直接显示 Codex 剩余额度与电脑内存占用。

[English](README_EN.md)

> 本项目为社区工具，与 OpenAI 官方无隶属或背书关系。

## 效果

![Codex Usage Tray 效果](docs/images/taskbar-meter.png)

- **紫色**：Codex 剩余额度。
- **蓝色**：电脑物理内存占用。
- 百分比直接显示，无需点击；悬停可查看详细信息。

## 功能

- 常驻 Windows 任务栏系统托盘左侧，不抢占窗口焦点。
- Codex 额度默认每 **5 分钟**刷新一次。
- 内存占用默认每 **30 分钟**刷新一次。
- 启动时立即读取一次，无需等待首个刷新周期。
- 右键面板可立即刷新、打开 Codex、打开任务管理器或退出。
- 随 Windows 登录自动启动。
- 本地运行，不上传额度、内存或其他遥测数据。

## 系统要求

- Windows 10 或 Windows 11（推荐 Windows 11）。
- 已安装并登录 Codex 桌面应用或 Codex CLI。
- .NET Framework 4.8（受支持的 Windows 10/11 通常已包含）。

## 快速安装

1. 打开 [Releases](https://github.com/Yanghub-code/codex-usage-tray/releases/latest)。
2. 下载 `CodexUsageTray-v2.0.2-win-x64.zip`。
3. 解压压缩包。
4. 在解压目录执行：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
   ```

安装程序会将应用复制到 `%LOCALAPPDATA%\CodexUsageTray`，创建当前用户的开机启动快捷方式，并立即启动监控。

> 当前发行文件未进行商业代码签名。Windows 首次运行时可能显示来源提醒；你可以先核对 Release 中的 SHA-256 文件，或按下方步骤自行构建。

## 使用方法

- 紫色“额度”电池条显示当前限制窗口中较紧张一档的 Codex **剩余百分比**。
- 蓝色“内存”电池条显示电脑物理内存的**已用百分比**。
- 将鼠标悬停在面板上，可以查看额度窗口、重置时间和内存容量详情。
- 右键面板可以立即刷新或退出。
- 最新读数同时写入安装目录下的 `status.json`，便于本地脚本读取。

## 卸载

在发行包解压目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

卸载脚本只会移除 `%LOCALAPPDATA%\CodexUsageTray` 与对应的当前用户启动快捷方式。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
git clone https://github.com/Yanghub-code/codex-usage-tray.git
cd codex-usage-tray
.\scripts\build.ps1
```

生成的程序位于 `artifacts\bin\CodexUsageTray.exe`。创建完整发行包：

```powershell
.\scripts\package.ps1 -Version 2.0.2
```

项目只使用 Windows 与 .NET Framework 自带组件，不需要 NuGet 包。

## 工作原理与隐私

- 内存数据通过 Windows `GlobalMemoryStatusEx` 在本机读取。
- Codex 额度通过本机 `codex app-server --stdio` 接口读取。
- 应用不会读取 Codex 会话内容，也不会收集或上传遥测数据。
- 应用不包含 API Key、访问令牌或用户路径。

Codex 的本机接口可能随产品版本变化。如果额度读取失效，请提交 Issue，并附上应用版本和脱敏后的错误信息。

## 已知限制

- 当前只跟随主显示器的 Windows 任务栏。
- Windows 任务栏位于屏幕底部时显示效果最佳。
- 发行文件暂未进行代码签名。

## 参与贡献

请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。安全问题请按照 [SECURITY.md](SECURITY.md) 私下报告。

## 许可证

[MIT License](LICENSE)
