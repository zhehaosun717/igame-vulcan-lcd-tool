# 前端 ↔ C# 通信协议（bridge protocol）

前端（Vue）跑在 Edge WebView2 里。**前端永远不直接碰屏幕/串口**，只向 C# 宿主发"请求"，由 C# 决定能不能执行（上传互斥、夜间模式、维护锁都在 C# 里判断）。

## 传输
- 前端 → C#：`window.chrome.webview.postMessage(JSON字符串)`
- C# → 前端：`window.chrome.webview.addEventListener('message', e => e.data)`（e.data 是 JSON 字符串）
- 浏览器开发模式（没有 `window.chrome?.webview`）自动改用 mock 实现，同一套接口。

## 请求
```json
{ "id": 12, "cmd": "getSettings", "args": {} }
```
## 响应
```json
{ "id": 12, "ok": true, "data": { ... } }
{ "id": 12, "ok": false, "error": "上传进行中" }
```
## 主动推送事件（无 id）
```json
{ "event": "state", "data": { ...DeviceState } }
{ "event": "log", "data": { "time": "10:58:07", "text": "..." } }
{ "event": "uploadProgress", "data": { "percent": 42, "result": -1 } }
```

## 命令清单（阶段 1）
| cmd | args | data | 说明 |
|---|---|---|---|
| `getState` | — | `DeviceState` | 连接状态、固件、是否上传中、夜间模式、当前播放 |
| `getSettings` | — | `Settings` | 亮度/不透明度/方向/启用 等（来自 settings.json） |
| `setBasic` | `{ brightness?:0-10, opacity?:0-100, dir?:0-3, enabled?:boolean }` | `{}` | 写屏幕。C# 会拒绝：未连接/上传中/熄灯/维护 |
| `listFiles` | — | `ScreenFile[]` | 屏幕上的文件名+字节数 |
| `playFile` | `{ name:string }` | `{}` | 设为当前全屏动画 |
| `getLog` | `{ limit?:number }` | `LogLine[]` | 最近日志 |

## 命令清单（阶段 2：上传）
上传的安全设计：**前端永远拿不到文件内容，也不能指定任意路径**。文件由 C# 弹出系统文件选择框，C# 记下路径并只返回一个一次性 `token`；`startUpload` 只认这个 token。真正的二次确认框也由 C# 弹出（原生 MessageBox），前端无法绕过。

| cmd | args | data | 说明 |
|---|---|---|---|
| `pickUploadFile` | — | `{ cancelled:true }` 或 `{ cancelled:false, token, fileName, sizeBytes, suggestedName }` | C# 弹文件选择框。`suggestedName` 是按屏幕文件名规则（1-8 位字母数字 + `.savi`）从文件名推导的建议名 |
| `getSpace` | — | `{ known:boolean, free:number, total:number }` | 屏幕剩余/总空间（字节，来自上次刷新的缓存，不读串口）。`known=false` 表示未读到 |
| `startUpload` | `{ token, name, fit:'contain'\|'cover'\|'stretch', mode:'movie'\|'background' }` | `{ started:true }` 或 `{ started:false, reason:'cancelled' }` | C# 校验后弹原生确认框；用户确认后才开始。开始后立即返回，进度走事件 |

`startUpload` 被 C# 以 `ok:false` 拒绝的条件：token 无效/已用过、`name` 不合法、未连接、已在上传、熄灯中、维护模式、挂件占用屏幕、**目标文件名等于当前默认动画或正在播放的文件**（覆盖正在使用的文件会写坏屏幕）。
上传**不可取消**（中途打断会把屏幕写坏）；上传期间 C# 拒绝一切其它写命令，且窗口无法退出。

新增事件：
```json
{ "event": "uploadProgress", "data": { "percent": 42, "result": -1 } }
{ "event": "uploadDone", "data": { "ok": true, "name": "img4.savi", "message": "上传完成，已设为当前全屏动画" } }
```
`uploadProgress` 在上传期间约每 250 毫秒推送一次；`result` 为 -1 表示进行中，0 表示完成，>0 为厂商库错误码。

