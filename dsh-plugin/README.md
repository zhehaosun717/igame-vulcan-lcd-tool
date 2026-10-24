# dsh-gpulcd-status

将 DeepSeek Harness 的**工作 / 等待用户 / 成功完成一轮**状态作为旁路遥测发送给 GpuLcd。只发送事件类型、会话 ID、随机轮次 ID、固定的 client=dsh，以及开始事件的 working/waiting 状态枚举，不发送对话、工具参数或答案。配色和状态优先级仍由 GpuLcd 决定。

## 本次修复状态与安全边界

**本目录为 0.1.2 发布产物。** 必须配套 **GpuLcd >=1.0.5**。旧版本都不要再装：0.1.0 的 tools/pre-execute 不调用 next、返回 undefined，会使 DSH 工具链读取 gate.kind 时抛 TypeError；0.1.1 的 inject 声明用了当前 Cordis 不支持的对象写法，挂载时永远等不到服务而不激活。0.1.2 已在 DSH 0.2.1-rc.2 的隔离 headless profile 实际挂载并验证事件收发。

仅实际离线验证 **DSH 0.2.1-rc.2**（Cordis 4.0.5）。package 与 manifest 的 DSH 下限已由 `>=0.1.7-0` 收紧为 `>=0.2.1-rc.2`；该范围只是最低版本门槛，不是对所有未来版本的兼容认证。未来 DSH 版本仍须重新运行真实契约测试。运行和开发要求 Node.js **>=24**。

本次仅做源码、只读安装包契约审查、独立测试进程与随机名称假管道验证。没有安装/重载插件、修改真实 profile 或已安装插件、重启 DSH/GpuLcd、连接真实 GpuLcdEvents 管道、发送真实事件或触碰硬件。

此前将崩溃归因为「pnpm / dump-config 必然导致 DSH 崩溃」的说法没有得到验证，应撤回；已复现且修复的原因是插件中间件违反 waterfall 返回契约。依赖安装/配置命令另有环境变更风险，不能与这项确定的代码缺陷混为一谈。

## 事件映射

| DSH 真实契约 | 遥测事件 | 说明 |
| --- | --- | --- |
| agent/status running、已 running 的 agents.list() | UserPromptSubmit | 新的随机轮次；子代理独立会话 |
| tools/pre-execute | PreToolUse 或 PermissionRequest | 等待尚未全部解除时绝不覆盖为工作 |
| approval/request、user-questions/request | PermissionRequest | 并发等待独立 token；最后一个解除才回工作 |
| session/event 的 turn/end，reason.kind=completed，且观察过该轮 step/start | Stop | 仅真实成功完成；空唤醒不冒充完成 |
| agent/status idle，但没有成功结束证据 | SessionEnd | 清理，不冒充完成 |
| turn/end 的错误/取消/阻止、agent/disposed({agent}) | SessionEnd | 清理；旧 finally 无法复活已关闭轮次 |

工具中间件只 return next()，从不强制 allow。审批与提问继续执行原链，保留返回对象和错误，不吞下游异常。遥测同步抛错、异步拒绝、接收端掉线均不改变工具/审批/提问结果。

## 通信语义与限制

- 单连接在途、FIFO 待发队列，默认最多 64 个待发项；满时丢最旧待发项，保留项不重排。默认队列年龄上限 5 秒，无无限重试。
- 连接超时 400 ms；连上后的写入加 OK 确认共用 400 ms 上限。只有收到完整 OK 换行才记成功；拒绝、断开、超时均失败。工具链不等待这些 I/O。
- 接收端目前在解析并排入 UI 线程后回 OK。**OK 证明协议接收/排队，不证明设置已启用、屏幕已渲染或硬件已响应。**
- 每个活跃轮次记录开始事件的传输 ACK。开始发送失败、排队过期或被挤掉时，每 15 秒心跳最多补发一个开始事件，每轮至多一个在途；不启动额外重试循环、不积压副本，轮次结束立即停止。收到 OK 后不再周期重放开始事件，只刷新当前状态；接收端重启或开关 ClearClient 后，普通状态刷新可重建空条目。
- 开始事件扩展为 UserPromptSubmit + state=working/waiting。所有活跃状态（开始、工作、等待和心跳）都在连接完成、真正序列化写入时才读取各自会话的当前等待状态；不是仅开始事件如此。已结束/销毁/替换的旧轮次供应器不写入。生命周期 Stop/SessionEnd 保留原轮次，仍按 FIFO 终结。恢复等待时使用原子 waiting start，不先发 working、SessionEnd reset 或制造临时 idle，也不重置已有客户端保持时间。
- **原子 waiting 恢复要求配套 GpuLcd >=1.0.5。旧 1.0.4 会忽略 state 字段，不能保证等待恢复无 working 中间态；0.1.2 不支持以旧 1.0.4 接收器代替。** 新接收器仅对 DSH UserPromptSubmit 解释 state：空/working 为工作，waiting 为等待，非法非空值拒绝；其他来源语义不变。
- ACK 仍不等于 Apply：若接收端回 OK 却异常丢弃事件、同时又保留旧轮次门控，发送方无法检测；本插件不会以周期强制 start 或清空状态掩盖该故障。持续断线、永久过载、进程退出或结束事件丢失也不提供必达保证；不重放历史 Stop，仍是有界尽力遥测，不是持久消息系统。
- 轮次使用每次挂载的随机 UUID 加计数，重载不会复用旧 ID。只读 current() 不会隐式创建新轮次。
- 插件挂载前已经存在的用户等待无法从 agents.list() 恢复；只能识别挂载后观察到的等待请求。完整启用验收仍待未来另行授权。

