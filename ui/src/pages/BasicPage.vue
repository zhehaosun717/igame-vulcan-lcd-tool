<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, reactive, ref, watch } from 'vue'
import type { SetBasicArgs } from '../bridge/types'
import { useDeviceStore } from '../stores/device'
import PageHead from '../components/PageHead.vue'
import Plate from '../components/Plate.vue'
import Notice from '../components/Notice.vue'
import Fader from '../components/Fader.vue'
import Rocker from '../components/Rocker.vue'
import Segmented from '../components/Segmented.vue'

const store = useDeviceStore()
const toast = useToast()

const form = reactive({
  brightness: store.settings.brightness,
  opacity: store.settings.opacity,
  dir: store.settings.dir,
  enabled: store.settings.enabled
})

const dirItems = [
  { label: '方向 0', value: 0 },
  { label: '方向 1', value: 1 },
  { label: '方向 2', value: 2 },
  { label: '方向 3', value: 3 }
]

const locked = computed(
  () =>
    store.state.uploading ||
    !store.state.connected ||
    store.state.nightMode ||
    store.state.maintenance
)

interface LockReason {
  color: 'warning' | 'error' | 'secondary'
  title: string
  description: string
}

const lockReason = computed<LockReason | null>(() => {
  if (store.state.uploading) {
    return {
      color: 'warning',
      title: '正在上传，暂时无法修改设置',
      description: '上传期间屏幕设置会被 C# 拒绝，请等待上传完成。'
    }
  }
  if (!store.state.connected) {
    return {
      color: 'error',
      title: '屏幕未连接',
      description: '请检查 USB 连接与 COM 口后再修改设置。'
    }
  }
  if (store.state.maintenance) {
    return {
      color: 'warning',
      title: '维护模式中，暂时无法修改设置',
      description: '维护期间屏幕不接受写操作。'
    }
  }
  if (store.state.nightMode) {
    return {
      color: 'secondary',
      title: '夜间熄灯模式中，暂时无法修改设置',
      description: '请点击上方的“恢复灯光和屏幕”按钮退出熄灯后再修改设置。'
    }
  }
  return null
})

const applying = ref(false)

// 熄灯 / 恢复：熄灯状态下这是唯一的恢复入口，只在正在上传或未连接时禁用。
const nightBusy = ref(false)
const nightButtonDisabled = computed(
  () => store.state.uploading || !store.state.connected
)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

async function toggleNight(): Promise<void> {
  if (nightButtonDisabled.value) return
  nightBusy.value = true
  try {
    const message = await store.setNight(!store.state.nightMode)
    toast.add({ title: message, color: 'success' })
  } catch (error) {
    toast.add({
      title: '操作失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    nightBusy.value = false
  }
}

let syncing = false
let timer: ReturnType<typeof setTimeout> | null = null

function syncFromStore(): void {
  syncing = true
  form.brightness = store.settings.brightness
  form.opacity = store.settings.opacity
  form.dir = store.settings.dir
  form.enabled = store.settings.enabled
  void nextTick(() => {
    syncing = false
  })
}

function clearTimer(): void {
  if (timer !== null) {
    clearTimeout(timer)
    timer = null
  }
}

async function flush(): Promise<void> {
  clearTimer()
  if (locked.value) {
    syncFromStore()
    return
  }

  const args: SetBasicArgs = {
    brightness: form.brightness,
    opacity: form.opacity,
    dir: form.dir,
    enabled: form.enabled
  }

  applying.value = true
  try {
    await store.setBasic(args)
  } catch (error) {
    toast.add({
      title: '设置失败',
      description: error instanceof Error ? error.message : String(error),
      color: 'error'
    })
    syncFromStore()
  } finally {
    applying.value = false
  }
}

function scheduleWrite(): void {
  clearTimer()
  timer = setTimeout(() => {
    timer = null
    void flush()
  }, 300)
}

function commitNow(): void {
  clearTimer()
  void flush()
}

// 亮度 / 不透明度：拖动后防抖 0.3 秒生效
watch([() => form.brightness, () => form.opacity], () => {
  if (syncing) return
  scheduleWrite()
})

// 启用开关 / 显示方向：立即生效
watch([() => form.dir, () => form.enabled], () => {
  if (syncing) return
  commitNow()
})

// store.settings 变化（含 C# 主动推送）→ 同步到控件，避免循环写入
watch(
  () => [
    store.settings.brightness,
    store.settings.opacity,
    store.settings.dir,
    store.settings.enabled
  ],
  () => {
    syncFromStore()
  }
)

onBeforeUnmount(clearTimer)
</script>

<template>
  <div class="page">
    <PageHead title="基本" hint="亮度、背景和方向。拖动推子后约 0.3 秒自动生效。" />

    <Plate title="灯光与屏幕" hint="一键熄灭屏幕与灯光。熄灯后，只有这个按钮能恢复。">
      <template #aside>
        <UButton
          :color="store.state.nightMode ? 'primary' : 'neutral'"
          :variant="store.state.nightMode ? 'solid' : 'outline'"
          :loading="nightBusy"
          :disabled="nightButtonDisabled"
          @click="toggleNight"
        >
          {{ store.state.nightMode ? '恢复灯光和屏幕' : '一键熄灯' }}
        </UButton>
      </template>
      <Notice
        v-if="lockReason"
        :color="lockReason.color"
        :title="lockReason.title"
        :description="lockReason.description"
      />
    </Plate>

    <Plate title="屏幕" hint="关闭后停止播放并熄灭背光，实际效果以屏幕为准。">
      <template #aside>
        <span v-if="applying" class="text-xs text-muted">应用中…</span>
        <Rocker v-model="form.enabled" :disabled="locked" />
      </template>

      <Fader v-model="form.brightness" label="亮度" :min="0" :max="10" :disabled="locked" help="0 最暗，10 最亮。" />
      <Fader v-model="form.opacity" label="背景不透明度" :min="0" :max="100" suffix="%" :disabled="locked" help="0 全透明，100 完全不透明。" />

      <div class="dir">
        <p class="dir-label">显示方向</p>
        <Segmented v-model="form.dir" :items="dirItems" :disabled="locked" />
        <p class="dir-help">数值含义以屏幕实际效果为准。</p>
      </div>
    </Plate>

    <Plate title="此刻" hint="屏幕正在播放的内容，以及开机或空闲时回到的默认动画。">
      <dl class="now">
        <div><dt>正在播放</dt><dd class="num">{{ store.state.playing || '—' }}</dd></div>
        <div><dt>默认动画</dt><dd class="num">{{ store.settings.current || '—' }}</dd></div>
      </dl>
    </Plate>
  </div>
</template>

<style scoped>
.dir { margin-top: 2.1rem; }
.dir-label { margin-bottom: 0.55rem; font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.dir-help { margin-top: 0.55rem; font-size: 0.76rem; color: var(--ui-text-muted); }
.now { display: flex; gap: 3rem; }
.now dt { font-size: 0.74rem; color: var(--ui-text-dimmed); }
.now dd { margin-top: 0.15rem; font-size: 1.5rem; font-weight: 600; color: var(--ui-text-highlighted); }
</style>
