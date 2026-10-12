<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { bridge } from '../bridge'
import type { LiveInfo } from '../bridge/types'
import { useDeviceStore } from '../stores/device'
import LiveStatusCard from '../components/LiveStatusCard.vue'
import PageHead from '../components/PageHead.vue'
import Plate from '../components/Plate.vue'
import Notice from '../components/Notice.vue'
import Rocker from '../components/Rocker.vue'

const store = useDeviceStore()
const toast = useToast()

const info = ref<LiveInfo>({ on: false, text: '', autostart: false })
const liveOn = ref(false)
const autostart = ref(false)

const liveBusy = ref(false)
const autoBusy = ref(false)

let syncing = false
let pollTimer: ReturnType<typeof setInterval> | null = null

// setLive 会写屏幕，受写守卫限制；setAutostart 只写注册表，任何状态都可改。
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
      title: '正在上传，暂时无法开启实时刷新',
      description: '上传期间屏幕写操作会被 C# 拒绝，请等待上传完成。开机自启不受影响。'
    }
  }
  if (!store.state.connected) {
    return {
      color: 'error',
      title: '屏幕未连接',
      description: '请检查 USB 连接与 COM 口后再开启实时刷新。开机自启不受影响。'
    }
  }
  if (store.state.maintenance) {
    return {
      color: 'warning',
      title: '维护模式中，暂时无法开启实时刷新',
      description: '维护期间屏幕不接受写操作。开机自启不受影响。'
    }
  }
  if (store.state.nightMode) {
    return {
      color: 'secondary',
      title: '夜间熄灯模式中，暂时无法开启实时刷新',
      description: '退出夜间模式后可开启实时刷新。开机自启不受影响。'
    }
  }
  return null
})

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

function apply(next: LiveInfo): void {
  info.value = next
  syncing = true
  if (!liveBusy.value) liveOn.value = next.on
  if (!autoBusy.value) autostart.value = next.autostart
  void nextTick(() => {
    syncing = false
  })
}

async function reload(): Promise<void> {
  try {
    apply(await bridge.call('getLive'))
  } catch (error) {
    console.error('[live] 读取实时数据状态失败', error)
  }
}

async function writeLive(): Promise<void> {
  if (liveBusy.value) return
  liveBusy.value = true
  try {
    await bridge.call('setLive', { on: liveOn.value })
  } catch (error) {
    toast.add({
      title: '切换实时刷新失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    liveBusy.value = false
    // 以 C# 实际生效的值回填控件
    await reload()
  }
}

async function writeAutostart(): Promise<void> {
  if (autoBusy.value) return
  autoBusy.value = true
  try {
    await bridge.call('setAutostart', { on: autostart.value })
  } catch (error) {
    toast.add({
      title: '设置开机自启失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    autoBusy.value = false
    await reload()
  }
}

watch(liveOn, () => {
  if (syncing) return
  void writeLive()
})

watch(autostart, () => {
  if (syncing) return
  void writeAutostart()
})

onMounted(() => {
  void reload()
  pollTimer = setInterval(() => {
    if (document.hidden) return
    if (liveBusy.value || autoBusy.value) return
    void reload()
  }, 2000)
})

onUnmounted(() => {
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
</script>

<template>
  <div class="page">
    <PageHead title="实时数据" hint="把 CPU 和内存数据推送到屏幕，轮播在数据页之间。" />

    <Notice
      v-if="lockReason"
      :color="lockReason.color"
      :title="lockReason.title"
      :description="lockReason.description"
    />

    <Plate title="屏幕刷新">
      <template #aside>
        <span class="tone"><i class="dot" :class="liveOn ? 'on' : 'off'" />{{ liveOn ? '已开启' : '已关闭' }}</span>
      </template>

      <div class="rows">
        <div class="row">
          <div class="row-text">
            <p class="row-label">启动屏幕刷新（实验性）</p>
            <p class="row-help">轮播数据页并推送 CPU / 内存数据；关闭后屏幕恢复默认动画。</p>
          </div>
          <Rocker v-model="liveOn" :disabled="locked || liveBusy" />
        </div>

        <div class="row">
          <div class="row-text">
            <p class="row-label">开机自动启动</p>
            <p class="row-help">程序启动后最小化到托盘，并恢复上次的设置。不依赖屏幕状态，随时可改。</p>
          </div>
          <Rocker v-model="autostart" :disabled="autoBusy" />
        </div>
      </div>
    </Plate>

    <LiveStatusCard :active="liveOn" :text="info.text" />

    <Plate title="说明">
      <div class="notes">
        <p>关闭窗口时，如果开着实时数据，程序会留在右下角托盘继续刷新屏幕；右键托盘图标选“退出”才会真正关闭。</p>
        <p>CPU 温度来自 HWiNFO 的共享内存（HWiNFO 设置里要打开 Shared Memory Support），没开就显示 0。CPU 占用和内存不依赖任何软件。</p>
      </div>
    </Plate>
  </div>
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

/* 开关排成对齐的表单行 */
.rows .row { display: flex; align-items: center; justify-content: space-between; gap: 2rem; }
.rows .row + .row { margin-top: 1.3rem; padding-top: 1.3rem; border-top: 1px solid var(--ui-border-muted); }
.row-label { font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.row-help { margin-top: 0.2rem; font-size: 0.78rem; color: var(--ui-text-muted); max-width: 46ch; line-height: 1.55; }

.notes { display: grid; gap: 0.7rem; }
.notes p { font-size: 0.8rem; color: var(--ui-text-muted); line-height: 1.65; max-width: 60ch; }
</style>