## 安装指导（本轮不执行）

1. 使用配套 1.0.6 的独立 `dsh-gpulcd-status-0.1.2.zip`，或安装/便携包内相同的 `dsh-plugin` 目录；独立压缩包顶层为 `dsh-gpulcd-status`，已含 lib、manifest、bundle patch、README、CONTRACTS 与 LICENSE，无需运行编译或安装构建依赖。先独立审查并完成下述离线构建检查/测试，不要把旧 lib 或旧 0.1.0 包复制到正在工作的 profile。
2. 如需安装验收，另行获得用户明确授权，先在**可丢弃的独立 profile** 中验证当前 DSH 版本、bundle 注册、卸载清理和假接收端。不要直接试验正在工作的 desktop profile。
3. 仅在隔离验收通过、用户批准维护窗口后，再通过该 DSH 版本的受支持插件安装流程部署完整产物；本说明不提供自动改写真实 profile 或破坏性恢复脚本。
4. 真正连接 GpuLcd/硬件是单独阶段。需要支持 dsh 来源的 GpuLcd，并确认联动设置启用；本次尚未验证该 live 集成。

## 开发与可重复构建

本包当前声明支持的**运行和开发**下限均为 Node.js 24（不是仅构建约束）：已核实桌面宿主 Node 24.21.0、系统验证 Node 24.19.0。构建无需安装任何依赖，不依赖用户 profile 编译器；完整恢复测试还需 Windows .NET Framework C# 编译器，仅在 TEMP 编译纯状态机、不启动应用。下面从本目录执行：

~~~bash
node build.mjs
node build.mjs --check
node --test test/index.test.mjs test/bridge.test.mjs test/contract.test.mjs test/recovery.test.mjs test/recovery-pipe.test.mjs test/queued-state.test.mjs
node test/typecheck.mjs
~~~

- build.mjs 用 Node 原生 stripTypeScriptTypes 生成随包 lib；这是语法擦除，不冒充类型检查。--check 检查源产物逐字一致；构建 API 目前有 ExperimentalWarning。
- typecheck.mjs 只在新建系统临时目录安装固定版本 TypeScript 和 Node 类型，缓存也在该目录；严格 noEmit 校验后删除该临时目录。不会写仓库/真实 profile 的 node_modules，需 npm 和网络。
- 普通测试只用内存遥测接收器和 gpulcd-isolated-* 随机假管道。契约测试只读安装包，直接执行其中真实 Cordis waterfall 方法，并检查 dsh-tools 实际 gate.kind 调用契约。安装包路径可由 DSH_CONTRACT_ASAR 环境变量指定；默认指向本次审查的 Windows 安装位置。目标包契约若变化应失败，而不是静默跳过。
- 已删除宽松 ambient shims；显式本地事件投影、严格类型检查和真实安装包契约回归各自负责不同层次，不声称已做完整 DSH 类型包联编。

详细契约来源与先失败后通过证据见 [CONTRACTS.md](./CONTRACTS.md)。lib 是发布入口，必须与 src 同步；0.1.2 复用已验收、未改动的冻结生产源码与 lib，当前发布包仅完成离线验证，未在真实 profile 安装或启用。安装/便携包附带三张 ds*.gif；SAVI 需用户后续另行授权、受控上传，本轮不执行。

## 配色（未变更）

| 状态 | DSH | Claude Code | Codex |
| --- | --- | --- | --- |
| 工作中 | #4D6BFE 深蓝 | #D97757 陶橙 | #3C82FF 蓝 |
| 等待用户 | #FF9F45 金黄 | #F0B43C 琥珀 | #B478FF 紫 |
| 完成 | #2ED3B7 青绿 | #78C878 草绿 | #3CDCDC 青 |
