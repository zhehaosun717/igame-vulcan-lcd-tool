<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { PreviewInfo, ScreenFile } from '../bridge/types'
import { loadPreview, peek, peekName } from '../stores/preview'

const props = defineProps<{
  file: ScreenFile
  current: boolean
  playing: boolean
  loading: boolean
  disabled: boolean
}>()

const emit = defineEmits<{ play: [name: string] }>()

function formatSize(size: number): string {
  if (!Number.isFinite(size) || size < 0) return '—'
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / (1024 * 1024)).toFixed(2)} MB`
}

// 缩略图：取动画中段的一帧；没有本机副本时显示占位，并在提示里说明原因
const info = ref<PreviewInfo | null>(null)
onMounted(async () => {
  info.value = await loadPreview(props.file.name)
})
const thumb = computed(() => {
  const f = info.value?.available ? info.value.frames : undefined
  return f && f.length ? f[Math.floor(f.length / 2)].src : ''
})
const peeking = computed(() => peekName.value === props.file.name)
</script>

<template>
  <div class="row">
    <button
      type="button"
      class="thumb"
      :class="{ on: peeking }"
      :disabled="!thumb"
      :title="thumb ? '在上方预览这个文件' : info?.reason || '读取中…'"
      :aria-label="thumb ? '预览 ' + file.name : '无法预览 ' + file.name"
      @click="peek(file.name)"
    >
      <img v-if="thumb" :src="thumb" alt="" />
      <span v-else class="none">{{ info ? '无副本' : '' }}</span>
    </button>
    <span class="name num">{{ file.name }}</span>

    <span class="state">
      <span v-if="current" class="s"><i class="dot done" />当前默认</span>
      <span v-if="playing" class="s"><i class="dot work" />播放中</span>
    </span>

    <span class="size num">{{ formatSize(file.size) }}</span>

    <span class="act">
      <UButton
        v-if="!current"
        color="neutral"
        variant="outline"
        size="sm"
        :loading="loading"
        :disabled="disabled"
        @click="emit('play', file.name)"
      >
        设为当前全屏动画
      </UButton>
      <span v-else class="enabled">已启用</span>
    </span>
  </div>
</template>

<style scoped>
.row {
  display: grid;
  grid-template-columns: 6.6rem minmax(0, 1fr) auto minmax(4.2rem, auto) auto;
  align-items: center;
  gap: 0.6rem 1.2rem;
  padding: 0.72rem 0;
}

/* 15:4 的小屏幕，和顶部孪生屏同一种比例 */
.thumb {
  position: relative;
  width: 6.6rem;
  aspect-ratio: 480 / 128;
  overflow: hidden;
  background: #04060a;
  border: 1px solid var(--ui-border);
  border-radius: 2px;
  transition: border-color 0.15s ease;
}
.thumb img { width: 100%; height: 100%; object-fit: fill; transform: rotate(180deg); display: block; }
.thumb:hover:not(:disabled), .thumb.on { border-color: var(--ui-text-highlighted); }
.thumb:disabled { cursor: default; }
.none { position: absolute; inset: 0; display: grid; place-items: center; font-size: 0.66rem; color: var(--ui-text-dimmed); }

.name {
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
  font-size: 1.05rem;
  font-weight: 600;
  letter-spacing: 0.01em;
  color: var(--ui-text-highlighted);
}

.state { display: flex; align-items: center; gap: 0.85rem; }
.s {
  display: inline-flex;
  align-items: center;
  gap: 0.32rem;
  font-size: 0.76rem;
  color: var(--ui-text-muted);
  white-space: nowrap;
}
.dot {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: var(--ui-border-accented);
}
.dot.done { background: var(--color-state-done); box-shadow: 0 0 0.5rem var(--color-state-done); }
.dot.work { background: var(--color-state-working); box-shadow: 0 0 0.5rem var(--color-state-working); }

.size {
  text-align: right;
  font-size: 0.95rem;
  color: var(--ui-text-toned);
  white-space: nowrap;
}

.act { display: flex; justify-content: flex-end; align-items: center; }
.enabled { font-size: 0.78rem; color: var(--ui-text-dimmed); }
</style>
