import { randomUUID } from 'node:crypto'
import { createBridge, type Bridge, type HookEventInput } from './bridge.js'

export const name = 'dsh-gpulcd-status'
export const inject = ['agents']

/** Explicit projections of audited public contracts, not ambient Cordis shims.
 * Additional fields are deliberately never inspected or sent. See CONTRACTS.md.
 */
export interface AgentLike { id: string; status?: string; session: { id: string } }
interface Request { agent?: AgentLike; signal?: AbortSignal }
interface SessionLike { id: string }
interface SessionEvent { type: string; data: { turn?: number; reason?: { kind: string } } }
type Next<T = unknown> = () => T | Promise<T>
interface Events {
  'agent/status': (payload: { agent: AgentLike; status: string }) => void
  'agent/created': (payload: { agent: AgentLike }) => void
  'agent/disposed': (payload: { agent: AgentLike }) => void
  'session/event': (session: SessionLike, event: SessionEvent) => void
  'tools/pre-execute': (request: Request, next: Next) => unknown
  'approval/request': (request: Request, next: Next) => unknown
  'user-questions/request': (request: Request, next: Next) => unknown
}
export interface PluginContext {
  on<K extends keyof Events>(event: K, listener: Events[K]): () => void
  effect(callback: () => () => void): unknown
  agents?: { list(): AgentLike[] }
}
export interface Config { pipeName?: string; heartbeatMs?: number }
export function sessionIdOf(agent: unknown): string | undefined {
  if (!agent || typeof agent !== 'object') return undefined
  const value = agent as { id?: unknown; session?: { id?: unknown } }
  const id = value.id ?? value.session?.id
  return typeof id === 'string' && id !== '' ? id : undefined
}

export function createTurnTracker() {
  const turns = new Map<string, string>()
  const epoch = randomUUID()
  let counter = 0
  return {
    start(sessionId: string) { const id = 'dsh-' + epoch + '-' + (++counter).toString(36); turns.set(sessionId, id); return id },
    current(sessionId: string) { return turns.get(sessionId) }, // reads must never create a turn
    has(sessionId: string) { return turns.has(sessionId) },
    end(sessionId: string) { turns.delete(sessionId) }
  }
}

