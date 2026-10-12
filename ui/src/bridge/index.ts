// 统一 bridge 层。页面组件只允许调用这里的 bridge.call / bridge.on，
// 禁止直接触碰 window.chrome.webview（见 ui/AGENTS.md）。
import { isMockMode, mockCall, mockOn } from './mock'
import type { CmdArgs, CmdData, CmdName, EventData, EventName } from './types'

export { mockControl } from './mock'
export type { MockSnapshot } from './mock'
export type * from './types'

/** 当前是否运行在 mock（无 C# 宿主）模式 */
export const isMock: boolean = isMockMode

const TIMEOUT_MS = 10_000

interface Pending {
  resolve: (value: unknown) => void
  reject: (error: Error) => void
  timer: ReturnType<typeof setTimeout>
}

const pending = new Map<number, Pending>()
const listeners = new Map<EventName, Set<(data: unknown) => void>>()
let seq = 0

function emit(event: EventName, data: unknown): void {
  listeners.get(event)?.forEach((handler) => {
    try {
      handler(data)
    } catch (error) {
      console.error('[bridge] 事件处理器抛出异常', error)
    }
  })
}

function parseIncoming(raw: unknown): Record<string, unknown> | null {
  let value: unknown = raw
  if (typeof value === 'string') {
    try {
      value = JSON.parse(value)
    } catch {
      console.error('[bridge] 收到非法 JSON', raw)
      return null
    }
  }
  if (value && typeof value === 'object') {
    return value as Record<string, unknown>
  }
  return null
}

function handleMessage(raw: unknown): void {
  const msg = parseIncoming(raw)
  if (!msg) return

  // 主动推送事件（无 id）
  if (typeof msg.event === 'string') {
    emit(msg.event as EventName, msg.data)
    return
  }

  // 命令响应
  if (typeof msg.id === 'number') {
    const entry = pending.get(msg.id)
    if (!entry) return
    clearTimeout(entry.timer)
    pending.delete(msg.id)

    if (msg.ok === true) {
      entry.resolve(msg.data)
    } else {
      const error =
        typeof msg.error === 'string' && msg.error.length > 0
          ? msg.error
          : '命令执行失败'
      entry.reject(new Error(error))
    }
  }
}

if (!isMockMode && typeof window !== 'undefined') {
  window.chrome?.webview?.addEventListener('message', (event) => {
    handleMessage(event.data)
  })
}

function call<K extends CmdName>(
  cmd: K,
  args?: CmdArgs<K>
): Promise<CmdData<K>> {
  if (isMockMode) {
    return mockCall(cmd, args)
  }

  return new Promise<CmdData<K>>((resolve, reject) => {
    const id = ++seq
    const timer = setTimeout(() => {
      pending.delete(id)
      reject(new Error(`命令超时（10 秒）：${cmd}`))
    }, TIMEOUT_MS)

    pending.set(id, {
      resolve: resolve as unknown as (value: unknown) => void,
      reject,
      timer
    })

    window.chrome?.webview?.postMessage(
      JSON.stringify({ id, cmd, args: args ?? {} })
    )
  })
}

function on<E extends EventName>(
  event: E,
  handler: (data: EventData<E>) => void
): () => void {
  if (isMockMode) {
    return mockOn(event, handler)
  }

  let set = listeners.get(event)
  if (!set) {
    set = new Set()
    listeners.set(event, set)
  }
  const h = handler as (data: unknown) => void
  set.add(h)
  return () => {
    set.delete(h)
  }
}

export const bridge = { call, on }
