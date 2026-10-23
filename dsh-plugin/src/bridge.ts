import { connect, type Socket } from 'node:net'

export interface HookEventInput {
  hook_event_name: string
  session_id: string
  turn_id?: string
  /** Atomic DSH UserPromptSubmit state; paired receiver required for waiting. */
  state?: 'working' | 'waiting'
}
export type HookEventSource = HookEventInput | (() => HookEventInput | undefined)
export interface BridgeOptions {
  pipeName?: string
  connectTimeoutMs?: number
  ackTimeoutMs?: number
  maxPending?: number
  maxQueueAgeMs?: number
  onSent?: (result: { ok: boolean; error?: string; event: string }) => unknown
  /** Dependency seam for isolated socket tests; never read from plugin configuration. */
  connect?: (path: string) => Socket
}
export interface Bridge {
  send(event: HookEventSource): Promise<boolean>
  readonly stats: { sent: number; failed: number; dropped: number; lastError: string; lastEvent: string; lastAt: number; pending: number; active: number; timers: number }
  dispose(): void
}
const bounded = (value: number | undefined, fallback: number, max: number) =>
  typeof value === 'number' && Number.isFinite(value) ? Math.max(1, Math.min(max, Math.floor(value))) : fallback

/** One in-flight connection; FIFO with bounded pending count and age, no retries.
 * Success means an exact OK line was read, NOT merely a successful socket.write.
 * It acknowledges the receiver protocol, not physical hardware rendering.
 */
export function createBridge(options: BridgeOptions = {}): Bridge {
  const pipeName = options.pipeName ?? 'GpuLcdEvents'
  const connectMs = bounded(options.connectTimeoutMs, 400, 10000)
  const ackMs = bounded(options.ackTimeoutMs, 400, 10000)
  const maxPending = bounded(options.maxPending, 64, 1024)
  const maxAge = bounded(options.maxQueueAgeMs, 5000, 60000)
  const stats = { sent: 0, failed: 0, dropped: 0, lastError: '', lastEvent: '', lastAt: 0, pending: 0, active: 0, timers: 0 }
  type Item = { event: string; payload: string; current?: () => HookEventInput | undefined; at: number; resolve: (ok: boolean) => void }
  const queue: Item[] = []
  let disposed = false
  let cancelActive: (() => void) | undefined
  let pumping = false
  const report = (item: Item, ok: boolean, error?: string) => {
    stats.lastEvent = item.event
    stats.lastAt = Date.now()
    if (ok) stats.sent++
    else { stats.failed++; stats.lastError = error ?? 'unknown' }
    item.resolve(ok)
    // User callbacks, including async callbacks, are strictly off the outcome path.
    try { void Promise.resolve(options.onSent?.({ ok, error, event: item.event })).catch(() => {}) } catch {}
  }
  const serialize = (event: HookEventInput) => {
    const payload = JSON.stringify({ hook_event_name: event.hook_event_name, session_id: event.session_id, turn_id: event.turn_id ?? '', client: 'dsh', ...(event.state === undefined ? {} : { state: event.state }) }) + '\n'
    if (Buffer.byteLength(payload) > 4096) throw new Error('event too large')
    return payload
  }
  const transmit = (item: Item) => new Promise<void>((resolve) => {
    let socket: Socket | undefined
    let timer: ReturnType<typeof setTimeout> | undefined
    let settled = false
    let response = ''
    const clearTimer = () => {
      if (timer !== undefined) { clearTimeout(timer); timer = undefined; stats.timers-- }
    }
    const finish = (ok: boolean, error?: string) => {
      if (settled) return
      settled = true
      clearTimer()
      cancelActive = undefined
      stats.active = 0
      // Leave the contained error handler attached until close: late errors from
      // destroy/write must never become uncaught 'error' events.
      socket?.removeAllListeners('connect')
      socket?.removeAllListeners('data')
      socket?.removeAllListeners('end')
      socket?.removeAllListeners('close')
      socket?.destroy()
      report(item, ok, error)
      resolve()
    }
    const arm = (ms: number, error: string) => {
      clearTimer()
      stats.timers++
      timer = setTimeout(() => finish(false, error), ms)
      timer.unref()
    }
    cancelActive = () => finish(false, 'disposed')
    stats.active = 1
    try {
      socket = (options.connect ?? connect)(
        process.platform === 'win32' ? '\\\\.\\pipe\\' + pipeName : pipeName)
      socket.on('error', (error: Error) => finish(false, error.message))
      socket.once('close', () => finish(false, 'closed before OK'))
      socket.once('end', () => finish(false, 'ended before OK'))
      socket.on('data', (data: Buffer) => {
        response += data.toString('utf8')
        if (response.length > 1024) return finish(false, 'oversized acknowledgement')
        const newline = response.indexOf('\n')
        if (newline >= 0) {
          const line = response.slice(0, newline).replace(/\r$/, '')
          finish(line === 'OK', line === 'OK' ? undefined : 'receiver rejected: ' + line)
        }
      })
      arm(connectMs, 'connect timeout')
      socket.once('connect', () => {
        arm(ackMs, 'ack timeout') // bounds writing plus reception, even if write never calls back
        try {
          let payload = item.payload
          if (item.current) {
            const current = item.current()
            if (!current) { finish(false, 'stale snapshot'); return }
            item.event = current.hook_event_name
            payload = serialize(current) // after connect, immediately before write
          }
          socket!.write(payload, (error?: Error | null) => { if (error) finish(false, error.message) })
        }
        catch (error) { finish(false, String(error)) }
      })
    } catch (error) { finish(false, String(error)) }
  })
  const pump = async () => {
    if (pumping) return
    pumping = true
    try {
      while (!disposed && queue.length) {
        const item = queue.shift()!
        stats.pending = queue.length
        if (Date.now() - item.at > maxAge) { stats.dropped++; report(item, false, 'queue expired'); continue }
        await transmit(item)
      }
    } finally { pumping = false }
  }
  return {
    stats,
    send(event) {
      if (disposed) return Promise.resolve(false)
      // Plain inputs snapshot at enqueue; guarded sources resolve at write time.
      // Both serialization paths convert failure to a settled false result.
      return new Promise<boolean>((resolve) => {
        let payload = ''
        const current = typeof event === 'function' ? event : undefined
        try { if (!current) payload = serialize(event as HookEventInput) }
        catch (error) {
          report({ event: 'invalid', payload: '', at: Date.now(), resolve }, false, String(error)); return
        }
        if (queue.length >= maxPending) {
          stats.dropped++
          report(queue.shift()!, false, 'queue overflow') // retain newer state; retained events remain FIFO
        }
        queue.push({ event: current ? 'snapshot' : (event as HookEventInput).hook_event_name, payload, current, at: Date.now(), resolve })
        stats.pending = queue.length
        void pump().catch(() => { /* transmit contains all socket failures */ })
      })
    },
    dispose() {
      if (disposed) return
      disposed = true
      cancelActive?.()
      for (const item of queue.splice(0)) report(item, false, 'disposed')
      stats.pending = 0
    }
  }
}
