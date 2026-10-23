/**
 * DeepSeek Harness 工作状态 → GpuLcd。
 *
 * GpuLcd 原本只认 Claude Code / Codex 的生命周期钩子：它的事件管道 GpuLcdEvents 收一行 JSON
 * {hook_event_name, session_id, turn_id, client}，再把状态合并成 工作中 / 等待授权 / 一轮结束，
 * 驱动显卡屏动画、SignalRGB 灯效和点阵屏。DSH 没有那种钩子，但有插件事件，所以这里把
 * DSH 的事件翻译成同一套词汇，client 用 "dsh"，GpuLcd 那边不必知道 DSH 内部长什么样。
 *
 * 映射：
 *   agent/status running            → UserPromptSubmit（一轮开始 = 开始工作）
 *   agent/status idle               → Stop（一轮结束）
 *   tools/pre-execute / post-execute → PreToolUse / PostToolUse（长回合里保持「工作中」）
 *   approval/request                → PermissionRequest（在等你点授权）→ 结束后回到工作
 *   user-questions/request          → PermissionRequest（在等你回答问题）→ 结束后回到工作
 *   agent/disposed                  → SessionEnd（会话关掉，立刻清掉它的状态）
 *
 * 不发送任何对话内容：只有事件类型、会话 id、轮次。
 */
import { connect } from 'node:net';
const DEFAULT_PIPE = 'GpuLcdEvents';
/**
 * 往 GpuLcd 的事件管道写一行 JSON。
 * 用 node:net 直接连 \\.\pipe\<name>：不需要额外的原生依赖，超时自己控制，
 * GpuLcd 没开时也能立刻失败而不是挂住。
 */
export function createBridge(options = {}) {
    const pipeName = options.pipeName ?? DEFAULT_PIPE;
    const connectTimeoutMs = options.connectTimeoutMs ?? 400;
    const writeTimeoutMs = options.writeTimeoutMs ?? 400;
    const stats = { sent: 0, failed: 0, lastError: '', lastEvent: '', lastAt: 0 };
    let disposed = false;
    const send = (event) => {
        if (disposed)
            return Promise.resolve(false);
        const payload = JSON.stringify({
            hook_event_name: event.hook_event_name,
            session_id: event.session_id,
            turn_id: event.turn_id ?? '',
            client: 'dsh'
        });
        stats.lastEvent = event.hook_event_name;
        stats.lastAt = Date.now();
        return new Promise((resolve) => {
            let settled = false;
            const finish = (ok, error) => {
                if (settled)
                    return;
                settled = true;
                if (ok)
                    stats.sent++;
                else {
                    stats.failed++;
                    stats.lastError = error ?? 'unknown';
                }
                options.onSent?.({ ok, error, event: event.hook_event_name });
                resolve(ok);
            };
            const socket = connect(`\\\\.\\pipe\\${pipeName}`);
            const timer = setTimeout(() => {
                socket.destroy();
                finish(false, 'connect timeout');
            }, connectTimeoutMs);
            socket.once('connect', () => {
                clearTimeout(timer);
                const writeTimer = setTimeout(() => {
                    socket.destroy();
                    finish(false, 'write timeout');
                }, writeTimeoutMs);
                socket.once('error', (error) => {
                    clearTimeout(writeTimer);
                    finish(false, error.message);
                });
                // GpuLcd 读完一行才回 OK；不等它也能用，读一下避免对端写完就断。
                socket.write(payload + '\n', () => {
                    clearTimeout(writeTimer);
                    socket.end();
                    finish(true);
                });
            });
            socket.once('error', (error) => {
                clearTimeout(timer);
                finish(false, error.message);
            });
        });
    };
    return {
        send,
        stats,
        dispose() { disposed = true; }
    };
}
