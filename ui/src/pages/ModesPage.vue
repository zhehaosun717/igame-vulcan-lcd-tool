<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import type { ModesInfo, SetModesArgs } from '../bridge/types'
import { bridge } from '../bridge'
import { useDeviceStore } from '../stores/device'

const store = useDeviceStore()
const toast = useToast()

const available = ref<string[]>([])
const canUseMovie = ref(true)
const loading = ref(false)
const applying = ref(false)

const form = reactive({
  selected: [] as number[],
  interval: '10',
  auto: true
})

interface LockReason {
  color: 'warning' | 'error' | 'secondary'
  title: string
  description: string
}

const lockReason = computed<LockReason | null>(() => {
  if (store.state.uploading) {
    return {
      color: 'warning',
      title: '正在上传，暂时无法修改轮播设置',
      description: '上传期间屏幕设置会被 C# 拒绝，请等待上传完成。'
    }
  }
  if (!store.state.connected) {
    return {
      color: 'error',
      title: '屏幕未连接',
      description: '请检查 USB 连接与 COM 口后再修改轮播设置。'
    }
  }
  if (store.state.maintenance) {
    return {
      color: 'warning',
      title: '维护模式中，暂时无法修改轮播设置',
      description: '维护期间屏幕不接受写操作。'
    }
  }
  if (store.state.nightMode) {
    return {
      color: 'secondary',
      title: '夜间熄灯模式中，暂时无法修改轮播设置',
      description: '请先在“基本”页点击“恢复灯光和屏幕”后再修改轮播设置。'
    }
  }
  return null
})

const isLocked = computed(() => lockReason.value !== null)

const intervalNumber = computed(() => Number(form.interval))

const intervalValid = computed(
  () =>
    Number.isInteger(intervalNumber.value) &&
    intervalNumber.value >= 1 &&
    intervalNumber.value <= 120
)

// 极小适配：Fader 需要数字模型，读写沿用现有的 form.interval 字符串。
const intervalModel = computed({
  get: () => intervalNumber.value,
  set: (value: number) => {
    form.interval = String(value)
  }
})

const applyDisabled = computed(
  () => isLocked.value || form.selected.length === 0 || !intervalValid.value
)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

function toggle(index: number): void {
  if (isLocked.value) return
  if (index === 0 && !canUseMovie.value) return
  const pos = form.selected.indexOf(index)
  if (pos >= 0) form.selected.splice(pos, 1)
  else form.selected.push(index)
  form.selected.sort((a, b) => a - b)
}

async function load(): Promise<void> {
  loading.value = true
  try {
    const info: ModesInfo = await bridge.call('getModes')
    available.value = info.available
    canUseMovie.value = info.canUseMovie
    form.selected = info.selected.filter((i) => i !== 0 || info.canUseMovie)
    form.interval = String(info.interval)
    form.auto = info.auto
  } catch (error) {
    toast.add({
      title: '读取轮播配置失败',
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
  const args: SetModesArgs = {
    selected: [...form.selected],
    interval: intervalNumber.value,
    auto: form.auto
  }
  try {
    await bridge.call('setModes', args)
    toast.add({ title: '轮播设置已应用', color: 'success' })
    await load()
  } catch (error) {
    // 被 C# 拒绝：把控件回滚到服务端当前值，并弹出错误。
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
    <PageHead
      title="轮播页面"
      hint="勾选要在屏幕上轮流显示的数据页，并设置每页停留时间与是否自动轮播。"
    />

    <Notice
      v-if="lockReason"
      :color="lockReason.color"
      :title="lockReason.title"
      :description="lockReason.description"
    />

    <Plate title="轮播页面" hint="下标 0 的“自定义动画 / 图片”需要屏幕上已有可播放的当前动画。">
      <template #aside>
        <UBadge color="neutral" variant="outline">
          已选 <span class="num">{{ form.selected.length }}</span> /
          <span class="num">{{ available.length }}</span>
        </UBadge>
      </template>

      <div v-if="loading" class="loading">正在读取…</div>

      <template v-else>
        <div class="modes-list">
          <UCheckbox
            v-for="(name, i) in available"
            :key="i"
            :label="name"
            :model-value="form.selected.includes(i)"
            :disabled="isLocked || (i === 0 && !canUseMovie)"
            @update:model-value="() => toggle(i)"
          />
        </div>

        <Notice
          v-if="!canUseMovie"
          color="warning"
          title="“自定义动画 / 图片”暂不可选"
          description="屏幕上还没有可播放的动画，请先在“图片 / 动画”页上传并设为当前。"
        />

        <p v-if="form.selected.length === 0" class="pick-empty">至少需要勾选一项。</p>

        <div class="modes-controls">
          <div>
            <Fader
              v-model="intervalModel"
              label="每页停留"
              :min="1"
              :max="120"
              suffix=" 秒"
              :disabled="isLocked"
              help="范围 1 - 120 秒，修改后点击“应用”生效。"
            />
            <p v-if="!intervalValid" class="field-error">请输入 1 - 120 的整数</p>
          </div>

          <div class="auto">
            <p class="field-label">自动轮播</p>
            <Rocker v-model="form.auto" :disabled="isLocked" />
            <p class="field-help">关闭后仅按屏幕自身行为切换。</p>
          </div>
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

      <template #footer>
        <p class="tip">
          CPU / 内存页的数据需要在“实时数据”里打开推送；GPU 页由显卡库自己更新。
        </p>
      </template>
    </Plate>
  </div>
</template>

<style scoped>
.loading { font-size: 0.86rem; color: var(--ui-text-muted); }

/* 复选清单：两列整齐排布，选中项文字反白，未选与禁用退到灰色 */
.modes-list { display: grid; grid-template-columns: 1fr; gap: 0.7rem 2.5rem; }
@media (min-width: 560px) { .modes-list { grid-template-columns: 1fr 1fr; } }
.modes-list :deep([data-slot='label']) { color: var(--ui-text-muted); font-weight: 500; }
.modes-list :deep([data-slot='root']:has([data-state='checked']) [data-slot='label']) {
  color: var(--ui-text-highlighted);
  font-weight: 600;
}
.modes-list :deep([data-slot='root']:has([data-disabled])) { opacity: 0.5; }
.modes-list :deep([data-slot='root']:has([data-disabled]) [data-slot='label']) {
  color: var(--ui-text-dimmed);
}

.pick-empty { margin-top: 1.1rem; font-size: 0.82rem; color: var(--color-state-error); }

.modes-controls { display: grid; gap: 2rem; margin-top: 2.25rem; }
@media (min-width: 620px) {
  .modes-controls { grid-template-columns: minmax(0, 1fr) auto; gap: 3.5rem; align-items: start; }
}
.auto { display: flex; flex-direction: column; align-items: flex-start; }
.field-label { font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.field-help { margin-top: 0.55rem; font-size: 0.76rem; color: var(--ui-text-muted); }
.field-error { margin-top: 0.6rem; font-size: 0.76rem; color: var(--color-state-error); }

.actions { display: flex; align-items: center; gap: 1rem; margin-top: 2.25rem; }
.applying { font-size: 0.76rem; color: var(--ui-text-muted); }
.tip { font-size: 0.76rem; color: var(--ui-text-muted); max-width: 60ch; }
</style>