## 命令清单（阶段 3：其余页面）
通用规则：所有写屏幕的命令，C# 在 熄灯 / 上传中 / 维护 / 挂件占用 / 未连接 时一律拒绝（`ok:false` + 中文原因），参数由 C# 校验。读命令只读内存缓存。**页面里把这些原因直接展示给用户，并在对应状态下禁用控件。**

| cmd | args | data | 说明 |
|---|---|---|---|
| `getModes` | — | `ModesInfo` | 轮播页面配置。`available` 是页面名称列表，`selected` 是已勾选的下标（对应 available） |
| `setModes` | `{ selected:number[], interval:1-120, auto:boolean }` | `{}` | 至少选一项；勾选下标 0（"自定义动画/图片"）要求屏幕上已有当前动画文件，否则被拒 |
| `getText` | — | `TextInfo` | 自定义文字配置 |
| `setText` | `{ text:string(≤64), fontSize:0-9, color:'RRGGBB', show:boolean }` | `{}` | 文字限 ASCII 可打印字符（厂商库限制，中文会被拒） |
| `getLive` | — | `LiveInfo` | 实时数据开关、最近一次推送文字、开机自启状态 |
| `setLive` | `{ on:boolean }` | `{}` | 开关"实时刷新"（CPU/内存推送，实验性） |
| `setAutostart` | `{ on:boolean }` | `{}` | 开机自启（写注册表，不碰屏幕，任何状态可用） |
| `getLinks` | — | `LinksInfo` | 联动配置与当前状态（不碰屏幕，任何状态可读） |
| `setLinks` | `{ codex?, claude?, signal?:boolean, idleOffsetMs?:-5000..5000, idleRatePercent?:95..105 }` | `{}` | 只写配置文件和联动状态，不直接写串口；联动定时器随后按各自的守卫处理 |
| `linkDemo` | `{ state:'working'\|'waiting'\|'done'\|'idle' }` | `{}` | 测试联动（8 秒后自动恢复），会触发屏幕播放，受写守卫限制 |
| `idleSync` | — | `{}` | "重播默认动画并对齐"，受写守卫限制 |
| `sendWidget` | `WidgetRequest` | `{ message:string }` | 进度/通知，C# 复用原有 widget 通道（参数由其校验） |
| `getWidgetStatus` | — | `WidgetStatus` | 当前挂件占用情况 |
| `setNight` | `{ on:boolean }` | `{ message:string }` | 一键熄灯/恢复。熄灯中的写守卫不影响本命令，但上传中会被拒 |
| `readOpening` | — | `{ mode:number }` | 读取开机方案（会读串口，约 1-2 秒），受写守卫限制 |
| `previewOpening` | — | `{ started:boolean }` | 在屏幕播放原开机动画。C# 弹原生确认框 |
| `openStorageManager` | — | `{ started:boolean }` | 进入存储维护（程序会退出并以维护模式重开）。C# 弹原生确认框，上传中被拒 |

| `getPreview` | `{ name }` | `PreviewInfo` | 动画预览（只读本机的转换副本，不碰串口）。`available=false` 时 `reason` 说明原因（无本机副本 / 副本与屏幕上的不一致）。`frames` 是按播放顺序的画面（`src` 为 JPEG data URL，`n` 为持续帧数，按 `fps` 换算时长）；画面在文件中倒置，显示时需旋转 180° |

类型见 `src/bridge/types.ts`。

阶段 1-3 **不包含**"从屏幕删除文件"，以后再加，同样由 C# 做二次确认。

## 类型（TypeScript）
```ts
export interface DeviceState {
  connected: boolean
  gpu: string          // "GeForce RTX 3070 Ti"
  com: string          // "COM4"
  firmware: number     // 122
  uploading: boolean
  nightMode: boolean
  maintenance: boolean
  playing: string      // 当前播放的文件名，可能为空
  linkState: 'idle' | 'working' | 'waiting' | 'done'
}
export interface Settings {
  brightness: number   // 0-10
  opacity: number      // 0-100
  dir: number          // 0-3
  enabled: boolean
  current: string      // 默认动画文件名
}
export interface ScreenFile { name: string; size: number }
export interface LogLine { time: string; text: string }
```
