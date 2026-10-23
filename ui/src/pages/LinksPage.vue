<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { bridge } from '../bridge'
import { mergeLinks } from '../bridge/links'
import type { LinkDemoState, LinksInfo, SetLinksArgs } from '../bridge/types'
import { useDeviceStore } from '../stores/device'
import LinksStatusCard from '../components/LinksStatusCard.vue'
import PageHead from '../components/PageHead.vue'
import Plate from '../components/Plate.vue'
import Notice from '../components/Notice.vue'
import Rocker from '../components/Rocker.vue'

const store = useDeviceStore()
const toast = useToast()

const info = ref<LinksInfo | null>(null)

const codexOn = ref(false)
const claudeOn = ref(false)
const dshOn = ref(false)
const signalOn = ref(false)
const dotMatrixOn = ref(false)
const idleOffset = ref(0)
const idleRate = ref(100)

const syncBusy = ref(false)
const demoBusy = ref(false)

let syncing = false
let pendingWrites = 0
let numbersTimer: ReturnType<typeof setTimeout> | null = null
let pollTimer: ReturnType<typeof setInterval> | null = null
let writeChain: Promise<void> = Promise.resolve()

// 只有会触发屏幕播放的按钮受写守卫限制；开关与参数只写配置，任何状态都可改。
const locked = computed(
  () =>
    store.state.uploading ||
    !store.state.connected ||
    store.state.nightMode ||
    store.state.maintenance
)

const actionsDisabled = computed(
  () => locked.value || syncBusy.value || demoBusy.value
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
      title: '正在上传，测试与重播按钮暂不可用',
      description: '上传期间会触发屏幕播放的按钮会被 C# 拒绝；下方开关与参数只写配置，仍可调整。'
    }
  }
  if (!store.state.connected) {
    return {
      color: 'error',
      title: '屏幕未连接',
      description: '联动测试与重播按钮暂不可用；下方开关与参数只写配置，仍可调整。'
    }
  }
  if (store.state.maintenance) {
    return {
      color: 'warning',
      title: '维护模式中，测试与重播按钮暂不可用',
      description: '维护期间屏幕不接受写操作；下方开关与参数仍可调整。'
    }
  }
  if (store.state.nightMode) {
    return {
      color: 'secondary',
      title: '夜间熄灯模式中，测试与重播按钮暂不可用',
      description: '退出夜间模式后可用；下方开关与参数仍可调整。'
    }
  }
  return null
})

const offsetError = computed<string | undefined>(() => {
  const value = idleOffset.value
  if (!Number.isFinite(value)) return '请输入 -5000 ~ 5000 之间的数值'
  if (value < -5000 || value > 5000) return '时间偏移必须在 -5000 ~ 5000 毫秒之间'
  return undefined
})

const rateError = computed<string | undefined>(() => {
  const value = idleRate.value
  if (!Number.isFinite(value)) return '请输入 95 ~ 105 之间的数值'
  if (value < 95 || value > 105) return '播放速度必须在 95 ~ 105 之间'
  return undefined
})

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

function apply(next: LinksInfo): void {
  info.value = next
  syncing = true
  codexOn.value = next.codex
  claudeOn.value = next.claude
  dshOn.value = next.dsh === true
  signalOn.value = next.signal
  dotMatrixOn.value = next.dotMatrix
  if (numbersTimer === null) {
    idleOffset.value = next.idleOffsetMs
    idleRate.value = next.idleRatePercent
  }
  void nextTick(() => {
    syncing = false
  })
}

async function reload(): Promise<void> {
  try {
    apply(await bridge.call('getLinks'))
  } catch (error) {
    console.error('[links] 读取联动状态失败', error)
  }
}

function clearNumbersTimer(): void {
  if (numbersTimer !== null) {
    clearTimeout(numbersTimer)
    numbersTimer = null
  }
}

function mergeLocal(args: SetLinksArgs): void {
  if (!info.value) return
  info.value = mergeLinks(info.value, args)
}

function writeLinks(args: SetLinksArgs): void {
  pendingWrites += 1
  writeChain = writeChain.then(async () => {
    try {
      await bridge.call('setLinks', args)
      // 成功时只做本地确认，避免整表重读把其它正在编辑的控件冲掉；轮询会再对齐一次。
      mergeLocal(args)
    } catch (error) {
      toast.add({
        title: '联动设置保存失败',
        description: errorMessage(error),
        color: 'error'
      })
      // 回滚到服务端值
      await reload()
    } finally {
      pendingWrites -= 1
    }
  })
}

