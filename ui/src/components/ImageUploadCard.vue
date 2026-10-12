<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import type { UploadFit, UploadMode } from '../bridge/types'
import { useDeviceStore } from '../stores/device'
import Plate from './Plate.vue'
import Notice from './Notice.vue'
import Segmented from './Segmented.vue'

// 前端不接触文件内容：pickUploadFile 由 C# 弹系统文件框并只返回一次性 token，
// startUpload 由 C# 弹原生确认框；真正的校验全部在 C# 侧完成。
interface PickedFile {
  token: string
  fileName: string
  sizeBytes: number
}

const store = useDeviceStore()
const toast = useToast()

const NAME_PATTERN = /^[A-Za-z0-9]{1,8}\.savi$/
const NAME_FORMAT_HINT = '1-8 位字母或数字，加 .savi，例如 img1.savi'
const NAME_IN_USE_HINT = '不能覆盖当前正在使用的文件，请先把默认动画切换到别的文件'

const files = computed(() => store.files.value)
const currentName = computed(() => store.settings.current)
const playingName = computed(() => store.state.playing)
const uploading = computed(() => store.state.uploading)

const picked = ref<PickedFile | null>(null)
const name = ref('')
const fit = ref<UploadFit>('contain')
const mode = ref<UploadMode>('movie')

const picking = ref(false)
const starting = ref(false)

const fitItems: { label: string; value: UploadFit }[] = [
  { label: '保持比例加黑边（contain）', value: 'contain' },
  { label: '裁切填满（cover）', value: 'cover' },
  { label: '拉伸（stretch）', value: 'stretch' }
]

const modeItems: { label: string; value: UploadMode; description: string }[] = [
  {
    label: '作为全屏动画',
    value: 'movie',
    description: '上传完成后立即设为当前全屏动画。'
  },
  {
    label: '作为数据页背景',
    value: 'background',
    description: '作为数据页的背景图使用。'
  }
]

const fitSegItems: { label: string; value: UploadFit }[] = [
  { label: '保持比例', value: 'contain' },
  { label: '裁切', value: 'cover' },
  { label: '拉伸', value: 'stretch' }
]

const modeSegItems: { label: string; value: UploadMode }[] = [
  { label: '全屏动画', value: 'movie' },
  { label: '数据页背景', value: 'background' }
]

const modeHelp: Record<UploadMode, string> = {
  movie: '上传完成后立即设为当前全屏动画。',
  background: '作为数据页的背景图使用。'
}

interface LockReason {
  color: 'warning' | 'error' | 'secondary'
  text: string
}

// 全局禁用（未连接 / 熄灯 / 维护 / 上传中）：卡片内按钮全部禁用并在顶部说明原因。
const lockReason = computed<LockReason | null>(() => {
  if (store.state.uploading) {
    return { color: 'warning', text: '正在上传，上传期间输入与其它操作已锁定。' }
  }
  if (!store.state.connected) {
    return {
      color: 'error',
      text: '屏幕未连接，暂时无法上传，请检查 USB 连接与 COM 口。'
    }
  }
  if (store.state.maintenance) {
    return { color: 'warning', text: '屏幕正在维护中，暂时无法上传。' }
  }
  if (store.state.nightMode) {
    return { color: 'secondary', text: '夜间模式（熄灯）已开启，屏幕不响应操作。' }
  }
  return null
})

const controlsDisabled = computed(() => lockReason.value !== null)

const nameFormatInvalid = computed(() => !NAME_PATTERN.test(name.value))
const nameExists = computed(
  () => !nameFormatInvalid.value && files.value.some((f) => f.name === name.value)
)
const nameInUse = computed(
  () =>
    !nameFormatInvalid.value &&
    (name.value === currentName.value || name.value === playingName.value)
)

// 输入框红色提示：优先提示「正在使用」，其次提示格式错误。
const nameError = computed<string | undefined>(() => {
  if (nameInUse.value) return NAME_IN_USE_HINT
  if (nameFormatInvalid.value) return NAME_FORMAT_HINT
  return undefined
})

