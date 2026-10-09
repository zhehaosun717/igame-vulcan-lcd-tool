# igame-vulcan-lcd-tool

脱离 iGameCenter，直接控制七彩虹 iGame 火神（Vulcan）系列显卡上的 LCD 3.0 侧屏，并与 SignalRGB 灯光、命令行 AI 助手状态联动。

本工具通过反射调用随 iGameCenter 一起安装的厂商库 `iGameAPI.LCD.CSharp.dll` 与屏幕通信（屏幕是一块独立的 USB 串口设备，芯片为 Sonix SNC7320），**无需运行 iGameCenter 本体及其后台服务**。在 RTX 3070 Ti Vulcan + MSI B560M + Windows 11 上开发验证。

> 屏幕规格：480×128，自带约 246 MB 存储（贴片式 SD NAND，非插拔卡）。图片/动画需先转换为厂商的 `.savi` 格式并上传进屏幕，再让屏幕循环播放；切换已存在的动画是即时的。

## 功能

- **基本控制**：开关屏幕、亮度（0–10）、背景不透明度、旋转方向。
- **内置监控页轮播**：CPU/GPU/显存/内存 的频率、占用、温度、风扇转速、时间等 14 个页面，可设停留秒数与自动轮播。
- **图片/动画上传**：ffmpeg → 调色板 GIF → 厂商 `SAVI.exe` → `.savi`，支持保持比例/裁切/拉伸三种适配，带进度轮询与上传后大小校验。可设为全屏动画或数据页背景。
- **自定义文字**：字号、颜色（仅支持 ASCII）。
- **实时数据**（实验性）：把 CPU 占用/频率/温度（读 HWiNFO 共享内存）和内存推送到数据页。
- **进度 / 通知挂件**：原生文字进度条与限时通知，到期自动恢复原画面。
- **夜间一键熄灯**：屏幕亮度归零并熄灭 SignalRGB 灯光，带读回校验与串口重发兜底。
- **存储 / 开机动画**：切入 USB 大容量模式后把屏幕存储挂成磁盘，支持带 SHA-256 校验的整盘备份、`.savi` 首帧预览、事务式替换开机动画（临时文件交换 + 校验 + 回滚）。
- **AI 助手状态联动**：接收 Codex 和另一个通用命令行助手（下称“助手 B”）的钩子事件，按会话/轮次状态在屏幕上播放对应动画（工作中/等待授权/本轮结束），并可同步到 SignalRGB 灯效。多会话时优先级为 等待 > 工作 > 结束。

## 目录结构

```
*.cs                     主程序（WinForms）源码，由 build.cmd 编译成 GpuLcd.exe
build.cmd                编译脚本（需 VS2022 自带的 Roslyn csc）
integration/
  LinkState.cs           助手状态机（编入主程序）
  CompletionReader.cs    Codex 会话记录增量读取（编入主程序）
  Hook.cs                GpuLcdHook.exe 源码：钩子事件转发
  DisplaySend.cs         GpuLcdSend.exe 源码：命令行发送进度/通知
  CodexLCD.template.html SignalRGB 本地灯效脚本（随状态变色）
  hooks.example.json     助手钩子配置示例（把命令加进你的助手事件配置里）
tools/make_assistant_anims.py  生成工作/等待/完成三段状态动画（需 Python + Pillow）
tests/                   状态机 / 完成补收 单元测试
docs/                    实时、存储、挂件、联动的详细说明
anims/                   示例状态动画（由生成器产出）
```

## 构建

需要：Windows、已安装 iGameCenter（提供厂商 DLL 与 `SAVIConverter`）、Visual Studio 2022（提供 Roslyn `csc`）。

```bat
build.cmd
```

生成 `GpuLcd.exe`（x64）。辅助工具 `GpuLcdHook.exe`、`GpuLcdSend.exe` 由 `integration/Hook.cs`、`integration/DisplaySend.cs` 单独用 `csc` 编译。

厂商 DLL 默认路径写死为 `C:\Program Files\iGameCenter`（见 `GpuLcd.cs` 的 `Vendor.Root`）。

## 使用

- 双击 `GpuLcd.exe` 打开窗口，或 `GpuLcd.exe --tray` 最小化到托盘自启。界面有 8 个页签：基本、轮播页面、图片/动画、文字、实时数据、联动、进度/通知、存储/开机。
- **命令行**（窗口未运行时直接连屏幕；窗口运行时自动转发给它）：
  ```bat
  GpuLcd.exe status
  GpuLcd.exe upload 图片.gif 名字.savi stretch
  GpuLcd.exe play 名字.savi
  GpuLcd.exe flash 名字.savi 120     :: 播放 120 秒后恢复默认
  GpuLcd.exe revert
  ```
- **助手联动**：用 `tools/make_assistant_anims.py` 生成三段动画，转换上传为 `cxwork/cxask/cxdone.savi`（Codex）和 `abwork/abask/abdone.savi`（助手 B）；把 `integration/hooks.example.json` 里的命令加进助手的事件钩子（把示例路径换成你的实际安装路径）。
- **SignalRGB**：导入 `integration/CodexLCD.template.html` 作为本地灯效并选中；工具通过官方 `127.0.0.1:16034` 事件接口发送状态，灯效随之变色。

## ⚠️ 重要警告

- **上传过程中不要让任何其他写操作占用串口。** 上传途中如果实时刷新、联动或挂件向同一串口写入，字节交错会把屏幕固件写到半截状态，表现为之后不再响应，**只能完全断电（关电源开关 30 秒）才能恢复**。
- **不要播放不完整或不存在的 `.savi` 文件**，同样会让屏幕卡死。
- 上传时请先暂停实时刷新与联动（当前版本的互斥还不完整，见 `docs/optimization-todo.md`）。

## 依赖与致谢

- 厂商库 `iGameAPI.*`、`SAVIConverter`（含 ffmpeg）随 iGameCenter 分发，本项目不包含、不重分发这些文件。
- 本工具仅为个人使用对自有硬件做的封装，与七彩虹、SignalRGB 无关联。
