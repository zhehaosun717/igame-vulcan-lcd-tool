// 浏览器开发模式下的假实现：不连硬件也能开发/截图。
// 真实 C# 宿主存在时（window.chrome.webview）本文件不会被调用。
import type {
  CmdArgs,
  CmdData,
  CmdName,
  DeviceState,
  EventData,
  EventName,
  GetLogArgs,
  LogLine,
  PlayFileArgs,
  ScreenFile,
  SetBasicArgs,
  Settings,
  SetLinksArgs,
  SetModesArgs,
  SetTextArgs,
  StartUploadArgs,
  LinkDemoState,
  PreviewInfo,
  WidgetRequest,
  WidgetStatus
} from './types'

export const isMockMode: boolean =
  typeof window === 'undefined' || !window.chrome?.webview

const COM_PORT = 'COM4'

function now(): string {
  return new Date().toLocaleTimeString('zh-CN', { hour12: false })
}

// ---- 可变状态 ----

const state: DeviceState = {
  connected: true,
  gpu: 'GeForce RTX 3070 Ti',
  com: COM_PORT,
  firmware: 122,
  uploading: false,
  nightMode: false,
  maintenance: false,
  playing: 'cyberpunk.ani',
  linkState: 'idle'
}

const settings: Settings = {
  brightness: 6,
  opacity: 100,
  dir: 0,
  enabled: true,
  current: 'cyberpunk.ani'
}

const mockFiles: ScreenFile[] = [
  { name: 'cyberpunk.ani', size: 2_400_000 },
  { name: 'matrix.ani', size: 1_850_000 },
  { name: 'waves.ani', size: 980_000 },
  { name: 'boot.gif', size: 512_000 },
  { name: 'logo.png', size: 64_000 }
]

const mockLog: LogLine[] = [
  { time: now(), text: '已连接 ' + COM_PORT },
  { time: now(), text: '固件版本 122' },
  { time: now(), text: '当前播放 cyberpunk.ani' }
]

// ---- 事件总线 ----

type Handler = (data: unknown) => void

const handlers = new Map<EventName, Set<Handler>>()

function emit(event: EventName, data: unknown): void {
  handlers.get(event)?.forEach((h) => h(data))
}

export function mockOn<E extends EventName>(
  event: E,
  handler: (data: EventData<E>) => void
): () => void {
  let set = handlers.get(event)
  if (!set) {
    set = new Set()
    handlers.set(event, set)
  }
  const h = handler as Handler
  set.add(h)
  return () => {
    set.delete(h)
  }
}

function pushLog(text: string): void {
  const line: LogLine = { time: now(), text }
  mockLog.unshift(line)
  if (mockLog.length > 300) mockLog.pop()
  emit('log', line)
}

function broadcastState(): void {
  emit('state', { ...state })
}

// ---- 上传进度模拟 ----

let uploadTimer: ReturnType<typeof setInterval> | null = null
let uploadPercent = 0

function startUploadTicker(): void {
  if (uploadTimer !== null) return
  uploadPercent = 0
  uploadTimer = setInterval(() => {
    uploadPercent = (uploadPercent + 7) % 101
    emit('uploadProgress', { percent: uploadPercent, result: -1 })
  }, 400)
}

function stopUploadTicker(): void {
  if (uploadTimer !== null) {
    clearInterval(uploadTimer)
    uploadTimer = null
  }
}

// ---- mock 控制（仅 mock 模式可用） ----

export interface MockSnapshot {
  connected: boolean
  uploading: boolean
  nightMode: boolean
  linkState: DeviceState['linkState']
}

export const mockControl = {
  getSnapshot(): MockSnapshot {
    return {
      connected: state.connected,
      uploading: state.uploading,
      nightMode: state.nightMode,
      linkState: state.linkState
    }
  },

  setConnected(value: boolean): void {
    if (state.connected === value) return
    state.connected = value
    if (value) {
      state.com = COM_PORT
      pushLog('屏幕已连接')
    } else {
      state.com = '—'
      state.playing = ''
      state.uploading = false
      stopUploadTicker()
      pushLog('屏幕未连接')
    }
    broadcastState()
  },

  connect(): void {
    this.setConnected(true)
  },

  disconnect(): void {
    this.setConnected(false)
  },

  setUploading(value: boolean): void {
    if (value && !state.connected) {
      pushLog('未连接，无法上传')
      return
    }
    if (state.uploading === value) return
    state.uploading = value
    if (value) {
      startUploadTicker()
      pushLog('开始上传')
    } else {
      stopUploadTicker()
      pushLog('上传结束')
    }
    broadcastState()
  },

  toggleUploading(): void {
    this.setUploading(!state.uploading)
  },

  setNightMode(value: boolean): void {
    if (state.nightMode === value) return
    state.nightMode = value
    pushLog(value ? '进入夜间模式（熄灯）' : '退出夜间模式')
    broadcastState()
  },

  setLinkState(linkState: DeviceState['linkState']): void {
    state.linkState = linkState
    broadcastState()
  }
}

