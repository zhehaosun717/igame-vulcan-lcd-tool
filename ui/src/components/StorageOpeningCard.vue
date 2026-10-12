<script setup lang="ts">
import { ref } from 'vue'
import { bridge } from '../bridge'
import Plate from './Plate.vue'

const props = defineProps<{
  disabled: boolean
}>()

const toast = useToast()

const mode = ref<number | null>(null)
const reading = ref(false)
const previewing = ref(false)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

async function read(): Promise<void> {
  if (props.disabled || reading.value) return
  reading.value = true
  try {
    const result = await bridge.call('readOpening')
    mode.value = result.mode
    toast.add({
      title: '已读取开机方案',
      description: `当前开机方案：${result.mode}`,
      color: 'success'
    })
  } catch (error) {
    toast.add({
      title: '读取失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    reading.value = false
  }
}

async function preview(): Promise<void> {
  if (props.disabled || previewing.value) return
  previewing.value = true
  try {
    const result = await bridge.call('previewOpening')
    if (!result.started) {
      toast.add({
        title: '已取消',
        description: '未在小屏幕播放开机动画。',
        color: 'neutral'
      })
      return
    }
    toast.add({
      title: '已开始播放',
      description: '正在小屏幕播放原开机动画。',
      color: 'success'
    })
  } catch (error) {
    toast.add({
      title: '播放失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    previewing.value = false
  }
}
</script>

<template>
  <Plate title="开机动画" hint="读取或试播屏幕上的原开机动画。">
    <template #aside>
      <div class="readout">
        <span>当前方案</span>
        <strong class="num">{{ mode === null ? '—' : mode }}</strong>
      </div>
    </template>

    <div class="acts">
      <div class="act">
        <UButton
          color="neutral"
          variant="outline"
          :loading="reading"
          :disabled="disabled || reading"
          @click="read"
        >
          读取当前开机方案
        </UButton>
        <p class="help">读取需要访问串口，约 1-2 秒。</p>
      </div>

      <div class="act">
        <UButton
          color="neutral"
          variant="outline"
          :loading="previewing"
          :disabled="disabled || previewing"
          @click="preview"
        >
          在小屏幕播放原开机动画
        </UButton>
        <p class="help">播放前系统会弹出原生确认框，取消则不会播放。</p>
      </div>
    </div>
  </Plate>
</template>

<style scoped>
.readout { display: grid; justify-items: end; gap: 0.1rem; }
.readout span { font-size: 0.7rem; color: var(--ui-text-dimmed); }
.readout strong { font-size: 2rem; font-weight: 600; line-height: 1; color: var(--ui-text-highlighted); }

.acts { display: grid; gap: 1.6rem; }
.act { display: grid; justify-items: start; gap: 0.5rem; }
.help { font-size: 0.76rem; color: var(--ui-text-muted); }
</style>
