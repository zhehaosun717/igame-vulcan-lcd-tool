<script setup lang="ts">
// 按 SAVI 的帧时长循环播放一组画面。画面在文件中是倒置的，这里旋转 180° 还原成屏幕上的样子。
// 窗口不可见时暂停；系统开启"减少动态效果"时只显示第一帧。
import { onMounted, onUnmounted, ref, watch } from 'vue'
import type { PreviewFrame } from '../bridge/types'

const props = defineProps<{ frames: PreviewFrame[]; fps: number }>()
const index = ref(0)
let timer: ReturnType<typeof setTimeout> | undefined
const reduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches

function schedule(): void {
  if (timer) clearTimeout(timer)
  timer = undefined
  if (reduced || props.frames.length < 2) return
  const f = props.frames[index.value] ?? props.frames[0]
  const ms = Math.max(16, (f.n * 1000) / Math.max(1, props.fps))
  timer = setTimeout(() => {
    if (!document.hidden) index.value = (index.value + 1) % props.frames.length
    schedule()
  }, ms)
}

// 先把所有画面解码进缓存，避免播放时闪烁
function warm(): void {
  for (const f of props.frames) {
    const img = new Image()
    img.src = f.src
  }
}

watch(
  () => props.frames,
  () => {
    index.value = 0
    warm()
    schedule()
  }
)
onMounted(() => {
  warm()
  schedule()
})
onUnmounted(() => {
  if (timer) clearTimeout(timer)
})
</script>

<template>
  <img class="anim" :src="frames[index]?.src" alt="" draggable="false" />
</template>

<style scoped>
.anim {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: fill;
  transform: rotate(180deg);
  image-rendering: auto;
}
</style>
