# igame-vulcan-lcd-tool

Windows 上的七彩虹 iGame 火神 LCD 3.0 侧屏控制工具：C# / WinForms 宿主 + Vue 3 / TypeScript / WebView2 界面。1.0.1 安装版和便携版已附带 LCD 厂商库及转换工具，无需另装 iGameCenter。源码开发环境仍可回退到本机安装的厂商库。

本次同步的是当前自用版本，包含实际使用的 Codex、Claude Code 和 SignalRGB 联动；部分脚本仍按本机安装环境编写，并非通用的一键安装发行版。

## 下载

在本私有仓库的 [Releases](https://github.com/zhehaosun717/igame-vulcan-lcd-tool/releases/latest) 下载：

- `GpuLcd-Setup-1.0.1.exe`：每用户安装版。
- `GpuLcd-1.0.1-portable.zip`：完整解压后运行其中的 `GpuLcd/GpuLcd.exe`。
- `SHA256SUMS.txt`：安装包与便携包的校验值。

保留 `vendor/` 目录和 exe 旁的 WebView2 DLL。包内包括 LCD / NVAPI 库、SAVI / FFmpeg、私有 x86 VC90 CRT，以及助手钩子和灯效模板。仍需 Windows x64 / .NET Framework 和兼容的显卡 USB 连接；WebView2 Runtime 未捆绑，缺少时尝试回退经典界面。SignalRGB 联动需另装 SignalRGB。

第三方二进制仅放在私有 Release 附件中，不写入 Git 源码历史。见 [第三方组件说明](THIRD-PARTY-NOTICES.md)。

## 功能

- 屏幕开关、亮度、不透明度、方向、监控页轮播。
- 图片和动画转换、上传、播放与默认画面设置。
- 自定义文字、实时数据、进度条和限时通知。
- Codex / Claude Code 工作、等待、完成状态动画及各自灯光配色。
- SignalRGB 默认彩虹、状态灯效、时间偏移和速率校准。
- 一键关闭屏幕和灯光，恢复后继续原状态。
- 存储维护和开机动画工具。
- 新网页界面及经典 WinForms 备用界面（`--classic`）。

## 源码结构

- 根目录 `*.cs`：宿主、屏幕控制和联动；`WebUi.cs` 为网页界面桥接。
- `ui/`：Vue 3、Nuxt UI、Vite 前端，协议见 `ui/docs/bridge-protocol.md`。
- `integration/`：事件钩子、状态机、SignalRGB 灯效、辅助命令和测试。
- `tests/`：状态机、完成补收、进度通知测试。
- `installer/`、`package.ps1`：安装程序和便携包构建。
- `anims/cc*.gif`、`integration/anims/cx*.gif`：两种助手的状态动画。
- `docs/`：较早版本的技术笔记，部分条目已经过时，以当前实现为准。

## 构建

需要 Windows、Node.js/npm、Visual Studio 2022 Community 的 Roslyn 编译器，以及安装在 `C:\Program Files\iGameCenter` 的厂商 DLL 和转换工具。编译器和厂商路径配置在 `build.cmd`；若存在 `vendor/iGameCenter`，构建和运行均优先使用它。可从完整便携包取出该目录进行开发。

新界面还需要 WebView2 Runtime。在 `lib/` 准备 Microsoft.Web.WebView2 NuGet 包中的以下文件；本机验证版本为 **1.0.4258.31**：

- `lib/net462/Microsoft.Web.WebView2.Core.dll` → `lib/Microsoft.Web.WebView2.Core.dll`
- `lib/net462/Microsoft.Web.WebView2.WinForms.dll` → `lib/Microsoft.Web.WebView2.WinForms.dll`
- `runtimes/win-x64/native/WebView2Loader.dll` → `lib/WebView2Loader.dll`

```powershell
cd ui
npm ci
npm run build
cd ..
.\build.cmd
```

前端产物会嵌入 `GpuLcd.exe`；程序所需的 WebView2 DLL 会复制到 exe 旁。`package.ps1` 可生成安装包和便携包。厂商 DLL、第三方二进制、安装包和编译产物不写入 Git 历史。`package.ps1` 从构建机收集厂商文件和 VC90 x86 CRT，将其放入发行包。

## 联动

保持工具运行，在联动页面启用对应客户端。将 `integration/CodexLCD.template.html` 作为本地 SignalRGB 灯效导入并选中。灯效通过本机事件接口接收状态，工具通过 SignalRGB 自带 MCP 读取确认。

`integration/Hook.cs` 编译为 `GpuLcdHook.exe`。参考 `integration/hooks.example.json` 配置 Claude Code；Codex 示例为 `integration/codex-hooks.example.json`。替换示例安装路径，并将事件项合并到已有配置，避免覆盖其他钩子。动画上传后分别命名为 `ccwork/ccask/ccdone.savi` 和 `cxwork/cxask/cxdone.savi`。

## 验证

```powershell
npm --prefix ui run build
node integration/test-effect.cjs
```

`tests/` 下的 C# 单元测试需与对应状态机 / 完成读取 / 显示任务源码一起编译。`integration/run-webbridge-test.ps1` 是实机测试，会连接设备并要求托盘程序退出；日常源码检查不必运行它。该脚本依赖本机备份，不适用于直接在新机器运行。

`python tools/verify-package.py` 在临时目录验证文件哈希、包内库加载、安装器内嵌资源与便携包一致性，以及独立 GIF→SAVI 转换。不会运行安装器或向屏幕写入指令。

## 数据与运行限制

个人配置保存在 `%APPDATA%\GpuLcdTool`，不随仓库上传。运行日志、设备枚举、布局快照、固件转储、备份、依赖目录和构建输出均排除。

上传期间由宿主控制写入互斥；维护功能可能切换 USB 模式。不要同时运行多个控制屏幕的程序。界面确认框和现有硬件限制仍适用。此工具只在特定硬件环境中验证，不承诺支持所有火神型号。
