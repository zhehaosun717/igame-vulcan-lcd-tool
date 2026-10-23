// 本文件严格对应 docs/bridge-protocol.md，不得擅自增删命令或字段。

export interface DeviceState {
  connected: boolean
  gpu: string // "GeForce RTX 3070 Ti"
  com: string // "COM4"
  firmware: number // 122
  uploading: boolean
  nightMode: boolean
  maintenance: boolean
  playing: string // 当前播放的文件名，可能为空
  linkState: 'idle' | 'working' | 'waiting' | 'done'
}

export interface Settings {
  brightness: number // 0-10
  opacity: number // 0-100
  dir: number // 0-3
  enabled: boolean
  current: string // 默认动画文件名
}

export interface ScreenFile {
  name: string
  size: number
}

export interface LogLine {
  time: string
  text: string
}

// ---- 命令 args / data ----

export interface SetBasicArgs {
  brightness?: number
  opacity?: number
  dir?: number
  enabled?: boolean
}

export interface PlayFileArgs {
  name: string
}

export interface GetLogArgs {
  limit?: number
}

export type UploadFit = 'contain' | 'cover' | 'stretch'
export type UploadMode = 'movie' | 'background'

export type PickUploadFileData =
  | { cancelled: true }
  | {
      cancelled: false
      token: string
      fileName: string
      sizeBytes: number
      suggestedName: string
    }

export interface SpaceInfo {
  known: boolean
  free: number
  total: number
}

export interface StartUploadArgs {
  token: string
  name: string
  fit: UploadFit
  mode: UploadMode
}

export type StartUploadData = { started: true } | { started: false; reason: 'cancelled' }

export interface UploadDone {
  ok: boolean
  name: string
  message: string
}

// ---- 阶段 3 ----

export interface ModesInfo {
  available: string[] // 页面名称，下标即 id
  selected: number[] // 已勾选的下标
  interval: number // 每页停留秒数 1-120
  auto: boolean
  canUseMovie: boolean // 屏幕上是否有可用的当前动画（决定能否勾选下标 0）
}
export interface SetModesArgs {
  selected: number[]
  interval: number
  auto: boolean
}

export interface TextInfo {
  text: string
  fontSize: number // 0-9
  color: string // 'RRGGBB'
  show: boolean
}
export type SetTextArgs = TextInfo

export interface LiveInfo {
  on: boolean
  text: string // 最近一次推送的描述，如 "CPU 12%  3200 MHz ..."
  autostart: boolean
}

export interface LinkSession {
  session: string
  turn: string
  state: string
  closed: boolean
}
export interface LinksInfo {
  codex: boolean
  claude: boolean
  dsh: boolean
  signal: boolean
  dotMatrix: boolean
  idleOffsetMs: number
  idleRatePercent: number
  linkState: DeviceState['linkState']
  receivedHookEvents: number
  lastHookEvent: string
  sessions: LinkSession[]
  statusText: string // 联动状态文字
  signalText: string // SignalRGB 状态文字
  dotMatrixText: string // Dot Matrix Studio 点阵屏状态文字
}
export interface SetLinksArgs {
  codex?: boolean
  claude?: boolean
  dsh?: boolean
  signal?: boolean
  dotMatrix?: boolean
  idleOffsetMs?: number
  idleRatePercent?: number
}
export type LinkDemoState = 'working' | 'waiting' | 'done' | 'idle'

export interface WidgetRequest {
  action: 'progress' | 'notify' | 'clear' | 'clear-progress' | 'clear-notice'
  percent?: number // progress: 0-100
  title?: string // progress 标题
  seconds?: number // notify 持续秒数
  text?: string // notify 文字
}
export interface WidgetStatus {
  active: boolean
  kind: string | null
  sent: string
  error: string
}

export interface PreviewFrame {
  src: string // JPEG data URL（倒置存放，显示时旋转 180°）
  n: number // 持续帧数
}
export interface PreviewInfo {
  available: boolean
  reason: string
  source?: string // 画面来源："本机上传副本" / "从屏幕存储读出的副本"
  fps?: number
  frameCount?: number
  durationMs?: number
  frames?: PreviewFrame[]
}

export interface MessageData {
  message: string
}

export type EmptyArgs = Record<string, never>
export type EmptyData = Record<string, never>

/** 命令名 → { args, data } 映射表 */
export interface CmdMap {
  getState: { args: EmptyArgs; data: DeviceState }
  getSettings: { args: EmptyArgs; data: Settings }
  setBasic: { args: SetBasicArgs; data: EmptyData }
  listFiles: { args: EmptyArgs; data: ScreenFile[] }
  playFile: { args: PlayFileArgs; data: EmptyData }
  getLog: { args: GetLogArgs; data: LogLine[] }
  pickUploadFile: { args: EmptyArgs; data: PickUploadFileData }
  getSpace: { args: EmptyArgs; data: SpaceInfo }
  startUpload: { args: StartUploadArgs; data: StartUploadData }
  getPreview: { args: { name: string }; data: PreviewInfo }
  getModes: { args: EmptyArgs; data: ModesInfo }
  setModes: { args: SetModesArgs; data: EmptyData }
  getText: { args: EmptyArgs; data: TextInfo }
  setText: { args: SetTextArgs; data: EmptyData }
  getLive: { args: EmptyArgs; data: LiveInfo }
  setLive: { args: { on: boolean }; data: EmptyData }
  setAutostart: { args: { on: boolean }; data: EmptyData }
  getLinks: { args: EmptyArgs; data: LinksInfo }
  setLinks: { args: SetLinksArgs; data: EmptyData }
  linkDemo: { args: { state: LinkDemoState }; data: EmptyData }
  idleSync: { args: EmptyArgs; data: EmptyData }
  sendWidget: { args: WidgetRequest; data: MessageData }
  getWidgetStatus: { args: EmptyArgs; data: WidgetStatus }
  setNight: { args: { on: boolean }; data: MessageData }
  readOpening: { args: EmptyArgs; data: { mode: number } }
  previewOpening: { args: EmptyArgs; data: { started: boolean } }
  openStorageManager: { args: EmptyArgs; data: { started: boolean } }
}

export type CmdName = keyof CmdMap
export type CmdArgs<K extends CmdName> = CmdMap[K]['args']
export type CmdData<K extends CmdName> = CmdMap[K]['data']

// ---- 主动推送事件 ----

export interface UploadProgress {
  percent: number
  result: number
}

export interface EventMap {
  state: DeviceState
  log: LogLine
  uploadProgress: UploadProgress
  uploadDone: UploadDone
}

export type EventName = keyof EventMap
export type EventData<E extends EventName> = EventMap[E]

// ---- 线格式（请求 / 响应 / 事件） ----

export interface BridgeRequest<K extends CmdName = CmdName> {
  id: number
  cmd: K
  args: CmdArgs<K>
}

export interface BridgeResponse {
  id: number
  ok: boolean
  data?: unknown
  error?: string
}

export interface BridgeEvent<E extends EventName = EventName> {
  event: E
  data: EventData<E>
}