// ---- 命令实现 ----

type MockOk = { ok: true; data: unknown }
type MockErr = { ok: false; error: string }
type MockResult = MockOk | MockErr

function delay<T>(value: T): Promise<T> {
  const ms = 300 + Math.floor(Math.random() * 301) // 300~600ms
  return new Promise((resolve) => setTimeout(() => resolve(value), ms))
}

function run(cmd: CmdName, args: unknown): MockResult {
  switch (cmd) {
    case 'getState':
      return { ok: true, data: { ...state } }

    case 'getSettings':
      return { ok: true, data: { ...settings } }

    case 'listFiles':
      return { ok: true, data: mockFiles.map((f) => ({ ...f })) }

    case 'getLog': {
      const a = (args ?? {}) as GetLogArgs
      const limit = a.limit ?? 200
      return { ok: true, data: mockLog.slice(0, limit).map((l) => ({ ...l })) }
    }

    case 'setBasic': {
      // 与真实 C# 行为一致：上传中 / 未连接时拒绝
      if (state.uploading) return { ok: false, error: '上传进行中' }
      if (!state.connected) return { ok: false, error: '屏幕未连接' }

      const a = (args ?? {}) as SetBasicArgs
      if (a.brightness !== undefined) settings.brightness = a.brightness
      if (a.opacity !== undefined) settings.opacity = a.opacity
      if (a.dir !== undefined) settings.dir = a.dir
      if (a.enabled !== undefined) settings.enabled = a.enabled

      pushLog('已写入屏幕设置')
      broadcastState()
      return { ok: true, data: {} }
    }

    case 'playFile': {
      if (state.uploading) return { ok: false, error: '上传进行中' }
      if (!state.connected) return { ok: false, error: '屏幕未连接' }

      const a = (args ?? {}) as PlayFileArgs
      if (!mockFiles.some((f) => f.name === a.name)) {
        return { ok: false, error: '文件不存在' }
      }
      state.playing = a.name
      settings.current = a.name
      pushLog('播放 ' + a.name)
      broadcastState()
      return { ok: true, data: {} }
    }

    case 'pickUploadFile': {
      // mock：模拟用户总是选了一个 3.2 MB 的 GIF
      mockToken = 'mock-token-' + Date.now()
      return {
        ok: true,
        data: {
          cancelled: false,
          token: mockToken,
          fileName: 'my-animation.gif',
          sizeBytes: 3_200_000,
          suggestedName: 'myanim.savi'
        }
      }
    }

    case 'getSpace':
      return { ok: true, data: { known: true, free: 38_000_000, total: 64_000_000 } }

    case 'startUpload': {
      const a = (args ?? {}) as StartUploadArgs
      if (!state.connected) return { ok: false, error: '屏幕未连接' }
      if (state.uploading) return { ok: false, error: '上传进行中' }
      if (state.nightMode) return { ok: false, error: '请先恢复灯光和屏幕' }
      if (!a.token || a.token !== mockToken) return { ok: false, error: '文件令牌无效，请重新选择文件' }
      if (!/^[A-Za-z0-9]{1,8}\.savi$/.test(a.name ?? '')) {
        return { ok: false, error: '屏幕上的文件名只能是 1-8 位字母或数字，加 .savi，例如 img1.savi' }
      }
      if (a.name === settings.current || a.name === state.playing) {
        return { ok: false, error: '不能覆盖当前正在使用的文件，请先把默认动画切换到别的文件' }
      }
      mockToken = ''
      runMockUpload(a)
      return { ok: true, data: { started: true } }
    }

    case 'getPreview': {
      const name = (args as { name: string }).name
      if (name === 'logo.png') return { ok: true, data: { available: false, reason: '本机没有这个文件的副本（它不是从本机上传的）' } }
      return { ok: true, data: mockPreview(name) }
    }

    // ---- 阶段 3 ----
    case 'getModes':
      return {
        ok: true,
        data: {
          available: MODE_NAMES,
          selected: [...modes.selected],
          interval: modes.interval,
          auto: modes.auto,
          canUseMovie: mockFiles.some((f) => f.name === settings.current)
        }
      }
    case 'setModes': {
      const b = writeBlock()
      if (b) return { ok: false, error: b }
      const a = args as SetModesArgs
      if (!Array.isArray(a.selected) || a.selected.length === 0) return { ok: false, error: '至少勾选一个页面' }
      if (a.selected.includes(0) && !mockFiles.some((f) => f.name === settings.current)) {
        return { ok: false, error: '屏幕上还没有可播放的动画，请先在“图片 / 动画”页上传并设为当前' }
      }
      if (!(a.interval >= 1 && a.interval <= 120)) return { ok: false, error: '停留时间必须是 1-120 秒' }
      modes.selected = [...a.selected]; modes.interval = a.interval; modes.auto = a.auto
      pushLog('已应用轮播')
      return { ok: true, data: {} }
    }
    case 'getText':
      return { ok: true, data: { ...textInfo } }
    case 'setText': {
      const b = writeBlock()
      if (b) return { ok: false, error: b }
      const a = args as SetTextArgs
      if (a.text.length > 64) return { ok: false, error: '文字最多 64 个字符' }
      if (!/^[\x20-\x7e]*$/.test(a.text)) return { ok: false, error: '文字只支持英文、数字和 ASCII 符号' }
      if (!(a.fontSize >= 0 && a.fontSize <= 9)) return { ok: false, error: '字号必须是 0-9' }
      if (!/^[0-9A-Fa-f]{6}$/.test(a.color)) return { ok: false, error: '颜色格式必须是 RRGGBB' }
      Object.assign(textInfo, a)
      pushLog('已应用文字')
      return { ok: true, data: {} }
    }
    case 'getLive':
      return { ok: true, data: { on: live.on, text: live.on ? 'CPU 12%  3200 MHz  45°C    内存 22.2 / 63.9 GB' : '', autostart: live.autostart } }
    case 'setLive': {
      const b = writeBlock()
      if (b) return { ok: false, error: b }
      live.on = (args as { on: boolean }).on
      return { ok: true, data: {} }
    }
    case 'setAutostart':
      live.autostart = (args as { on: boolean }).on
      return { ok: true, data: {} }
    case 'getLinks':
      return {
        ok: true,
        data: {
          ...links,
          linkState: state.linkState,
          receivedHookEvents: 32,
          lastHookEvent: 'claude/SessionEnd',
          sessions: [{ session: 'claude:demo', turn: 't1', state: state.linkState, closed: false }],
          statusText: '任务事件：' + state.linkState,
          signalText: 'SignalRGB：事件接口已接收',
          dotMatrixText: '点阵屏：空闲　已显示'
        }
      }
    case 'setLinks': {
      const a = args as SetLinksArgs
      if (a.idleOffsetMs !== undefined && !(a.idleOffsetMs >= -5000 && a.idleOffsetMs <= 5000)) return { ok: false, error: '时间偏移必须在 -5000 到 5000 毫秒之间' }
      if (a.idleRatePercent !== undefined && !(a.idleRatePercent >= 95 && a.idleRatePercent <= 105)) return { ok: false, error: '播放速度必须在 95 到 105 之间' }
      Object.assign(links, a)
      return { ok: true, data: {} }
    }
    case 'linkDemo': {
      const b = writeBlock()
      if (b) return { ok: false, error: b }
      state.linkState = (args as { state: LinkDemoState }).state
      broadcastState()
      return { ok: true, data: {} }
    }
    case 'idleSync': {
      const b = writeBlock()
      if (b) return { ok: false, error: b }
      pushLog('重播默认动画并对齐')
      return { ok: true, data: {} }
    }
    case 'sendWidget': {
      const b = writeBlock()
      if (b) return { ok: false, error: b }
      const a = args as WidgetRequest
      if (a.action === 'progress') {
        if (!(a.percent !== undefined && a.percent >= 0 && a.percent <= 100)) return { ok: false, error: '进度必须是 0-100' }
        widget = { active: true, kind: 'progress', sent: 'progress|' + a.title, error: '' }
      } else if (a.action === 'notify') {
        if (!a.text) return { ok: false, error: '通知文字不能为空' }
        widget = { active: true, kind: 'notify', sent: 'notify|' + a.text, error: '' }
      } else widget = { active: false, kind: null, sent: '', error: '' }
      return { ok: true, data: { message: widget.active ? '已接收，最多两秒内更新' : '已清除临时显示' } }
    }
    case 'getWidgetStatus':
      return { ok: true, data: { ...widget } }
    case 'setNight': {
      if (state.uploading) return { ok: false, error: '上传进行中，请上传结束后再切换熄灯' }
      const on = (args as { on: boolean }).on
      state.nightMode = on
      pushLog(on ? '睡眠熄灯已开启' : '已恢复灯光和屏幕')
      broadcastState()
      return { ok: true, data: { message: on ? '睡眠熄灯已开启' : '已恢复灯光和屏幕' } }
    }
    case 'readOpening': {
      const b = writeBlock()
      if (b) return { ok: false, error: b }
      return { ok: true, data: { mode: 0 } }
    }
    case 'previewOpening': {
      const b = writeBlock()
      if (b) return { ok: false, error: b }
      return { ok: true, data: { started: true } }
    }
    case 'openStorageManager':
      if (state.uploading) return { ok: false, error: '上传进行中' }
      return { ok: true, data: { started: false } }

    default:
      return { ok: false, error: '未知命令：' + String(cmd) }
  }
}