function flushNumbers(): void {
  numbersTimer = null
  if (!info.value) return
  const offset = idleOffset.value
  const rate = idleRate.value
  const args: SetLinksArgs = {}
  if (Number.isFinite(offset) && offset !== info.value.idleOffsetMs) {
    args.idleOffsetMs = offset
  }
  if (Number.isFinite(rate) && rate !== info.value.idleRatePercent) {
    args.idleRatePercent = rate
  }
  if (Object.keys(args).length > 0) writeLinks(args)
}

async function runIdleSync(): Promise<void> {
  if (actionsDisabled.value) return
  syncBusy.value = true
  try {
    await bridge.call('idleSync')
    toast.add({
      title: '已重播默认动画并对齐',
      description: '预览约 30 秒后恢复默认动画。',
      color: 'success'
    })
  } catch (error) {
    toast.add({
      title: '重播默认动画失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    syncBusy.value = false
    await reload()
  }
}

async function runDemo(state: LinkDemoState, label: string): Promise<void> {
  if (actionsDisabled.value) return
  demoBusy.value = true
  try {
    await bridge.call('linkDemo', { state })
    toast.add({
      title: `已触发：${label}`,
      description: '约 8 秒后自动恢复默认动画。',
      color: 'success'
    })
  } catch (error) {
    toast.add({
      title: '测试联动失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    demoBusy.value = false
    await reload()
  }
}

watch(codexOn, () => {
  if (syncing) return
  writeLinks({ codex: codexOn.value })
})

watch(claudeOn, () => {
  if (syncing) return
  writeLinks({ claude: claudeOn.value })
})

watch(dshOn, () => {
  if (syncing) return
  writeLinks({ dsh: dshOn.value })
})

watch(signalOn, () => {
  if (syncing) return
  writeLinks({ signal: signalOn.value })
})

watch(dotMatrixOn, () => {
  if (syncing) return
  writeLinks({ dotMatrix: dotMatrixOn.value })
})

// 两个数字输入防抖 600ms 后再 setLinks
watch([idleOffset, idleRate], () => {
  if (syncing) return
  clearNumbersTimer()
  numbersTimer = setTimeout(() => {
    flushNumbers()
  }, 600)
})

onMounted(() => {
  void reload()
  pollTimer = setInterval(() => {
    if (document.hidden) return
    if (pendingWrites > 0 || numbersTimer !== null) return
    void reload()
  }, 2000)
})

onUnmounted(() => {
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
  clearNumbersTimer()
})
</script>

<template>
  <div class="page">
    <PageHead
      title="联动"
      hint="接收 Codex 与 Claude Code 的任务事件，驱动屏幕动画，并可同步到 SignalRGB 灯光和点阵屏。"
    />

    <Notice
      v-if="lockReason"
      :color="lockReason.color"
      :title="lockReason.title"
      :description="lockReason.description"
    />

    <Plate title="灯效说明">
      <p class="prose">
        Codex：蓝色工作 / 橙色等待 / 翠绿呼吸完成。Claude Code：陶橙工作 / 琥珀呼吸等待 / 草绿渐亮完成。Codex 结束提示 30 秒，Claude Code 120 秒，然后恢复默认动画。
      </p>
    </Plate>

    <Plate title="接收与同步">
      <div class="rows">
        <div class="row">
          <div class="row-text">
            <p class="row-label">接收 Codex 任务事件</p>
            <p class="row-help">Codex 的工作 / 等待 / 完成事件会触发对应动画。</p>
          </div>
          <Rocker v-model="codexOn" />
        </div>

        <div class="row">
          <div class="row-text">
            <p class="row-label">接收 Claude Code 任务事件</p>
            <p class="row-help">Claude Code 的工作 / 等待 / 完成事件会触发对应动画。</p>
          </div>
          <Rocker v-model="claudeOn" />
        </div>

        <div class="row">
          <div class="row-text">
            <p class="row-label">接收 DeepSeek Harness 任务事件</p>
            <p class="row-help">DeepSeek Harness 的工作 / 等待 / 完成事件会触发对应动画（需安装 dsh-plugin）。</p>
          </div>
          <Rocker v-model="dshOn" />
        </div>

        <div class="row">
          <div class="row-text">
            <p class="row-label">同步到 SignalRGB 灯光</p>
            <p class="row-help">把联动状态同步给 SignalRGB。</p>
          </div>
          <Rocker v-model="signalOn" />
        </div>

        <div class="row">
          <div class="row-text">
            <p class="row-label">同步到 Dot Matrix Studio 点阵屏</p>
            <p class="row-help">把联动状态发给本机运行的 Dot Matrix Studio，在 iDotMatrix 点阵屏上显示同色图标；夜间熄灯时点阵屏也熄灭。</p>
          </div>
          <Rocker v-model="dotMatrixOn" />
        </div>
      </div>
    </Plate>

    <Plate title="默认灯效参数">
      <div class="form">
        <div class="field">
          <div class="field-body">
            <label class="field-label" for="idle-offset">默认灯效时间偏移（毫秒）</label>
            <p class="field-help">-5000 ~ 5000，步进 50；灯光慢了填正数，快了填负数。</p>
            <p v-if="offsetError" class="field-error">{{ offsetError }}</p>
          </div>
          <UInput
            id="idle-offset"
            v-model="idleOffset"
            type="number"
            :min="-5000"
            :max="5000"
            :step="50"
            class="w-44"
          />
        </div>

        <div class="field">
          <div class="field-body">
            <label class="field-label" for="idle-rate">灯效播放速度（%）</label>
            <p class="field-help">95 ~ 105，步进 0.01；100 为原速。</p>
            <p v-if="rateError" class="field-error">{{ rateError }}</p>
          </div>
          <UInput
            id="idle-rate"
            v-model="idleRate"
            type="number"
            :min="95"
            :max="105"
            :step="0.01"
            class="w-44"
          />
        </div>
      </div>
    </Plate>

    <Plate title="重播与测试">
      <div class="tests-block">
        <UButton
          block
          color="primary"
          variant="solid"
          :loading="syncBusy"
          :disabled="actionsDisabled"
          @click="runIdleSync"
        >
          重播默认动画并对齐（预览 30 秒）
        </UButton>

        <div class="tests">
          <UButton
            block
            color="neutral"
            variant="outline"
            :disabled="actionsDisabled"
            @click="runDemo('working', '测试工作')"
          >
            测试工作
          </UButton>
          <UButton
            block
            color="neutral"
            variant="outline"
            :disabled="actionsDisabled"
            @click="runDemo('waiting', '测试等待')"
          >
            测试等待
          </UButton>
          <UButton
            block
            color="neutral"
            variant="outline"
            :disabled="actionsDisabled"
            @click="runDemo('done', '测试结束')"
          >
            测试结束
          </UButton>
          <UButton
            block
            color="neutral"
            variant="outline"
            :disabled="actionsDisabled"
            @click="runDemo('idle', '恢复默认')"
          >
            恢复默认
          </UButton>
        </div>

        <p class="fine">测试按钮约 8 秒后自动恢复；重播预览约 30 秒后回到默认动画。</p>
      </div>
    </Plate>

    <LinksStatusCard :info="info" />

    <p class="fine foot">页面可见时每 2 秒自动刷新联动状态。</p>
  </div>
</template>

<style scoped>
.prose { font-size: 0.82rem; color: var(--ui-text-muted); line-height: 1.7; max-width: 62ch; }

/* 开关排成对齐的表单行 */
.rows .row { display: flex; align-items: center; justify-content: space-between; gap: 2rem; }
.rows .row + .row { margin-top: 1.3rem; padding-top: 1.3rem; border-top: 1px solid var(--ui-border-muted); }
.row-label { font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.row-help { margin-top: 0.2rem; font-size: 0.78rem; color: var(--ui-text-muted); max-width: 46ch; line-height: 1.55; }

/* 数字输入：整齐的表单行 */
.form { display: grid; gap: 1.35rem; }
.field { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 1.5rem; align-items: start; }
.field + .field { padding-top: 1.35rem; border-top: 1px solid var(--ui-border-muted); }
.field-label { font-size: 0.9rem; font-weight: 600; color: var(--ui-text-highlighted); }
.field-help { margin-top: 0.2rem; font-size: 0.78rem; color: var(--ui-text-muted); max-width: 46ch; line-height: 1.55; }
.field-error { margin-top: 0.3rem; font-size: 0.78rem; color: var(--color-state-error); }

/* 重播是主操作，测试是次要操作并排 */
.tests-block { display: grid; gap: 1.1rem; }
.tests { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 0.6rem; }
.fine { font-size: 0.78rem; color: var(--ui-text-muted); line-height: 1.6; }
.foot { margin-top: 1.6rem; }

@media (max-width: 640px) {
  .tests { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
