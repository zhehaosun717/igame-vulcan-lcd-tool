import { createBridge } from './bridge.js';
export const name = 'dsh-gpulcd-status';
/** 取会话 id：agent 自身的 id 就是会话 id，agent/status 也带 session.id。 */
export function sessionIdOf(agent) {
    const a = agent;
    const id = a?.id ?? a?.session?.id;
    return typeof id === 'string' && id !== '' ? id : undefined;
}
/** 每轮一个递增短 id，让 GpuLcd 能把同一会话的不同轮次分开（它按 turn_id 判断是否新的一轮）。 */
export function createTurnTracker() {
    const turns = new Map();
    let counter = 0;
    return {
        start(sessionId) {
            const id = 'dsh-' + (++counter).toString(36);
            turns.set(sessionId, { id, startedAt: Date.now() });
            return id;
        },
        current(sessionId) {
            const entry = turns.get(sessionId);
            return entry?.id ?? this.start(sessionId);
        },
        has(sessionId) { return turns.has(sessionId); },
        end(sessionId) { turns.delete(sessionId); }
    };
}
export function apply(ctx, config) {
    const pipeName = typeof config?.pipeName === 'string' && config.pipeName !== '' ? config.pipeName : undefined;
    const bridge = createBridge({ pipeName });
    const turns = createTurnTracker();
    const open = new Map(); // 每个会话正在等待的人数（授权 + 提问可以同时挂着）
    const ctxAny = ctx;
    const emit = (hook_event_name, sessionId, turn_id) => {
        if (sessionId === undefined)
            return;
        void bridge.send({ hook_event_name, session_id: sessionId, turn_id });
    };
    /** 进入等待：第一次挂起时告诉 GpuLcd「在等你」，最后一个挂起解除时回到工作中。 */
    const beginWait = (sessionId) => {
        const count = open.get(sessionId) ?? 0;
        open.set(sessionId, count + 1);
        if (count === 0)
            emit('PermissionRequest', sessionId, turns.current(sessionId));
    };
    const endWait = (sessionId) => {
        const count = (open.get(sessionId) ?? 0) - 1;
        if (count > 0) {
            open.set(sessionId, count);
            return;
        }
        open.delete(sessionId);
        emit('PreToolUse', sessionId, turns.current(sessionId));
    };
    const register = (name, fn) => {
        const off = ctxAny.on(name, fn);
        return off;
    };
    const disposers = [];
    // 一轮的开始与结束：running → 开始（新 turn_id），idle → Stop。
    disposers.push(register('agent/status', (payload) => {
        const sessionId = sessionIdOf(payload?.agent);
        if (sessionId === undefined)
            return;
        if (payload.status === 'running') {
            if (!turns.has(sessionId)) {
                const turn = turns.start(sessionId);
                emit('UserPromptSubmit', sessionId, turn);
            }
            else {
                emit('PreToolUse', sessionId, turns.current(sessionId));
            }
            return;
        }
        if (payload.status === 'idle') {
            const turn = turns.current(sessionId);
            open.delete(sessionId);
            turns.end(sessionId);
            emit('Stop', sessionId, turn);
        }
    }));
    // 工具调用：长回合里让「工作中」保持新鲜（GpuLcd 30 分钟没有事件就把状态忘了）。
    disposeToolEvents();
    function disposeToolEvents() {
        disposers.push(register('tools/pre-execute', (payload) => {
            const sessionId = sessionIdOf(payload?.agent);
            if (sessionId !== undefined)
                emit('PreToolUse', sessionId, turns.current(sessionId));
            return undefined;
        }));
    }
    // 等你确认：授权请求和提问都算。处理链必须放行（return next() 的结果）。
    disposers.push(register('approval/request', (request, next) => {
        const sessionId = sessionIdOf(request?.agent);
        if (sessionId === undefined)
            return next();
        beginWait(sessionId);
        return Promise.resolve(next()).finally(() => endWait(sessionId));
    }));
    disposers.push(register('user-questions/request', (request, next) => {
        const sessionId = sessionIdOf(request?.agent);
        if (sessionId === undefined)
            return next();
        beginWait(sessionId);
        return Promise.resolve(next()).finally(() => endWait(sessionId));
    }));
    // 会话关掉：清掉状态，别让屏幕一直停在「本轮结束」。
    disposers.push(register('agent/disposed', (agent) => {
        const sessionId = sessionIdOf(agent);
        if (sessionId === undefined)
            return undefined;
        open.delete(sessionId);
        turns.end(sessionId);
        emit('SessionEnd', sessionId, '');
        return undefined;
    }));
    const cleanup = () => {
        for (const off of disposers.splice(0)) {
            try {
                off();
            }
            catch { /* 卸载时忽略 */ }
        }
        bridge.dispose();
    };
    if (ctxAny.effect)
        ctxAny.effect(() => cleanup);
    else
        ctx.on('dispose', cleanup);
}
