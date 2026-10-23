# GpuLcd 1.0.5 + DSH 插件 0.1.1

本版通过离线验收；未在真实 DSH profile 安装或启用插件，未验证物理屏幕/灯光输出。旧版本附件保持不变，旧包不包含本次修复。

## 兼容与升级要求

- **DSH 插件 0.1.1 必须配套 GpuLcd >=1.0.5。** 旧 GpuLcd 1.0.4 忽略启动事件的 state 字段，不保证等待恢复无 working 中间态。
- **旧插件 0.1.0 不再建议使用**：tools/pre-execute 中间件不执行 next 并返回 undefined，会截断 DSH 工具链，并使 gate.kind 读取抛出 TypeError。
- 仅实际离线验证 **DSH 0.2.1-rc.2 / Cordis 4.0.5**。package 与 manifest 的 DSH 下限统一收紧为 **>=0.2.1-rc.2**，不再无依据宣称 >=0.1.7 普遍支持。该下限不是未来版本的兼容认证；未来版本仍须复验真实契约。Node.js 运行/开发下限保持 **>=24**。
- 不提供自动修改真实 DSH profile 的脚本。安装必须在另行授权后，先用可丢弃独立 profile 验证，再进入真实维护窗口。

## 修复

### DSH 生命周期与工具链

- 工具、审批、提问中间件继续调用并原样返回 next，保留 allow/deny/ask/cancel、Promise、同步异常与异步拒绝，不强行放行。
- “完成”只来自观察过有效 step/start 的成功 turn/end；idle、空唤醒、失败、取消、阻止与 disposed 不冒充成功。并发审批/提问按独立等待 token 管理，全部解除才回工作；旧 finally 不复活已关闭或替换的轮次。

### ACK、FIFO、恢复和排队过期

- 单连接在途 FIFO；默认最多 64 个待发项，满时丢最旧待发项，默认排队上限 5 秒。连接与写入/完整 OK 换行确认各有 400 ms 有界期限。
- ACK 只证明接收端解析并排入 UI 队列，不证明 UI 已应用、灯效已显示或硬件已响应。
- 未确认的启动在 15 秒心跳节奏下有界重试，每轮最多一个启动在途，无额外无限重试循环；确认后不周期强制重播启动。清空/重启接收端后，普通当前状态可重建空条目。
- 所有排队的活跃状态在真正写入时检查轮次身份并读取当前等待集合，避免延迟连接将 waiting 降回 working 或将已回答等待重新写成 waiting；过期代际不写入。Stop/SessionEnd 仍保留原轮次、按 FIFO 终结。
- 新接收器支持 DSH UserPromptSubmit + state=waiting 原子恢复，不先发 working、临时 idle 或 SessionEnd reset，不重置既有来源保持时间。永久断线、持续过载、退出或终结丢失仍可能丢事件；不重放历史 Stop，不是持久消息系统。

### 输出、灯效与界面

- SignalRGB 模板接受原文与 URL 编码的 dsh@working / dsh@waiting / dsh@done，使用深蓝 #4D6BFE、橙金 #FF9F45、青绿 #2ED3B7；同状态换来源也刷新，完成心跳不重启动画，熄灯优先。
- 灯效回执兼容旧 GpuLcdState=状态;seq=序号，DSH/Claude 追加 client 来源；宿主按来源去重并显示确认。不带来源的旧回执仍代表 Codex。
- 联动 UI 部分设置成功保存后同步本地基线，保留 false、0 与未指定字段，避免后续保存带回过期设置。
- 事件入口拒绝未知来源、大小写变体、显式空值；省略 client 的旧事件仍默认 Codex。关闭来源清除旧状态。等待 > 工作 > 完成优先级及同级来源至少 15 秒保持不变。
- Dot Matrix 协议兼容已离线验证；本次不修改 dot-motion-builder 版本，也不另行打包它。

## 产物与资源

- GpuLcd-Setup-1.0.5.exe：每用户安装器。
- GpuLcd-1.0.5-portable.zip：完整便携目录。
- dsh-gpulcd-status-0.1.1.zip：独立插件，顶层 dsh-gpulcd-status，含 lib、package、manifest、bundle patch、README、CONTRACTS 和 LICENSE；与两种宿主包中的插件逐文件一致。
- SHA256SUMS.txt：仅覆盖上述三个最终文件。

安装/便携包携带新编译宿主、实际重建并嵌入的 UI、新灯效模板，以及 anims 下的 dswork.gif、dsask.gif、dsdone.gif。SAVI 需用户后续授权、受控转换/上传；本轮只在 TEMP 做独立转换验证，不向 LCD 上传。

## 离线校验与限制

实际离线验收结果：插件 build --check 通过；完整 **52/52** 插件测试通过；C# **Link 250、Completion 25、DotMatrix 67、Display 15、VendorRuntime 5** 断言全部通过；灯效 JS、UI 合并回归及 Vue 类型检查/生产构建通过。三件产物通过逐文件清单、内嵌 UI/版本、安装器与便携包相等、插件相等、库加载与 TEMP 转换校验。以下各项仅代表离线验证。

- 完整插件测试覆盖真实已安装包的只读 waterfall 契约、假命名管道、ACK/FIFO、TTL/溢出恢复与原始 C# 接收器逐事件投影；build --check 比较冻结 src/lib。
- C# 纯测试台覆盖 LinkState、Completion、DotMatrix（仅随机端口 fake API）、DisplayJobs、VendorRuntime；JS 灯效渲染和 UI 合并回归不连接真实输出。
- package.ps1 使用已有依赖强制重建 UI 和宿主，不运行它们；保持纯 ASCII，生成 JSON 为 UTF-8 无 BOM。重复打包先备份同名产物到新的唯一目录，不覆盖旧备份。
- tools/verify-package.py 检查完整逐文件 manifest、assembly/file/display 版本、真实内嵌 UI 与新 dist、安装器 payload=便携包、三份插件相等、三条 SHA256SUMS、厂商库加载和独立 GIF→SAVI 转换，不执行宿主或安装器入口。
- 验证未修改真实 profile/已安装插件，未连接真实事件管道、灯效服务、串口、蓝牙或硬件，也未执行安装。真实部署和设备输出仍需另行受控验收。

### English summary

GpuLcd 1.0.5 and dsh-gpulcd-status 0.1.1 fix middleware transparency, completion classification, bounded ACK/FIFO recovery, current-at-delivery state snapshots, source-aware lighting receipts and partial UI settings. Plugin 0.1.1 requires GpuLcd >=1.0.5 and Node >=24; only DSH 0.2.1-rc.2 has been validated offline. No live activation or physical rendering is claimed.
