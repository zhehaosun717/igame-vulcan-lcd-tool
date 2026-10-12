<script setup lang="ts">
import { ref } from 'vue'
import { bridge } from '../bridge'
import Plate from './Plate.vue'
import Notice from './Notice.vue'

const props = defineProps<{
  disabled: boolean
}>()

const toast = useToast()

const confirming = ref(false)
const opening = ref(false)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

function askConfirm(): void {
  if (props.disabled || opening.value) return
  confirming.value = true
}

function cancel(): void {
  if (opening.value) return
  confirming.value = false
}

async function open(): Promise<void> {
  if (props.disabled || opening.value) return
  opening.value = true
  try {
    const result = await bridge.call('openStorageManager')
    if (!result.started) {
      toast.add({
        title: '已取消',
        description: '未进入存储维护模式。',
        color: 'neutral'
      })
      return
    }
    toast.add({
      title: '正在进入存储维护',
      description: '程序将以维护模式重新打开。',
      color: 'success'
    })
  } catch (error) {
    toast.add({
      title: '进入失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    opening.value = false
    confirming.value = false
  }
}
</script>

<template>
  <Plate title="存储维护" hint="进入 USB 存储维护模式，浏览、备份或替换开机动画。">
    <Notice
      color="warning"
      title="进入后程序会退出"
      description="进入存储维护后，屏幕暂停播放和外部指令。程序会退出并以维护模式重新打开。恢复普通控制需要安全弹出存储，再整机断电约 30 秒后开机。"
    />

    <template v-if="!confirming">
      <UButton
        color="error"
        variant="outline"
        :disabled="disabled || opening"
        @click="askConfirm"
      >
        打开存储维护…
      </UButton>
    </template>

    <template v-else>
      <Notice
        color="warning"
        title="确认进入存储维护？"
        description="程序将立即退出并以维护模式重新打开。恢复普通控制需要整机断电约 30 秒。请先保存工作，确认后才会执行。"
      />
      <div class="actions">
        <UButton
          color="error"
          variant="outline"
          :loading="opening"
          :disabled="opening"
          @click="open"
        >
          确认，进入维护模式
        </UButton>
        <UButton
          color="neutral"
          variant="outline"
          :disabled="opening"
          @click="cancel"
        >
          取消
        </UButton>
      </div>
    </template>
  </Plate>
</template>

<style scoped>
.actions { display: flex; flex-wrap: wrap; gap: 0.6rem; }
</style>
