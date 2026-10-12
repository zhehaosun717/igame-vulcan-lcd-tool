<script setup lang="ts">
import { computed, ref } from 'vue'
import { bridge } from '../bridge'
import Plate from './Plate.vue'

const props = defineProps<{
  disabled: boolean
}>()

const emit = defineEmits<{
  (e: 'sent'): void
}>()

const toast = useToast()

const MAX_TEXT = 40
const MIN_SECONDS = 1
const MAX_SECONDS = 300
// 原生文字只支持 ASCII 可打印字符（0x20-0x7E）
const ASCII_PRINTABLE = /^[\x20-\x7e]*$/

const text = ref('')
const seconds = ref('10')
const sending = ref(false)

const textError = computed<string | undefined>(() => {
  if (!ASCII_PRINTABLE.test(text.value)) {
    return '原生文字只支持英文、数字和 ASCII 符号'
  }
  return undefined
})

const secondsError = computed<string | undefined>(() => {
  if (seconds.value.trim() === '') return '请输入 1-300 之间的秒数'
  const n = Number(seconds.value)
  if (!Number.isFinite(n) || !Number.isInteger(n) || n < MIN_SECONDS || n > MAX_SECONDS) {
    return '持续秒数必须在 1-300 之间'
  }
  return undefined
})

const canSend = computed(
  () =>
    !props.disabled &&
    !sending.value &&
    text.value.length > 0 &&
    textError.value === undefined &&
    secondsError.value === undefined
)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

async function send(): Promise<void> {
  if (!canSend.value) return
  sending.value = true
  try {
    const result = await bridge.call('sendWidget', {
      action: 'notify',
      seconds: Number(seconds.value),
      text: text.value
    })
    toast.add({
      title: '已发送通知',
      description: result.message,
      color: 'success'
    })
    emit('sent')
  } catch (error) {
    toast.add({
      title: '发送通知失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    sending.value = false
  }
}
</script>

<template>
  <Plate title="通知" hint="在小屏幕上显示一段短文字，到时间后自动消失。">
    <template #aside>
      <span class="count num">{{ text.length }}/{{ MAX_TEXT }}</span>
    </template>

    <UFormField
      label="通知文字"
      :error="textError"
      help="英文、数字和 ASCII 符号，最多 40 个字符。"
    >
      <UInput
        v-model="text"
        :maxlength="MAX_TEXT"
        placeholder="例如 Build finished"
        :disabled="disabled"
        class="w-full"
      />
    </UFormField>

    <div class="field">
      <UFormField
        label="持续秒数"
        :error="secondsError"
        help="1-300 秒，默认 10 秒。"
      >
        <UInput
          v-model="seconds"
          type="number"
          :min="MIN_SECONDS"
          :max="MAX_SECONDS"
          :disabled="disabled"
          class="sec-input"
        />
      </UFormField>
    </div>

    <UButton
      color="primary"
      variant="solid"
      class="send"
      :loading="sending"
      :disabled="!canSend"
      @click="send"
    >
      显示通知
    </UButton>
  </Plate>
</template>

<style scoped>
.count { font-size: 0.72rem; color: var(--ui-text-dimmed); }
.field { margin-top: 1.6rem; }
.sec-input { width: 10rem; }
.sec-input :deep(input) { font-family: var(--font-display); font-variant-numeric: tabular-nums; }
.send { margin-top: 1.6rem; }
</style>