// 合成一段 24 帧的循环动画；与真实文件一致，画面按"倒置"存放（前端会旋转 180° 显示）
function mockPreview(name: string): PreviewInfo {
  let hue = 0
  for (const ch of name) hue = (hue * 31 + ch.charCodeAt(0)) % 360
  const c = document.createElement('canvas')
  c.width = 480
  c.height = 128
  const g = c.getContext('2d')!
  const frames = []
  for (let i = 0; i < 24; i++) {
    g.save()
    g.translate(480, 128)
    g.rotate(Math.PI)
    g.fillStyle = `hsl(${hue} 60% 8%)`
    g.fillRect(0, 0, 480, 128)
    for (let k = 0; k < 7; k++) {
      g.fillStyle = `hsl(${(hue + k * 40) % 360} 85% 58%)`
      const x = ((i * 20 + k * 70) % 560) - 80
      g.fillRect(x, 18 + k * 13, 90, 9)
    }
    g.fillStyle = '#fff'
    g.font = 'bold 26px sans-serif'
    g.fillText(name, 300, 72)
    g.restore()
    frames.push({ src: c.toDataURL('image/jpeg', 0.8), n: 3 })
  }
  return { available: true, reason: '', fps: 60, frameCount: 72, durationMs: 1200, frames }
}

