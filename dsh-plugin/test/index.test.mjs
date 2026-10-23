// 单元测试：事件映射、轮次跟踪、管道写入（用假的 GpuLcdEvents 管道对端）。
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'node:net'
import { createBridge } from '../lib/bridge.js'
import { apply, createTurnTracker, sessionIdOf } from '../lib/index.js'

/** 一个最小的 cordis ctx：记录监听器，emit 时按 waterall 链分发给它们。 */
function fakeCtx() {
  const listeners = new Map()
  const offs = []
  return {
    listeners,
    on(name, fn) {
      if (!listeners.has(name)) listeners.set(name, [])
      listeners.get(name).push(fn)
      const off = () => { const list = listeners.get(name); const i = list.indexOf(fn); if (i >= 0) list.splice(i, 1) }
      offs.push(off)
      return off
    },
    effect(fn) { const d = fn(); return () => d?.() },
    async emit(name, ...args) {
      const list = [...(listeners.get(name) ?? [])]
      const chain = (i) => async () => (i >= list.length ? 'default' : await list[i](...args, () => chain(i + 1)()))
      return chain(0)()
    },
    count() { return [...listeners.values()].reduce((n, l) => n + l.length, 0) },
    disposeAll() { for (const off of offs.splice(0)) off() }
  }
}

const pipePath = (name) => '\\\\.\\pipe\\' + name

/** 起一个假的 GpuLcd 管道，收集收到的每一行。 */
async function fakePanel() {
  const name = 'gpulcd-test-' + process.pid + '-' + Math.random().toString(36).slice(2)
  const lines = []
  const server = createServer((socket) => {
    let buf = ''
    socket.on('data', (d) => {
      buf += d
      let i
      while ((i = buf.indexOf('\n')) >= 0) { lines.push(buf.slice(0, i)); buf = buf.slice(i + 1) }
    })
    socket.on('error', () => {})
  })
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(pipePath(name), resolve) })
  return { name, lines, close: () => new Promise((r) => server.close(r)) }
}

test('sessionIdOf reads the agent id or the session id', () => {
  assert.equal(sessionIdOf({ id: 'a' }), 'a')
  assert.equal(sessionIdOf({ session: { id: 'b' } }), 'b')
  assert.equal(sessionIdOf(undefined), undefined)
  assert.equal(sessionIdOf({ id: '' }), undefined)
})

test('turn tracker gives a new turn per start and forgets on end', () => {
  const turns = createTurnTracker()
  const a = turns.start('s1')
  assert.match(a, /^dsh-/)
  assert.equal(turns.current('s1'), a)
  turns.end('s1')
  assert.notEqual(turns.current('s1'), a, 'after end, a new turn starts')
})

test('a working session reports start, work, waiting, work, and the end of the turn', async () => {
  const panel = await fakePanel()
  const ctx = fakeCtx()
  apply(ctx, { pipeName: panel.name })
  const agent = { id: 'sess-1' }
  await ctx.emit('agent/status', { agent, status: 'running' })
  await ctx.emit('tools/pre-execute', { agent })
  const approval = ctx.emit('approval/request', { agent }, async () => 'allowed')
  await approval
  await ctx.emit('agent/status', { agent, status: 'idle' })
  await new Promise((r) => setTimeout(r, 150))
  const events = panel.lines.map((line) => JSON.parse(line))
  assert.deepEqual(events.map((e) => e.hook_event_name), ['UserPromptSubmit', 'PreToolUse', 'PermissionRequest', 'PreToolUse', 'Stop'])
  assert.ok(events.every((e) => e.client === 'dsh' && e.session_id === 'sess-1'), 'every event is ours')
  const turns = new Set(events.map((e) => e.turn_id))
  assert.equal(turns.size, 1, 'one turn id for the whole turn')
  assert.ok([...turns][0].startsWith('dsh-'))
  ctx.disposeAll()
  await panel.close()
})

test('waiting while another approval is still open stays waiting', async () => {
  const panel = await fakePanel()
  const ctx = fakeCtx()
  apply(ctx, { pipeName: panel.name })
  const agent = { id: 'sess-2' }
  await ctx.emit('agent/status', { agent, status: 'running' })
  let releaseFirst
  const first = new Promise((r) => { releaseFirst = r })
  const a = ctx.emit('approval/request', { agent }, () => first)
  const b = ctx.emit('user-questions/request', { agent }, async () => 'answered')
  await b
  releaseFirst('allowed')
  await a
  await new Promise((r) => setTimeout(r, 120))
  const names = panel.lines.map((line) => JSON.parse(line).hook_event_name)
  assert.deepEqual(names, ['UserPromptSubmit', 'PermissionRequest', 'PreToolUse'], 'one waiting, then back to work when both cleared')
  ctx.disposeAll()
  await panel.close()
})

test('a disposed session ends and is forgotten', async () => {
  const panel = await fakePanel()
  const ctx = fakeCtx()
  apply(ctx, { pipeName: panel.name })
  const agent = { id: 'sess-3' }
  await ctx.emit('agent/status', { agent, status: 'running' })
  await ctx.emit('agent/disposed', agent)
  await new Promise((r) => setTimeout(r, 120))
  const names = panel.lines.map((line) => JSON.parse(line).hook_event_name)
  assert.deepEqual(names, ['UserPromptSubmit', 'SessionEnd'])
  ctx.disposeAll()
  await panel.close()
})

test('bridge writes a JSON line GpuLcd can parse', async () => {
  const panel = await fakePanel()
  const bridge = createBridge({ pipeName: panel.name })
  const ok = await bridge.send({ hook_event_name: 'PreToolUse', session_id: 'sess-1', turn_id: 't1' })
  await new Promise((r) => setTimeout(r, 100))
  assert.equal(ok, true)
  assert.deepEqual(JSON.parse(panel.lines[0]), { hook_event_name: 'PreToolUse', session_id: 'sess-1', turn_id: 't1', client: 'dsh' })
  assert.equal(bridge.stats.sent, 1)
  bridge.dispose()
  await panel.close()
})

test('a missing GpuLcd fails fast instead of blocking the session', async () => {
  const bridge = createBridge({ pipeName: 'definitely-not-running-' + process.pid, connectTimeoutMs: 150 })
  const started = Date.now()
  const ok = await bridge.send({ hook_event_name: 'Stop', session_id: 's' })
  assert.equal(ok, false)
  assert.ok(Date.now() - started < 2000, 'gave up quickly')
  assert.equal(bridge.stats.failed, 1)
  assert.ok(bridge.stats.lastError.length > 0)
  bridge.dispose()
})
