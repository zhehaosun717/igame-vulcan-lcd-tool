<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import type { TextInfo } from '../bridge/types'
import { bridge } from '../bridge'
import { useDeviceStore } from '../stores/device'

const store = useDeviceStore()
const toast = useToast()

const loading = ref(false)
const applying = ref(false)

const form = reactive({
  text: '',
  fontSize: '0',
  color: 'FF0000',
  show: false
})

const MAX_LEN = 64

interface LockReason {
  color: 'warning' | 'error' | 'secondary'
  title: string
  description: string
}

const lockReason = computed<LockReason | null>(() => {
  if (store.state.uploading) {
    return {
      color: 'warning',
      title: '正在上传，暂时无法修改文字',
      description: '上传期间屏幕设置会被 C# 拒绝，请等待上传完成。'
    }
  }
  if (!store.state.connected) {
    return {
      color: 'error',
      title: '屏幕未连接',
      description: '请检查 USB 连接与 COM 口后再修改文字。'
    }
  }
  if (store.state.maintenance) {
    return {
      color: 'warning',
      title: '维护模式中，暂时无法修改文字',
      description: '维护期间屏幕不接受写操作。'
    }
  }
  if (store.state.nightMode) {
    return {
      color: 'secondary',
      title: '夜间熄灯模式中，暂时无法修改文字',
      description: '请先在“基本”页点击“恢复灯光和屏幕”后再修改文字。'
    }
  }
  return null
})

const isLocked = computed(() => lockReason.value !== null)

// 只允许 ASCII 可打印字符（空格 - 波浪号）。
const hasNonAscii = computed(() => !/^[\x20-\x7e]*$/.test(form.text))
const tooLong = computed(() => form.text.length > MAX_LEN)

const textError = computed<string | undefined>(() => {
  if (hasNonAscii.value) return '只支持英文、数字和 ASCII 符号'
  if (tooLong.value) return `最多 ${MAX_LEN} 个字符`
  return undefined
})

const fontSizeNumber = computed(() => Number(form.fontSize))
const fontSizeValid = computed(
  () =>
    Number.isInteger(fontSizeNumber.value) &&
    fontSizeNumber.value >= 0 &&
    fontSizeNumber.value <= 9
)

// 极小适配：Fader 需要数字模型，读写沿用现有的 form.fontSize 字符串。
const fontSizeModel = computed({
  get: () => fontSizeNumber.value,
  set: (value: number) => {
    form.fontSize = String(value)
  }
})

const applyDisabled = computed(
  () => isLocked.value || hasNonAscii.value || tooLong.value || !fontSizeValid.value
)

// 原生 <input type="color"> 只产生 #RRGGBB，这里转成 C# 需要的无 # 大写形式。
const colorInput = computed<string>({
  get: () => '#' + form.color,
  set: (value) => {
    form.color = value.replace('#', '').toUpperCase()
  }
})

const previewFontSize = computed(() => 10 + fontSizeNumber.value * 2)
// 预览字号：把 0-9 映射到屏宽百分比（cqw），随 480×128 的预览等比缩放。
const previewSize = computed(() => (2.6 + fontSizeNumber.value * 0.52).toFixed(2) + 'cqw')
const previewColor = computed(() => '#' + form.color)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