const MODE_NAMES = ['自定义动画/图片', 'CPU 频率', 'CPU 占用', 'CPU 温度', 'GPU 频率', 'GPU 占用', 'GPU 温度', 'GPU 风扇转速', '显存频率', '显存占用', '内存频率', '内存占用', '内存温度', '时间']
const modes = { selected: [0], interval: 10, auto: true }
const textInfo: SetTextArgs = { text: '', fontSize: 0, color: 'FF0000', show: false }
const live = { on: false, autostart: false }
const links = { codex: true, claude: true, signal: false, dotMatrix: true, idleOffsetMs: 0, idleRatePercent: 100 }
let widget: WidgetStatus = { active: false, kind: null, sent: '', error: '' }

// 与真实 C# 的 WebWriteBlock 一致
function writeBlock(): string | null {
  if (state.uploading) return '上传进行中'
  if (state.maintenance) return '屏幕处于存储维护模式'
  if (state.nightMode) return '请先恢复灯光和屏幕'
  if (!state.connected) return '屏幕未连接'
  return null
}

let mockToken = ''

function runMockUpload(a: StartUploadArgs): void {
  state.uploading = true
  pushLog('开始上传 ' + a.name)
  broadcastState()
  let percent = 0
  const timer = setInterval(() => {
    percent = Math.min(100, percent + 6)
    emit('uploadProgress', { percent, result: percent >= 100 ? 0 : -1 })
    if (percent < 100) return
    clearInterval(timer)
    state.uploading = false
    const existing = mockFiles.findIndex((f) => f.name === a.name)
    if (existing >= 0) mockFiles[existing] = { name: a.name, size: 3_100_000 }
    else mockFiles.push({ name: a.name, size: 3_100_000 })
    if (a.mode === 'movie') {
      settings.current = a.name
      state.playing = a.name
    }
    pushLog('上传完成 ' + a.name)
    broadcastState()
    emit('uploadDone', {
      ok: true,
      name: a.name,
      message: a.mode === 'movie' ? '上传完成，已设为当前全屏动画' : '上传完成，已设为背景'
    })
  }, 300)
}

export async function mockCall<K extends CmdName>(
  cmd: K,
  args?: CmdArgs<K>
): Promise<CmdData<K>> {
  const result = await delay(run(cmd, args))
  if (!result.ok) throw new Error(result.error)
  return result.data as CmdData<K>
}
