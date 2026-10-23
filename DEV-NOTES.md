# 开发说明（GpuLcd 1.0.5）

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
| 准备安装包 + 便携版 + 插件 0.1.1 | `.\package.ps1`（强制重建 UI，检查插件 src/lib）→ `release\` |
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

## 1.0.5 发布准备（不部署）

- 固定宿主 1.0.5、插件 0.1.1；插件必须使用 GpuLcd >=1.0.5，旧 1.0.4 不保证原子 waiting 恢复。旧插件 0.1.0 有中间件截断缺陷，不再建议使用。
- 仅离线验证 DSH 0.2.1-rc.2；package/manifest 下限同为 >=0.2.1-rc.2、Node >=24，未来 DSH 仍须复验真实 waterfall/生命周期契约。
- 用已有依赖执行 `.\package.ps1`：强制重建 UI/dist 并嵌进新 exe，`node dsh-plugin/build.mjs --check` 不改冻结 lib；不安装依赖、不运行宿主/安装器、不碰真实 profile。
- 打包以白名单复制插件运行库、package、manifest、patch、README、CONTRACTS 和根 LICENSE；独立插件 zip 与两种宿主包逐文件相同。所有生成 JSON 为 UTF-8 无 BOM。旧同名产物/校验和先复制进唯一的 release/backups 子目录，不覆盖旧备份；旧版附件不变。
- 用可用的 Python 运行 `tools/verify-package.py`，核验完整清单、assembly/file 版本、真实内嵌 UI 与刚重建 dist、独立插件相等、三条 SHA256SUMS、厂商库加载和 TEMP 转换。反射读取资源，不执行宿主或安装器入口。
- 离线回归包括完整 52 插件测试、Link 250 / Completion 25 / DotMatrix 67 / Display 15 / VendorRuntime 5 断言、灯效脚本和 UI 合并回归。DotMatrixTests 仅随机端口 fake API。不得用 live 脚本替代。
- 灯效模板支持 dsh@ 状态与来源回执；UI 部分保存基线、unknown-source 拒绝、15 秒同级来源保持见本版 release notes。只验证离线逻辑，不声称实际灯效/屏幕生效。GIF 随包；SAVI 后续经用户批准受控上传。dot-motion-builder 版本与包不变。
- 本阶段不 commit/push/tag、不操作 GitHub、不修改真实安装；发布由协调者执行。

## 未做 / 以后
- 从屏幕删除文件（需要 C# 原生确认框 + 协议）。
- 当前自用源码已同步到私有仓库 `igame-vulcan-lcd-tool`；通用化安装与路径配置仍待后续处理。
- 代码签名：安装包未签名，SmartScreen 可能提示“未知发布者”。
- `docs\optimization-todo.md` 第 3–13 条（UI 线程上的 `Thread.Sleep` 等）。

## 1.0.1 随包运行库
- `VendorRuntime.cs` 优先解析 exe 旁 `vendor/iGameCenter`，完整包无需另装 iGameCenter。
- `tools/Collect-VendorRuntime.ps1` 收集白名单库、转换器和私有 VC90 CRT；`package.ps1` 包含它们及哈希清单。
- `tools/verify-package.py` 验证两种包、加载路径及无屏幕写入的转换。
