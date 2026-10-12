<script setup lang="ts">
// 拨杆开关：两段式「关 | 开」，点亮的那一半是墨色。比胶囊开关更像硬件面板上的拨动开关。
// 用法与 USwitch 对应：v-model + disabled。
const model = defineModel<boolean>({ default: false })
const props = defineProps<{ disabled?: boolean; offLabel?: string; onLabel?: string }>()

function toggle(): void {
  if (props.disabled) return
  model.value = !model.value
}
</script>

<template>
  <button
    type="button"
    role="switch"
    class="rocker"
    :class="{ on: model }"
    :aria-checked="model"
    :disabled="disabled"
    @click="toggle"
  >
    <span class="half off">{{ offLabel ?? '关' }}</span>
    <span class="half on">{{ onLabel ?? '开' }}</span>
  </button>
</template>

<style scoped>
.rocker {
  display: inline-grid;
  grid-template-columns: 1fr 1fr;
  width: 5.2rem;
  height: 1.9rem;
  border: 1px solid var(--ui-border-accented);
  border-radius: 3px;
  background: #0f1215;
  overflow: hidden;
  font-size: 0.8rem;
  transition: border-color 0.15s ease;
}
.rocker:hover:not(:disabled) { border-color: var(--ui-text-muted); }
.rocker:disabled { opacity: 0.4; cursor: not-allowed; }
.half {
  display: grid;
  place-items: center;
  color: var(--ui-text-dimmed);
  transition: background 0.18s ease, color 0.18s ease;
}
/* 关的时候左半亮（灰），开的时候右半亮（墨色） */
.rocker:not(.on) .half.off { background: #2a3138; color: var(--ui-text-toned); }
.rocker.on .half.on { background: var(--ui-text-highlighted); color: #121518; font-weight: 700; }
</style>
