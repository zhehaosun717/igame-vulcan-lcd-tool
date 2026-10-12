<script setup lang="ts" generic="T extends string | number">
// 分段选择：几个互斥选项并排，当前项反白。用于方向、适配方式这类 2–4 个选项的选择。
const model = defineModel<T>({ required: true })
defineProps<{ items: { label: string; value: T }[]; disabled?: boolean }>()
</script>

<template>
  <div class="seg" role="radiogroup">
    <button
      v-for="it in items"
      :key="String(it.value)"
      type="button"
      role="radio"
      :aria-checked="model === it.value"
      :class="{ on: model === it.value }"
      :disabled="disabled"
      @click="model = it.value"
    >
      {{ it.label }}
    </button>
  </div>
</template>

<style scoped>
.seg {
  display: inline-flex;
  border: 1px solid var(--ui-border-accented);
  border-radius: 3px;
  overflow: hidden;
  background: #0f1215;
}
button {
  min-width: 3.4rem;
  padding: 0.38rem 0.9rem;
  font-size: 0.82rem;
  color: var(--ui-text-muted);
  border-right: 1px solid var(--ui-border);
  transition: background 0.15s ease, color 0.15s ease;
}
button:last-child { border-right: 0; }
button:hover:not(:disabled):not(.on) { color: var(--ui-text-highlighted); background: #1a1f23; }
button.on { background: var(--ui-text-highlighted); color: #121518; font-weight: 700; }
button:disabled { opacity: 0.4; cursor: not-allowed; }
</style>
