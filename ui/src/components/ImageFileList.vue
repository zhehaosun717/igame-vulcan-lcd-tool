<script setup lang="ts">
import { ref } from 'vue'
import type { ScreenFile } from '../bridge/types'
import { useDeviceStore } from '../stores/device'
import ImageFileRow from './ImageFileRow.vue'

defineProps<{
  files: ScreenFile[]
  current: string
  playing: string
  disabled: boolean
}>()

const store = useDeviceStore()
const toast = useToast()

const busyName = ref<string | null>(null)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

async function play(name: string): Promise<void> {
  if (busyName.value !== null) return
  busyName.value = name
  try {
    await store.playFile(name)
    toast.add({
      title: '已设为当前动画',
      description: name,
      color: 'success'
    })
  } catch (error) {
    toast.add({
      title: '设置失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    busyName.value = null
  }
}
</script>

<template>
  <div v-if="files.length === 0" class="empty">
    <p class="t">暂无屏幕文件</p>
    <p class="d">连接屏幕后点击右上角「刷新列表」重新获取。</p>
  </div>

  <ul v-else class="spec">
    <li v-for="file in files" :key="file.name">
      <ImageFileRow
        :file="file"
        :current="file.name === current"
        :playing="file.name === playing"
        :loading="busyName === file.name"
        :disabled="disabled || busyName !== null"
        @play="play"
      />
    </li>
  </ul>
</template>

<style scoped>
.empty { padding: 0.35rem 0 0.15rem; }
.empty .t { font-size: 0.92rem; font-weight: 600; color: var(--ui-text-highlighted); }
.empty .d { margin-top: 0.35rem; font-size: 0.8rem; color: var(--ui-text-muted); }

.spec { display: block; }
.spec > li + li { border-top: 1px solid var(--ui-border-muted); }
</style>
