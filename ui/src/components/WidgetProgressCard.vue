<script setup lang="ts">
import { computed, ref } from 'vue'
import { bridge } from '../bridge'
import Plate from './Plate.vue'
import Fader from './Fader.vue'

const props = defineProps<{
  disabled: boolean
}>()

const emit = defineEmits<{
  (e: 'sent'): void
}>()

const toast = useToast()

const MAX_TITLE = 24
// 原生文字只支持 ASCII 可打印字符（0x20-0x7E）
const ASCII_PRINTABLE = /^[\x20-\x7e]*$/

const title = ref('')
const percent = ref(50)
const sending = ref(false)

const titleError = computed<string | undefined>(() => {
  if (!ASCII_PRINTABLE.test(title.value)) {
    return '原生文字只支持英文、数字和 ASCII 符号'
  }
  return undefined
})

const canSend = computed(
  () => !props.disabled && !sending.value && titleError.value === undefined
)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

async function send(): Promise<void> {
  if (!canSend.value) return
  sending.value = true
  try {
    const result = await bridge.call('sendWidget', {
      action: 'progress',
      percent: percent.value,
      title: title.value
    })
    toast.add({
      title: '已发送进度',
      description: result.message,
      color: 'success'
    })
    emit('sent')
  } catch (error) {
    toast.add({
      title: '发送进度失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    sending.value = false
  }
}
</script>

<template>
  <Plate title="进度" hint="把推子拉到目标位置后发送，会临时覆盖屏幕显示。">
    <template #aside>
      <span class="count num">{{ title.length }}/{{ MAX_TITLE }}</span>
    </template>

    <UFormField
      label="进度标题"
      :error="titleError"
      help="英文、数字和 ASCII 符号，最多 24 个字符。"
    >
      <UInput
        v-model="title"
        :maxlength="MAX_TITLE"
        placeholder="例如 Building"
        :disabled="disabled"
        class="w-full"
      />
    </UFormField>

    <Fader
      v-model="percent"
      class="fader-gap"
      label="进度值"
      :min="0"
      :max="100"
      suffix="%"
      :disabled="disabled"
    />

    <UButton
      color="primary"
      variant="solid"
      class="send"
      :loading="sending"
      :disabled="!canSend"
      @click="send"
    >
      显示进度
    </UButton>
  </Plate>
</template>

<style scoped>
.count { font-size: 0.72rem; color: var(--ui-text-dimmed); }
.fader-gap { margin-top: 1.6rem; }
.send { margin-top: 1.6rem; }
</style>
