<script setup lang="ts">
// 推子：滑块是"范围值"，所以刻度是有信息的——刻度尺标出可调范围，右边用大号窄体数字读数。
import { computed, useId } from 'vue'

const model = defineModel<number>({ required: true })
const props = defineProps<{
  label: string
  min: number
  max: number
  step?: number
  disabled?: boolean
  suffix?: string
  help?: string
}>()

const id = useId()
const pct = computed(() => ((model.value - props.min) / (props.max - props.min)) * 100)
// 刻度：范围不大就每格一刻，否则分成 10 等份
const ticks = computed(() => {
  const span = props.max - props.min
  const n = span <= 12 ? span : 10
  return Array.from({ length: n + 1 }, (_, i) => Math.round(props.min + (span / n) * i))
})
</script>

<template>
  <div class="fader" :class="{ off: disabled }">
    <div class="top">
      <label :for="id">{{ label }}</label>
      <output class="num" :for="id">{{ model }}<small v-if="suffix">{{ suffix }}</small></output>
    </div>

    <input
      :id="id"
      v-model.number="model"
      type="range"
      :min="min"
      :max="max"
      :step="step ?? 1"
      :disabled="disabled"
      :style="{ '--p': pct + '%' }"
    />
    <div class="ruler" aria-hidden="true">
      <span v-for="t in ticks" :key="t"><i /><em>{{ t }}</em></span>
    </div>

    <p v-if="help" class="help">{{ help }}</p>
  </div>
</template>

<style scoped>
.fader { max-width: 34rem; }
.fader + .fader { margin-top: 1.9rem; }
.top { display: flex; align-items: baseline; justify-content: space-between; }
.top label { font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.top output { font-size: 2.2rem; font-weight: 600; line-height: 1; color: var(--ui-text-highlighted); }
.top output small { margin-left: 0.15rem; font-size: 1rem; color: var(--ui-text-muted); }

input[type='range'] {
  appearance: none;
  width: 100%;
  height: 1.7rem;
  margin: 0.55rem 0 0;
  background: transparent;
  cursor: pointer;
}
input[type='range']::-webkit-slider-runnable-track {
  height: 2px;
  background: linear-gradient(90deg, var(--ui-text-highlighted) var(--p), #2b323a var(--p));
}
input[type='range']::-webkit-slider-thumb {
  appearance: none;
  width: 11px;
  height: 24px;
  margin-top: -11px;
  border-radius: 2px;
  background:
    linear-gradient(90deg, transparent 4px, #121518 4px 6px, transparent 6px),
    #e9ecef;
  box-shadow: 0 2px 8px rgb(0 0 0 / 0.55);
}
input[type='range']:hover::-webkit-slider-thumb { background:
    linear-gradient(90deg, transparent 4px, #121518 4px 6px, transparent 6px),
    #ffffff; }
input[type='range']:disabled { cursor: not-allowed; }
.off input[type='range']::-webkit-slider-thumb { background: #3b444d; box-shadow: none; }
.off input[type='range']::-webkit-slider-runnable-track { background: #2b323a; }
.off .top output { color: var(--ui-text-dimmed); }

/* 刻度尺：和推子轨道左右对齐（半个拇指宽的内缩） */
.ruler { display: flex; justify-content: space-between; padding: 0 5px; margin-top: -0.2rem; }
.ruler span { display: grid; justify-items: center; width: 0; }
.ruler i { width: 1px; height: 6px; background: var(--ui-border-accented); }
.ruler em { margin-top: 0.15rem; font-style: normal; font-size: 0.66rem; color: var(--ui-text-dimmed); font-family: var(--font-display); white-space: nowrap; }

.help { margin-top: 0.55rem; font-size: 0.76rem; color: var(--ui-text-muted); }
</style>