async function load(): Promise<void> {
  loading.value = true
  try {
    const info: TextInfo = await bridge.call('getText')
    form.text = info.text
    form.fontSize = String(info.fontSize)
    form.color = info.color.toUpperCase()
    form.show = info.show
  } catch (error) {
    toast.add({
      title: '读取文字配置失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    loading.value = false
  }
}

async function apply(): Promise<void> {
  if (applyDisabled.value) return
  applying.value = true
  try {
    await bridge.call('setText', {
      text: form.text,
      fontSize: fontSizeNumber.value,
      color: form.color.toUpperCase(),
      show: form.show
    })
    toast.add({ title: '文字设置已应用', color: 'success' })
    await load()
  } catch (error) {
    // 被 C# 拒绝：回滚到服务端当前值，并弹出错误。
    toast.add({
      title: '应用失败',
      description: errorMessage(error),
      color: 'error'
    })
    await load()
  } finally {
    applying.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="page">
    <PageHead title="文字" hint="在屏幕上显示一段自定义文字，可设置字号与颜色。" />

    <Notice
      v-if="lockReason"
      :color="lockReason.color"
      :title="lockReason.title"
      :description="lockReason.description"
    />

    <Plate title="预览" hint="480 × 128 的黑屏示意，不是屏幕的实时画面。">
      <div class="screen">
        <span
          class="silk preview-text"
          :class="{ off: !form.show }"
          :style="{ color: previewColor, fontSize: previewSize }"
        >
          {{ form.text || '—' }}
        </span>
        <div class="pixels" aria-hidden="true" />
      </div>
    </Plate>

    <Plate title="自定义文字" hint="最多 64 个字符，只支持英文、数字和 ASCII 符号。">
      <template #aside>
        <span class="count num">{{ form.text.length }} / {{ MAX_LEN }}</span>
      </template>

      <div v-if="loading" class="loading">正在读取…</div>

      <template v-else>
        <UFormField
          label="文字内容"
          :error="textError"
          help="只支持英文、数字和 ASCII 符号（中文会被 C# 拒绝）。"
        >
          <UInput
            v-model="form.text"
            placeholder="例如 RTX 3070 Ti"
            :maxlength="MAX_LEN + 1"
            :disabled="isLocked"
            class="w-full"
          />
        </UFormField>

        <div class="fields">
          <div>
            <Fader
              v-model="fontSizeModel"
              label="字号"
              :min="0"
              :max="9"
              :disabled="isLocked"
              help="0 最小，9 最大。"
            />
            <p v-if="!fontSizeValid" class="field-error">请输入 0 - 9 的整数</p>
          </div>

          <div class="color">
            <p class="field-label">颜色</p>
            <div class="color-row">
              <input
                v-model="colorInput"
                type="color"
                :disabled="isLocked"
                class="color-input"
              />
              <span class="num color-value">#{{ form.color }}</span>
            </div>
            <p class="field-help">点击色块选择颜色。</p>
          </div>
        </div>

        <div class="show-row">
          <div>
            <p class="field-label">在屏幕上显示</p>
            <p class="field-help">关闭后只保存设置，不在屏幕上显示。</p>
          </div>
          <Rocker v-model="form.show" :disabled="isLocked" />
        </div>

        <div class="actions">
          <UButton
            color="primary"
            variant="solid"
            :loading="applying"
            :disabled="applyDisabled"
            @click="apply"
          >
            应用
          </UButton>
          <span v-if="applying" class="applying">应用中…</span>
        </div>
      </template>
    </Plate>
  </div>
</template>

<style scoped>
.loading { font-size: 0.86rem; color: var(--ui-text-muted); }

/* 预览：一小块 480×128 的黑屏，是这一页唯一发光的东西 */
.screen {
  position: relative;
  width: 100%;
  max-width: 30rem;
  aspect-ratio: 480 / 128;
  container-type: inline-size;
  display: grid;
  place-items: center;
  padding: 0 4%;
  overflow: hidden;
  background: #04060a;
  border: 1px solid var(--ui-border);
  border-radius: 3px;
  box-shadow: 0 0 0 1px #000, inset 0 0 38px rgb(0 0 0 / 0.9);
}
.preview-text {
  position: relative;
  z-index: 1;
  line-height: 1.12;
  text-align: center;
  word-break: break-all;
  text-shadow: 0 0 1.2cqw currentColor;
  transition: opacity 0.2s ease;
}
.preview-text.off { opacity: 0.32; }
.pixels {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background:
    repeating-linear-gradient(90deg, rgb(0 0 0 / 0.28) 0 1px, transparent 1px 0.4167%),
    repeating-linear-gradient(0deg, rgb(0 0 0 / 0.28) 0 1px, transparent 1px 0.78125%);
  mix-blend-mode: multiply;
}

.count { font-size: 0.82rem; color: var(--ui-text-muted); }

.fields { display: grid; gap: 2rem; margin-top: 2.25rem; }
@media (min-width: 560px) {
  .fields { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 2.75rem; }
}
.field-label { font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.field-help { margin-top: 0.55rem; font-size: 0.76rem; color: var(--ui-text-muted); }
.field-error { margin-top: 0.6rem; font-size: 0.76rem; color: var(--color-state-error); }

.color-row { display: flex; align-items: center; gap: 0.9rem; margin-top: 0.55rem; }
.color-input {
  width: 3.4rem;
  height: 1.9rem;
  padding: 0.14rem;
  cursor: pointer;
  border: 1px solid var(--ui-border-accented);
  border-radius: 3px;
  background: #0f1215;
}
.color-input:disabled { opacity: 0.4; cursor: not-allowed; }
.color-value { font-size: 1.15rem; font-weight: 600; letter-spacing: 0.04em; color: var(--ui-text-highlighted); }

.show-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1.5rem;
  margin-top: 2.25rem;
  padding-top: 1.5rem;
  border-top: 1px dashed var(--ui-border);
}
.show-row .field-help { margin-top: 0.2rem; }

.actions { display: flex; align-items: center; gap: 1rem; margin-top: 2.25rem; }
.applying { font-size: 0.76rem; color: var(--ui-text-muted); }
</style>
