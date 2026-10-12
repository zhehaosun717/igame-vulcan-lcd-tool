# 开发说明（GpuLcd 1.0.1）

## 结构
- **C# 宿主**（WinForms，.NET Framework，`csc` 编译）：唯一允许写串口的一方。`GpuLcd.cs` 主逻辑；`WebUi.cs` 是新界面的桥；`Integrations.cs` / `Widgets.cs` / `NightMode.cs` 等是各功能。
- **新界面**（`ui\`）：Vue 3 + TypeScript + Nuxt UI v4 + Tailwind v4（Vite），经 WebView2 `postMessage` 向 C# 发请求。协议见 `ui\docs\bridge-protocol.md`。前端**不能**直接碰设备。
- 前端构建产物 `ui\dist` 会被 `build.cmd` 打成 zip **嵌进 exe**，成品不依赖散落文件。
- 经典 WinForms 窗口保留作备用：托盘菜单“经典界面（备用）”，或 `GpuLcd.exe --classic`。WebView2 起不来时自动回退。

## 常用命令
| 做什么 | 命令 |
|---|---|
| 只重编 C#（含嵌入已有 dist） | `build.cmd` |
| 改了前端后 | `cd ui && npx vue-tsc --noEmit && npm run build`，再 `build.cmd` |
| 一键出安装包 + 便携版 | `.\package.ps1`（加 `-SkipUi` 复用现有 dist）→ `release\` |
| 真机命令层测试（107 项） | `.\integration\run-webbridge-test.ps1`（会请托盘程序先退出；测后自行重启） |
| 重新生成图标 | `.\tools\make-icon.ps1` |

`package.ps1` 只用 ASCII：Windows PowerShell 5.1 会把无 BOM 的脚本按 ANSI 读。

## 安全约定（改代码前先看）
1. **写串口的入口必须经 `Guard()` / `WebWriteBlock()`**，并尊重 `uploading` 互斥。新增前端命令先写协议，再写 C#，再写真机测试。
2. 上传：前端只拿一次性 token，文件选择和确认框在 C#；拒绝 `.savi`，拒绝覆盖当前默认动画或正在播放的文件；上传不可取消、期间窗口无法退出。
3. 存储维护 / 开机动画预览 / 上传都有 C# 原生确认框；`confirmHook` 只是测试接缝，生产恒为 null。
4. 测试程序**不要**对真实的 `HKCU\...\Run\GpuLcdTool` 留下改动：程序写它时用的是自己当前的 exe 路径。`run-webbridge-test.ps1` 已自动保存并还原它。

## 沙箱坑（在 DSH 会话里工作时）
从工作目录启动的 exe 无法写 `%APPDATA%` / `%LOCALAPPDATA%`，也连不上命名管道。用 `deploy-run.cmd` 部署到 `%LOCALAPPDATA%\GpuLcdRun` 再运行；测试台从 `%TEMP%` 运行。
根因（2026-10-12 查明）：沙箱会给工作目录加「低完整性」标签（`icacls 目录` 能看到 `Mandatory Label\Low Mandatory Level`，带 OI/CI 继承），目录里的 exe 不管谁启动都以低完整性运行——**手动双击也一样**：托盘图标注册不上、WebView2 报 0x800700AA 退回经典界面、写不了 `%APPDATA%` 的配置和日志、`Process.HasExited` 打不开别的进程。所以日常运行也用部署到 `GpuLcdRun` 的那份，开机自启要从那份的界面里重新开关一次。

## 用 opencode 的 DeepSeek 工人写前端
`opencode run -m opencode-go/deepseek-v4.1-flash --dir ui "<提示词>"`。经验：
- 提示词里**明令禁止**启动 dev server（会卡死会话），只许 `vue-tsc` + `vite build`；并行时各自构建到 `--outDir .tmp-build-X`。
- 限定可改的文件范围，事后用时间戳 / 快照核对是否越界，并自己重跑类型检查和构建。
- 守则在 `ui\AGENTS.md`，提示词和汇报留在 `ui\docs\worker*-*.txt`。

## 未做 / 以后
- 从屏幕删除文件（需要 C# 原生确认框 + 协议）。
- 当前自用源码已同步到私有仓库 `igame-vulcan-lcd-tool`；通用化安装与路径配置仍待后续处理。
- 代码签名：安装包未签名，SmartScreen 可能提示“未知发布者”。
- `docs\optimization-todo.md` 第 3–13 条（UI 线程上的 `Thread.Sleep` 等）。

## 1.0.1 随包运行库
- `VendorRuntime.cs` 优先解析 exe 旁 `vendor/iGameCenter`，完整包无需另装 iGameCenter。
- `tools/Collect-VendorRuntime.ps1` 收集白名单库、转换器和私有 VC90 CRT；`package.ps1` 包含它们及哈希清单。
- `tools/verify-package.py` 验证两种包、加载路径及无屏幕写入的转换。