/** Dependency seam is not a config option: tests cannot accidentally open the real pipe. */
export function attach(ctx: PluginContext, bridge: Bridge, config: Config = {}): () => void {
  const turns = createTurnTracker()
  type Turn = { agent: AgentLike; id: string; waits: Map<symbol, () => void>; number?: number; stepped: boolean; startAck: boolean; startPending: boolean }
  const active = new Map<string, Turn>()
  const running = new Map<string, AgentLike>()
  const closed = new WeakSet<object>()
  const offs: Array<() => void> = []
  let disposed = false
  const safe = (fn: () => void) => { try { if (!disposed) fn() } catch {} }
  const emit = (event: HookEventInput['hook_event_name'], session: string, turn: string): Promise<boolean> => {
    try { return Promise.resolve(bridge.send({ hook_event_name: event, session_id: session, turn_id: turn })).catch(() => false) } catch { return Promise.resolve(false) }
  }
  const refresh = (session: string, turn: Turn) => {
    // Every active-state observation is a guarded current snapshot, not an
    // enqueue-time status. Earlier work/wait requests must not invert a newer
    // state or recreate a closed/replaced generation when delivery is delayed.
    const current = () => disposed || active.get(session) !== turn ? undefined : {
      hook_event_name: turn.waits.size ? 'PermissionRequest' : 'PreToolUse',
      session_id: session, turn_id: turn.id
    }
    try { void Promise.resolve(bridge.send(current)).catch(() => {}) } catch {}
  }
  const sendStart = (session: string, turn: Turn) => {
    if (turn.startPending || turn.startAck) return
    turn.startPending = true
    // Resolve state at actual serialization, not enqueue time. A queued old
    // Turn must not resurrect after idle/dispose/new generation.
    const current = () => disposed || active.get(session) !== turn ? undefined : {
      hook_event_name: 'UserPromptSubmit', session_id: session, turn_id: turn.id,
      state: turn.waits.size ? 'waiting' as const : 'working' as const
    }
    let sent: Promise<boolean>
    try { sent = Promise.resolve(bridge.send(current)).catch(() => false) } catch { sent = Promise.resolve(false) }
    void sent.then(ok => {
      if (active.get(session) !== turn) return
      turn.startPending = false
      turn.startAck = ok
    })
  }
  const start = (agent: AgentLike): Turn | undefined => {
    const session = sessionIdOf(agent)
    if (!session || closed.has(agent)) return
    let turn = active.get(session)
    if (turn?.agent === agent) return turn
    if (turn) {
      active.delete(session)
      for (const release of turn.waits.values()) release()
      turn.waits.clear()
      emit('SessionEnd', session, turn.id)
    }
    turn = { agent, id: turns.start(session), waits: new Map(), stepped: false, startAck: false, startPending: false }
    active.set(session, turn)
    sendStart(session, turn)
    return turn
  }
  const finish = (session: string, turn: Turn, completed: boolean) => {
    if (active.get(session) !== turn) return
    active.delete(session)
    turns.end(session)
    for (const release of [...turn.waits.values()]) release()
    turn.waits.clear()
    emit(completed ? 'Stop' : 'SessionEnd', session, turn.id)
  }
  const observe = (request: Request): { session: string; turn: Turn } | undefined => {
    const agent = request?.agent
    const session = sessionIdOf(agent)
    if (!session || !agent || closed.has(agent)) return
    let turn = active.get(session)
    // Middleware is telemetry only: late tool/wait callbacks never open turns.
    if (turn?.agent !== agent) return
    return { session, turn }
  }
  offs.push(ctx.on('agent/status', (payload) => safe(() => {
    const agent = payload.agent
    const session = sessionIdOf(agent)
    if (!session || closed.has(agent)) return
    if (payload.status === 'running') { running.set(session, agent); const turn = start(agent); if (turn) refresh(session, turn) }
    else if (payload.status === 'idle') {
      if (running.get(session) === agent) running.delete(session)
      const turn = active.get(session)
      if (turn?.agent === agent) finish(session, turn, false) // idle alone is not proof of success
    }
  })))
  offs.push(ctx.on('agent/created', ({ agent }) => safe(() => { if (agent.status === 'running') { running.set(agent.id, agent); start(agent) } })))
  offs.push(ctx.on('agent/disposed', ({ agent }) => safe(() => {
    if (closed.has(agent)) return
    closed.add(agent)
    const session = sessionIdOf(agent)
    if (!session) return
    if (running.get(session) === agent) running.delete(session)
    const turn = active.get(session)
    if (turn?.agent === agent) finish(session, turn, false)
    else if (!turn) emit('SessionEnd', session, '')
  })))
  offs.push(ctx.on('session/event', (session, event) => safe(() => {
    let turn = active.get(session.id)
    const agent = running.get(session.id)
    if (!turn && event.type === 'turn/start' && agent?.session === session) turn = start(agent)
    // Session identity prevents an old session instance from ending a resumed
    // instance with the same id. Real agents carry this exact Session object.
    if (!turn || turn.agent.session !== session) return
    if (event.type === 'turn/start') {
      if (turn.number !== undefined && turn.number !== event.data.turn) {
        finish(session.id, turn, false)
        const next = start(turn.agent)
        if (next) next.number = event.data.turn
      } else turn.number = event.data.turn
    } else if (event.type === 'step/start' && (turn.number === undefined || turn.number === event.data.turn)) {
      turn.number = event.data.turn
      turn.stepped = true
    } else if (event.type === 'turn/end' && (turn.number === undefined || turn.number === event.data.turn)) {
      finish(session.id, turn, turn.stepped && event.data.reason?.kind === 'completed')
    }
  })))
  // Never synthesize allow: preserve exactly the original downstream return
  // (including Promise identity here), synchronous throws and async rejections.
  offs.push(ctx.on('tools/pre-execute', (request, next) => {
    safe(() => { const state = observe(request); if (state) refresh(state.session, state.turn) })
    return next()
  }))
  const wait = (request: Request, next: Next): unknown => {
    let state: ReturnType<typeof observe>
    const token = Symbol('wait')
    let released = false
    const release = () => {
      if (released) return
      released = true
      try { request?.signal?.removeEventListener('abort', release) } catch {}
      safe(() => {
        if (!state || active.get(state.session) !== state.turn || !state.turn.waits.delete(token)) return
        if (!state.turn.waits.size) refresh(state.session, state.turn)
      })
    }
    safe(() => {
      state = observe(request)
      if (!state || request?.signal?.aborted) return
      state.turn.waits.set(token, release)
      refresh(state.session, state.turn)
      request?.signal?.addEventListener('abort', release, { once: true })
    })
    try {
      const result = next()
      if (result && typeof (result as PromiseLike<unknown>).then === 'function') return Promise.resolve(result).finally(release)
      release()
      return result
    } catch (error) { release(); throw error }
  }
  offs.push(ctx.on('approval/request', wait))
  offs.push(ctx.on('user-questions/request', wait))
  // Recover a missing start at most once per heartbeat until transport ACK.
  // Once ACKed, ordinary state heartbeats suffice after receiver ClearClient/restart.
  const heartbeatMs = typeof config.heartbeatMs === 'number' && Number.isFinite(config.heartbeatMs)
    ? Math.max(10, Math.min(300000, config.heartbeatMs)) : 15000
  const heartbeat = setInterval(() => safe(() => {
    for (const [session, turn] of active) {
      if (!turn.startAck) sendStart(session, turn)
      else refresh(session, turn)
    }
  }), heartbeatMs)
  heartbeat.unref()
  safe(() => { for (const agent of ctx.agents?.list() ?? []) if (agent.status === 'running') { running.set(agent.id, agent); start(agent) } })
  const cleanup = () => {
    if (disposed) return
    disposed = true
    clearInterval(heartbeat)
    for (const turn of active.values()) for (const release of turn.waits.values()) release()
    active.clear()
    running.clear()
    for (const off of offs.splice(0)) { try { off() } catch {} }
    try { bridge.dispose() } catch {}
  }
  ctx.effect(() => cleanup)
  return cleanup
}
export function apply(ctx: PluginContext, config: Config = {}): void {
  attach(ctx, createBridge({ pipeName: typeof config.pipeName === 'string' && config.pipeName ? config.pipeName : undefined }), config)
}
