<script setup lang="ts">
// 提示条：左边一条竖线 + 文字。中性提示是灰色；只有警告和错误才带状态色——颜色只表示状态。
// 与 UAlert 的用法兼容：color / title / description。
import { computed } from 'vue'

const props = defineProps<{
  color?: string // 'warning' | 'error' | 其它（视为中性）
  title?: string
  description?: string
}>()

const tone = computed(() => (props.color === 'error' ? 'error' : props.color === 'warning' ? 'warn' : 'neutral'))
</script>

<template>
  <div class="notice" :data-tone="tone" role="status">
    <p v-if="title" class="t">{{ title }}</p>
    <p v-if="description" class="d">{{ description }}</p>
    <slot />
  </div>
</template>

<style scoped>
.notice {
  --bar: var(--ui-border-accented);
  position: relative;
  padding: 0.15rem 0 0.15rem 0.95rem;
  margin-bottom: 1.4rem;
}
.notice::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 2px;
  background: var(--bar);
}
.notice[data-tone='warn'] { --bar: var(--color-state-waiting); }
.notice[data-tone='error'] { --bar: var(--color-state-error); }
.t { font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.d { margin-top: 0.15rem; font-size: 0.8rem; color: var(--ui-text-muted); max-width: 60ch; }
</style>
