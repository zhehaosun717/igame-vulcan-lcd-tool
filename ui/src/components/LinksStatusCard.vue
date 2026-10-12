<script setup lang="ts">
import type { LinksInfo } from '../bridge/types'
import Plate from './Plate.vue'

defineProps<{
  info: LinksInfo | null
}>()

const stateText: Record<string, string> = {
  idle: '空闲',
  working: '工作中',
  waiting: '等待中',
  done: '完成'
}

function tone(state: string): string {
  return state === 'working' || state === 'waiting' || state === 'done' ? state : 'idle'
}

function label(state: string): string {
  return stateText[state] ?? state
}
</script>

<template>
  <Plate title="联动状态">
    <template #aside>
      <span v-if="info" class="state"><i class="dot" :class="tone(info.linkState)" />{{ label(info.linkState) }}</span>
      <span v-else class="state"><i class="dot idle" />读取中…</span>
    </template>

    <template v-if="info">
      <dl class="spec">
        <div>
          <dt>任务事件</dt>
          <dd>{{ info.statusText || '—' }}</dd>
        </div>
        <div>
          <dt>SignalRGB</dt>
          <dd>{{ info.signalText || '—' }}</dd>
        </div>
        <div>
          <dt>点阵屏</dt>
          <dd>{{ info.dotMatrixText || '—' }}</dd>
        </div>
        <div>
          <dt>已接收任务事件</dt>
          <dd class="num">{{ info.receivedHookEvents }}</dd>
        </div>
        <div>
          <dt>最近事件</dt>
          <dd class="num break">{{ info.lastHookEvent || '—' }}</dd>
        </div>
      </dl>

      <div class="sessions">
        <p class="cap">活动会话</p>
        <p v-if="info.sessions.length === 0" class="empty">没有活动会话</p>
        <ul v-else class="rows">
          <li v-for="(session, index) in info.sessions" :key="index">
            <span class="s-name num">{{ session.session }}</span>
            <span class="s-turn num">turn {{ session.turn }}</span>
            <span class="s-state">
              <i class="dot" :class="tone(session.state)" />{{ session.state }}<template v-if="session.closed">（已结束）</template>
            </span>
          </li>
        </ul>
      </div>
    </template>
    <p v-else class="empty">正在读取联动状态…</p>
  </Plate>
</template>

<style scoped>
.state { display: inline-flex; align-items: center; gap: 0.45rem; font-size: 0.8rem; color: var(--ui-text-muted); }
.dot {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: #2a3138;
  box-shadow: inset 0 0 0 1px #3b444d;
}
.dot.working { background: var(--color-state-working); box-shadow: 0 0 0.5rem var(--color-state-working); }
.dot.waiting { background: var(--color-state-waiting); box-shadow: 0 0 0.5rem var(--color-state-waiting); }
.dot.done { background: var(--color-state-done); box-shadow: 0 0 0.5rem var(--color-state-done); }
.dot.idle { background: var(--ui-border-accented); }

/* 读数：两列规格表，不做卡片 */
.spec { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1.1rem 2rem; }
.spec > div { min-width: 0; }
.spec dt { font-size: 0.72rem; color: var(--ui-text-dimmed); }
.spec dd { margin-top: 0.18rem; font-size: 0.9rem; color: var(--ui-text-highlighted); }
.spec dd.num { font-size: 1.05rem; font-weight: 600; }
.break { overflow-wrap: anywhere; }

/* 会话：规格表行 */
.sessions { margin-top: 1.7rem; padding-top: 1.3rem; border-top: 1px solid var(--ui-border-muted); }
.cap { font-size: 0.78rem; color: var(--ui-text-muted); margin-bottom: 0.5rem; }
.rows { display: grid; }
.rows li {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  gap: 1.5rem;
  align-items: center;
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--ui-border-muted);
}
.rows li:first-child { border-top: 1px solid var(--ui-border-muted); }
.s-name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.82rem; color: var(--ui-text); }
.s-turn { font-size: 0.78rem; color: var(--ui-text-muted); }
.s-state { display: inline-flex; align-items: center; gap: 0.4rem; justify-self: end; font-size: 0.78rem; color: var(--ui-text-muted); }
.empty { font-size: 0.82rem; color: var(--ui-text-muted); }
</style>