const canStart = computed(
  () =>
    picked.value !== null &&
    !starting.value &&
    !controlsDisabled.value &&
    !nameFormatInvalid.value &&
    !nameInUse.value
)

const spaceText = computed(() => {
  if (!store.space.known) return '屏幕剩余空间：未知'
  return `屏幕剩余空间：${formatSize(store.space.free)} / 共 ${formatSize(store.space.total)}`
})

function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '—'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

function reset(): void {
  picked.value = null
  name.value = ''
  fit.value = 'contain'
  mode.value = 'movie'
}

async function refreshSpace(): Promise<void> {
  try {
    await store.refreshSpace()
  } catch (error) {
    console.error('[upload] 读取剩余空间失败', error)
  }
}

async function pick(): Promise<void> {
  if (picking.value || controlsDisabled.value) return
  picking.value = true
  try {
    const data = await store.pickUploadFile()
    if (data.cancelled) return
    picked.value = {
      token: data.token,
      fileName: data.fileName,
      sizeBytes: data.sizeBytes
    }
    name.value = data.suggestedName
    await refreshSpace()
  } catch (error) {
    toast.add({
      title: '选择文件失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    picking.value = false
  }
}

async function start(): Promise<void> {
  if (!canStart.value || !picked.value) return
  starting.value = true
  try {
    const result = await store.startUpload({
      token: picked.value.token,
      name: name.value,
      fit: fit.value,
      mode: mode.value
    })
    if (!result.started) {
      // 用户在原生确认框点了取消：保持当前已选状态，什么都不改。
      toast.add({
        title: '已取消上传',
        description: '未开始上传，可修改后重试。',
        color: 'neutral'
      })
      return
    }
    // started:true → 进入上传中，进度由 store.state.uploading / store.upload 驱动。
  } catch (error) {
    // C# 以 ok:false 拒绝（token 失效、挂件占用等）：token 多半已作废，回到初始状态让用户重选。
    toast.add({
      title: '上传被拒绝',
      description: `${errorMessage(error)}。请重新选择文件。`,
      color: 'error'
    })
    reset()
  } finally {
    starting.value = false
  }
}

// 上传结束：成功则提示并复位；失败后 token 已作废，必须提示重新选择并回到初始状态。
watch(
  () => store.upload.lastDone,
  (done) => {
    if (!done) return
    if (done.ok) {
      toast.add({ title: '上传完成', description: done.message, color: 'success' })
    } else {
      toast.add({
        title: '上传失败',
        description: `${done.message}。文件令牌已作废，请重新选择文件。`,
        color: 'error'
      })
    }
    reset()
  }
)

onMounted(() => {
  void refreshSpace()
})
</script>

<template>
  <Plate title="上传新图片 / 动画" hint="由系统文件框选择，C# 负责校验与二次确认。">
    <template #aside>
      <span v-if="uploading" class="live"><i class="dot work" />上传中</span>
    </template>

    <Notice
      v-if="lockReason"
      :color="lockReason.color"
      title="当前无法上传"
      :description="lockReason.text"
    />

    <Notice
      color="warning"
      title="上传不可取消，请谨慎操作"
      description="上传一旦开始就不能取消。期间请勿关闭程序、休眠或断开电源；若中途失败，可能需要整机断电 30 秒才能恢复屏幕。开始上传时系统会再弹一次原生确认框。"
    />

    <template v-if="uploading">
      <div class="prog">
        <div class="top">
          <span class="lbl">上传中，请勿关闭程序</span>
          <output class="num">{{ store.upload.percent }}<small>%</small></output>
        </div>
        <div class="track"><b :style="{ width: store.upload.percent + '%' }" /></div>
      </div>
    </template>

    <template v-else-if="!picked">
      <p class="hint">点击下方按钮，由系统弹出文件选择框（C# 负责，前端只拿到一次性令牌）。</p>
      <UButton
        block
        color="primary"
        :loading="picking"
        :disabled="controlsDisabled || picking"
        @click="pick"
      >
        选择文件…
      </UButton>
    </template>

    <template v-else>
      <div class="picked">
        <div class="info">
          <span class="fn num">{{ picked.fileName }}</span>
          <span class="fs num">{{ formatSize(picked.sizeBytes) }}</span>
        </div>
        <UButton
          size="xs"
          color="neutral"
          variant="outline"
          :disabled="controlsDisabled || picking"
          :loading="picking"
          @click="pick"
        >
          重新选择
        </UButton>
      </div>

      <UFormField
        class="field"
        label="屏幕文件名"
        :error="nameError"
        :help="`只能用 ${NAME_FORMAT_HINT}`"
      >
        <UInput
          v-model="name"
          placeholder="例如 img1.savi"
          :disabled="controlsDisabled"
          class="w-full"
        />
      </UFormField>

      <Notice
        v-if="nameExists && !nameInUse"
        color="warning"
        title="同名文件已存在"
        description="上传会覆盖屏幕上的同名文件。"
      />

      <div class="field">
        <p class="flabel">适配方式</p>
        <Segmented v-model="fit" :items="fitSegItems" :disabled="controlsDisabled" />
        <p class="fhelp">屏幕比例与源文件不一致时如何处理。</p>
      </div>

      <div class="field">
        <p class="flabel">用途</p>
        <Segmented v-model="mode" :items="modeSegItems" :disabled="controlsDisabled" />
        <p class="fhelp">{{ modeHelp[mode] }}</p>
      </div>

      <p class="space">
        屏幕剩余空间
        <template v-if="store.space.known">
          <span class="num">{{ formatSize(store.space.free) }}</span> / 共
          <span class="num">{{ formatSize(store.space.total) }}</span>
        </template>
        <template v-else>：未知</template>
      </p>

      <UButton class="submit" block color="primary" :loading="starting" :disabled="!canStart" @click="start">
        开始上传
      </UButton>
    </template>
  </Plate>
</template>

<style scoped>
.live {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.78rem;
  color: var(--ui-text-muted);
  white-space: nowrap;
}
.dot {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
}
.dot.work { background: var(--color-state-working); box-shadow: 0 0 0.5rem var(--color-state-working); }

.hint { font-size: 0.86rem; color: var(--ui-text-muted); margin-bottom: 1rem; max-width: 60ch; }

.picked {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding-bottom: 0.9rem;
  border-bottom: 1px solid var(--ui-border);
}
.info { display: flex; align-items: baseline; gap: 0.9rem; min-width: 0; }
.fn {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--ui-text-highlighted);
}
.fs { flex: none; font-size: 1rem; color: var(--ui-text-toned); }

.field { margin-top: 1.6rem; }
.flabel { margin-bottom: 0.55rem; font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.fhelp { margin-top: 0.55rem; font-size: 0.76rem; color: var(--ui-text-muted); }

.space { margin-top: 1.7rem; font-size: 0.86rem; color: var(--ui-text-muted); }
.space .num { font-size: 1rem; color: var(--ui-text-toned); }

.submit { margin-top: 1.7rem; }

.prog { margin-top: 0.1rem; }
.prog .top { display: flex; align-items: baseline; justify-content: space-between; gap: 1rem; }
.prog .lbl { font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.prog output { font-size: 2.4rem; font-weight: 600; line-height: 1; color: var(--ui-text-highlighted); }
.prog output small { margin-left: 0.1rem; font-size: 1rem; color: var(--ui-text-muted); }
.track { margin-top: 0.7rem; height: 3px; background: var(--ui-border); }
.track b {
  display: block;
  height: 100%;
  background: var(--color-state-waiting);
  transition: width 0.25s linear;
}
</style>
