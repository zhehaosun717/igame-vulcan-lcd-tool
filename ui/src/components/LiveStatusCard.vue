<script setup lang="ts">
import Plate from './Plate.vue'

defineProps<{
  active: boolean
  text: string
}>()
</script>

<template>
  <Plate title="最近一次推送">
    <template #aside>
      <span class="tone"><i class="dot" :class="active ? 'on' : 'off'" />{{ active ? '刷新中' : '已停止' }}</span>
    </template>

    <p v-if="active && text" class="reading num">{{ text }}</p>
    <p v-else-if="active" class="idle">等待第一次推送…</p>
    <p v-else class="idle">实时刷新已关闭，屏幕不会推送 CPU / 内存数据。</p>
  </Plate>
</template>

<style scoped>
.tone { display: inline-flex; align-items: center; gap: 0.45rem; font-size: 0.8rem; color: var(--ui-text-muted); }
.dot {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: #2a3138;
  box-shadow: inset 0 0 0 1px #3b444d;
}
.dot.on { background: var(--color-state-done); box-shadow: 0 0 0.5rem var(--color-state-done); }
.dot.off { background: var(--ui-border-accented); }

/* 大号读数区：推送的数据用窄体仪表盘字形铺开 */
.reading {
  font-size: 1.85rem;
  font-weight: 600;
  line-height: 1.35;
  letter-spacing: 0.01em;
  color: var(--ui-text-highlighted);
  white-space: pre-wrap;
  word-break: break-word;
}
.idle { font-size: 0.82rem; color: var(--ui-text-muted); }
</style>
