// 极简全局 store（不引入 pinia）：一个模块级单例，用 reactive/ref 承载。
import { reactive, ref } from 'vue'
import { bridge } from '../bridge'
import { forgetPreview } from './preview'
import type {
  DeviceState,
  LogLine,
  ScreenFile,
  PickUploadFileData,
  SetBasicArgs,
  Settings,
  SpaceInfo,
  StartUploadArgs,
  StartUploadData,
  UploadDone
} from '../bridge/types'

const state = reactive<DeviceState>({
  connected: false,
  gpu: '',
  com: '',
  firmware: 0,
  uploading: false,
  nightMode: false,
  maintenance: false,
  playing: '',
  linkState: 'idle'
})

const settings = reactive<Settings>({
  brightness: 5,
  opacity: 100,
  dir: 0,
  enabled: true,
  current: ''
})

const files = ref<ScreenFile[]>([])
const log = reactive<LogLine[]>([])

let subscribed = false

function subscribe(): void {
  if (subscribed) return
  subscribed = true

  bridge.on('state', (next) => {
    Object.assign(state, next)
  })

  bridge.on('log', (line) => {
    log.push(line)
    if (log.length > 500) log.splice(0, log.length - 500)
  })
}

subscribe()

async function refresh(): Promise<void> {
  const [nextState, nextSettings, nextFiles, nextLog] = await Promise.all([
    bridge.call('getState'),
    bridge.call('getSettings'),
    bridge.call('listFiles'),
    bridge.call('getLog', { limit: 200 })
  ])

  Object.assign(state, nextState)
  Object.assign(settings, nextSettings)
  files.value = nextFiles
  log.splice(0, log.length, ...nextLog)
}

async function setBasic(args: SetBasicArgs): Promise<void> {
  await bridge.call('setBasic', args)
  // 以 C# 实际生效的值为准（它可能做了范围校验），不在前端自行假设
  Object.assign(settings, await bridge.call('getSettings'))
  Object.assign(state, await bridge.call('getState'))
}

async function playFile(name: string): Promise<void> {
  await bridge.call('playFile', { name })
  // C# 的 Use() 会同时把它设为默认动画，所以 settings 也要重读
  Object.assign(settings, await bridge.call('getSettings'))
  Object.assign(state, await bridge.call('getState'))
}

// ---- 上传（阶段 2）----
// 前端不接触文件内容：pickUploadFile 由 C# 弹文件框并返回 token，startUpload 由 C# 弹原生确认框。
const space = reactive<SpaceInfo>({ known: false, free: 0, total: 0 })
const upload = reactive({ percent: 0, result: -1, lastDone: null as UploadDone | null })

bridge.on('uploadProgress', (p) => {
  upload.percent = p.percent
  upload.result = p.result
})
bridge.on('uploadDone', async (d) => {
  upload.lastDone = d
  forgetPreview(d.name)   // 同名文件被覆盖后，旧的预览画面作废
  // 上传结束后文件列表、空间、默认动画都可能变了，重读一遍
  try {
    await refresh()
    Object.assign(space, await bridge.call('getSpace'))
  } catch (error) {
    console.error('[device] 上传后刷新失败', error)
  }
})

async function refreshSpace(): Promise<void> {
  Object.assign(space, await bridge.call('getSpace'))
}

function pickUploadFile(): Promise<PickUploadFileData> {
  return bridge.call('pickUploadFile')
}

async function startUpload(args: StartUploadArgs): Promise<StartUploadData> {
  upload.percent = 0
  upload.result = -1
  upload.lastDone = null
  const result = await bridge.call('startUpload', args)
  Object.assign(state, await bridge.call('getState'))
  return result
}

/** 一键熄灯 / 恢复。返回 C# 的提示文字。熄灯时其它写命令会被 C# 拒绝，所以恢复入口必须一直可用（上传中除外）。 */
async function setNight(on: boolean): Promise<string> {
  const r = await bridge.call('setNight', { on })
  Object.assign(settings, await bridge.call('getSettings'))
  Object.assign(state, await bridge.call('getState'))
  return r.message
}

const store = {
  setNight,
  state,
  settings,
  files,
  log,
  space,
  upload,
  refresh,
  refreshSpace,
  setBasic,
  playFile,
  pickUploadFile,
  startUpload
}

export function useDeviceStore(): typeof store {
  return store
}
