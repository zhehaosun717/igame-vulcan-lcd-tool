<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { bridge, mockControl } from '../bridge'
import type { MockSnapshot } from '../bridge'
import type { DeviceState } from '../bridge/types'

const snap = ref<MockSnapshot>(mockControl.getSnapshot())

let off: (() => void) | null = null

onMounted(() => {
  // mock 模式下 bridge.on 会转发到 mock 事件总线，state 变化即刷新快照
  off = bridge.on('state', () => {
    snap.value = mockControl.getSnapshot()
  })
})

onBeforeUnmount(() => {
  off?.()
})

const linkOrder: DeviceState['linkState'][] = [
  'idle',
  'working',
  'waiting',
  'done'
]

function cycleLink(): void {
  const index = linkOrder.indexOf(snap.value.linkState)
  mockControl.setLinkState(linkOrder[(index + 1) % linkOrder.length]!)
}
</script>

<template>
  <div
    class="fixed bottom-4 right-4 z-50 w-60 rounded-lg border border-default bg-elevated/95 p-3 text-xs shadow-lg backdrop-blur"
  >
    <div class="mb-2 flex items-center justify-between">
      <span class="font-semibold">Mock 调试</span>
      <UBadge
        :color="snap.connected ? 'success' : 'error'"
        variant="subtle"
      >
        {{ snap.connected ? '已连接' : '未连接' }}
      </UBadge>
    </div>

    <div class="space-y-2">
      <div class="flex items-center justify-between gap-2">
        <span class="text-muted">连接</span>
        <UButton
          size="xs"
          :color="snap.connected ? 'neutral' : 'primary'"
          variant="soft"
          @click="mockControl.setConnected(!snap.connected)"
        >
          {{ snap.connected ? '断开' : '连接' }}
        </UButton>
      </div>

      <div class="flex items-center justify-between gap-2">
        <span class="text-muted">上传</span>
        <UButton
          size="xs"
          :color="snap.uploading ? 'warning' : 'neutral'"
          variant="soft"
          :disabled="!snap.connected"
          @click="mockControl.setUploading(!snap.uploading)"
        >
          {{ snap.uploading ? '停止' : '开始' }}
        </UButton>
      </div>

      <div class="flex items-center justify-between gap-2">
        <span class="text-muted">夜间</span>
        <UButton
          size="xs"
          :color="snap.nightMode ? 'secondary' : 'neutral'"
          variant="soft"
          @click="mockControl.setNightMode(!snap.nightMode)"
        >
          {{ snap.nightMode ? '关闭' : '开启' }}
        </UButton>
      </div>

      <div class="flex items-center justify-between gap-2">
        <span class="text-muted">联动</span>
        <UButton size="xs" color="neutral" variant="soft" @click="cycleLink">
          {{ snap.linkState }}
        </UButton>
      </div>
    </div>
  </div>
</template>
