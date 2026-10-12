<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { bridge } from '../bridge'
import type { WidgetStatus } from '../bridge/types'
import { useDeviceStore } from '../stores/device'
import WidgetProgressCard from '../components/WidgetProgressCard.vue'
import WidgetNotifyCard from '../components/WidgetNotifyCard.vue'
import WidgetStatusCard from '../components/WidgetStatusCard.vue'
import PageHead from '../components/PageHead.vue'
import Notice from '../components/Notice.vue'

const store = useDeviceStore()

const status = ref<WidgetStatus | null>(null)

interface LockReason {
  color: 'warning' | 'error' | 'secondary'
  title: string
  description: string
}

// 写屏幕的命令在 上传中 / 未连接 / 维护 / 熄灯 时会被 C# 拒绝：禁用控件并在顶部说明原因。
const lockReason = computed<LockReason | null>(() => {
  if (store.state.uploading) {
    return {
      color: 'warning',
      title: '正在上传，暂时无法发送挂件',
      description: '上传期间屏幕写操作会被 C# 拒绝，请等待上传完成。'
    }
  }
  if (!store.state.connected) {
    return {
      color: 'error',
      title: '屏幕未连接',
      description: '请检查 USB 连接与 COM 口后再发送进度或通知。'
    }
  }
  if (store.state.maintenance) {
    return {
      color: 'warning',
      title: '维护模式中，暂时无法发送挂件',
      description: '维护期间屏幕不接受写操作。'
    }
  }
  if (store.state.nightMode) {
    return {
      color: 'secondary',
      title: '夜间熄灯模式中，暂时无法发送挂件',
      description: '退出夜间模式后可发送进度或通知。'
    }
  }
  return null
})

const locked = computed(() => lockReason.value !== null)

// 页面可见时每 2 秒读一次挂件状态；切走（组件卸载）时清除定时器，标签页隐藏时跳过请求。
const POLL_INTERVAL_MS = 2000
let pollTimer: ReturnType<typeof setInterval> | null = null

async function refreshStatus(): Promise<void> {
  try {
    status.value = await bridge.call('getWidgetStatus')
  } catch {
    // 读取失败时保留上一次状态，不打断页面
  }
}

function startPolling(): void {
  if (pollTimer !== null) return
  void refreshStatus()
  pollTimer = setInterval(() => {
    if (document.hidden) return
    void refreshStatus()
  }, POLL_INTERVAL_MS)
}

function stopPolling(): void {
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

function onVisibilityChange(): void {
  if (!document.hidden) void refreshStatus()
}

onMounted(() => {
  startPolling()
  document.addEventListener('visibilitychange', onVisibilityChange)
})

onUnmounted(() => {
  stopPolling()
  document.removeEventListener('visibilitychange', onVisibilityChange)
})
</script>

<template>
  <div class="page">
    <PageHead
      title="进度 / 通知"
      hint="向屏幕发送临时进度条或通知文字，临时显示结束后会恢复最新的助手状态。"
    />

    <Notice
      v-if="lockReason"
      :color="lockReason.color"
      :title="lockReason.title"
      :description="lockReason.description"
    />

    <WidgetProgressCard :disabled="locked" @sent="refreshStatus" />
    <WidgetNotifyCard :disabled="locked" @sent="refreshStatus" />
    <WidgetStatusCard :status="status" :disabled="locked" @cleared="refreshStatus" />

    <p class="foot">
      也可以由脚本发送真实进度（GpuLcdSend.exe）。助手事件会继续接收，临时文字结束后恢复最新的助手状态。
    </p>
  </div>
</template>

<style scoped>
.foot { margin-top: 1.6rem; font-size: 0.72rem; color: var(--ui-text-muted); line-height: 1.6; max-width: 60ch; }
</style>
