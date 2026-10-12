<script setup lang="ts">
// 屏幕孪生：按真实 480×128（15:4）比例，还原物理侧屏"此刻会显示什么"。
// 这是整个界面里唯一发光的东西——其余部分是无彩色的"机箱"。
// 它是示意，不是实时画面：设备没有提供读回当前帧的接口，所以只根据已知状态推演。
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { bridge } from '../bridge'
import type { PreviewInfo } from '../bridge/types'
import { useDeviceStore } from '../stores/device'
import { endPeek, loadPreview, peekName } from '../stores/preview'
import AnimPlayer from './AnimPlayer.vue'

const store = useDeviceStore()

// ---- 挂件（进度 / 通知）：C# 的 sent 形如 "notify|文字|RRGGBB|x|y|字号" ----
const widget = ref<{ active: boolean; kind: string; text: string; color: string }>({ active: false, kind: '', text: '', color: 'FFFFFF' })
let timer: ReturnType<typeof setInterval> | undefined

async function pollWidget(): Promise<void> {
  if (document.hidden) return
  try {
    const w = await bridge.call('getWidgetStatus')
    const [kind = '', text = '', color = 'FFFFFF'] = (w.sent || '').split('|')
    widget.value = { active: w.active && !!w.sent, kind, text, color: /^[0-9A-Fa-f]{6}$/.test(color) ? color : 'FFFFFF' }
  } catch {
    widget.value = { ...widget.value, active: false }
  }
}

onMounted(() => {
  void pollWidget()
  timer = setInterval(pollWidget, 1500)
})
onUnmounted(() => {
  if (timer) clearInterval(timer)
})

// ---- 屏幕此刻的"模式"：按优先级，与真实设备的行为一致 ----
type Mode = 'nolink' | 'maint' | 'off' | 'upload' | 'peek' | 'widget' | 'link' | 'idle'
const mode = computed<Mode>(() => {
  const s = store.state
  if (s.uploading) return 'upload'
  if (peekName.value) return 'peek'   // 用户主动点了"预览"：优先于屏幕当前状态（上传除外）
  if (!s.connected) return 'nolink'
  if (s.maintenance) return 'maint'
  if (s.nightMode || !store.settings.enabled) return 'off'
  if (widget.value.active) return 'widget'
  if (s.linkState !== 'idle') return 'link'
  return 'idle'
})

const stateColor: Record<string, string> = {
  working: 'var(--color-state-working)',
  waiting: 'var(--color-state-waiting)',
  done: 'var(--color-state-done)'
}
const stateWord: Record<string, string> = { working: 'WORKING', waiting: 'WAITING', done: 'DONE' }
const linkColor = computed(() => stateColor[store.state.linkState] ?? 'transparent')

// 亮度 0-10 映射成可见亮度，0 也留一点点，免得误以为"坏了"
const bright = computed(() => Math.max(0, Math.min(10, store.settings.brightness)) / 10)
const fileLabel = computed(() => (target.value || '——').replace(/\.savi$/i, ''))

// ---- 真实画面：屏幕正在播放（或默认）的文件；联动时 playing 就是联动动画 ----
const target = computed(() => peekName.value || store.state.playing || store.settings.current || '')
const preview = ref<PreviewInfo | null>(null)
const previewFor = ref('')
let seq = 0
watch(
  target,
  async (name) => {
    const my = ++seq
    if (!name) { preview.value = null; previewFor.value = ''; return }
    const p = await loadPreview(name)
    if (my !== seq) return   // 期间又换了文件
    preview.value = p
    previewFor.value = name
  },
  { immediate: true }
)
const hasFrames = computed(() => !!preview.value?.available && !!preview.value.frames?.length && previewFor.value === target.value)
const noPreviewReason = computed(() => (preview.value && !preview.value.available && previewFor.value === target.value ? preview.value.reason : ''))

// 开机"通电"动画：只在首次挂载时播放一次
const booting = ref(true)
onMounted(() => setTimeout(() => (booting.value = false), 1100))

const dot = computed(() => {
  const m: Record<string, string> = { working: 'var(--color-state-working)', waiting: 'var(--color-state-waiting)', done: 'var(--color-state-done)' }
  return m[store.state.linkState] ?? 'var(--ui-border-accented)'
})
const linkText = computed(() => ({ idle: '空闲', working: '工作中', waiting: '等待授权', done: '已完成' })[store.state.linkState] ?? '空闲')
</script>

<template>
  <section class="twin" aria-label="屏幕孪生（示意）">
    <div class="bezel">
      <i class="crop tl" /><i class="crop tr" /><i class="crop bl" /><i class="crop br" />

      <div class="screen" :class="{ boot: booting }" :data-mode="mode">
        <!-- 内容层：亮度只影响内容，不影响黑底 -->
        <div class="content" :style="{ opacity: mode === 'off' || mode === 'upload' ? 1 : 0.3 + 0.7 * bright }">
          <template v-if="mode === 'nolink'">
            <div class="noise" />
            <span class="silk big dim">NO LINK</span>
          </template>

          <template v-else-if="mode === 'maint'">
            <span class="silk big">USB MAINT</span>
          </template>

          <template v-else-if="mode === 'upload'">
            <div class="col">
              <span class="silk mid">WRITING</span>
              <div class="bar"><b :style="{ width: store.upload.percent + '%' }" /></div>
              <span class="silk small dim">{{ store.upload.percent }}%  DO NOT POWER OFF</span>
            </div>
          </template>

          <template v-else-if="mode === 'widget'">
            <span class="silk big" :style="{ color: '#' + widget.color }">{{ widget.text }}</span>
          </template>

          <!-- 预览 / 联动 / 空闲：有本机副本就播放真实画面 -->
          <template v-else-if="mode === 'peek' || ((mode === 'link' || mode === 'idle') && hasFrames)">
            <AnimPlayer v-if="hasFrames" :frames="preview!.frames!" :fps="preview!.fps ?? 60" />
            <template v-else>
              <div class="scan" />
              <span class="silk big">{{ fileLabel }}</span>
            </template>
          </template>

          <template v-else-if="mode === 'link'">
            <div class="wash" :style="{ '--c': linkColor }" />
            <span class="silk big" :style="{ color: '#f4f8ff', textShadow: `0 0 1.2cqw ${linkColor}, 0 0 3.6cqw ${linkColor}` }">{{ stateWord[store.state.linkState] }}</span>
          </template>

          <template v-else-if="mode === 'idle'">
            <div class="scan" />
            <span class="silk big">{{ fileLabel }}</span>
          </template>
        </div>

        <!-- 预览时的角标：说明这不是屏幕此刻的内容，并可以立刻退出 -->
        <button v-if="mode === 'peek'" type="button" class="peek-tag" @click="endPeek">
          预览 {{ target }}<span aria-hidden="true">✕</span><span class="sr-only">，点击结束预览</span>
        </button>

        <!-- 像素结构：真实 LCD 能看到的栅格感 -->
        <div class="pixels" :class="{ faint: hasFrames && (mode === 'idle' || mode === 'link' || mode === 'peek') }" />
      </div>
    </div>

    <dl class="meta">
      <div><dt>显卡</dt><dd class="num">{{ store.state.connected ? store.state.gpu.replace('GeForce ', '') : '—' }}</dd></div>
      <div><dt>串口</dt><dd class="num">{{ store.state.connected ? store.state.com : '—' }}</dd></div>
      <div><dt>固件</dt><dd class="num">{{ store.state.connected && store.state.firmware ? store.state.firmware : '—' }}</dd></div>

      <div class="grow" />

      <div v-if="store.state.uploading"><dt>上传</dt><dd><i class="led on warn" />写入中</dd></div>
      <div><dt>联动</dt><dd><i class="led" :class="{ on: store.state.linkState !== 'idle' }" :style="{ '--led': dot }" />{{ linkText }}</dd></div>
      <div><dt>熄灯</dt><dd><i class="led" :class="{ on: store.state.nightMode }" style="--led: #8b949e" />{{ store.state.nightMode ? '已开启' : '关' }}</dd></div>
    </dl>
    <p class="note">
      <template v-if="hasFrames && (mode === 'idle' || mode === 'link' || mode === 'peek')">画面来自{{ preview?.source || '本机副本' }} {{ target }}，按当前状态推演，不是屏幕此刻的实时画面</template>
      <template v-else-if="noPreviewReason && (mode === 'idle' || mode === 'peek')">{{ target }} 无法预览：{{ noPreviewReason }}</template>
      <template v-else>示意图：根据当前状态推演，不是屏幕的实时画面</template>
    </p>
  </section>
</template>

<style scoped>
/* 设备铭牌：左边是屏幕孪生，右边是竖排读数，像显卡包装盒上的规格栏 */
.twin {
  display: grid;
  /* 屏幕宽度同时受两个因素限制：不超过 36rem，且头部整体不超过窗口高度的约 38%（系统显示缩放后窗口的有效高度会小很多） */
  grid-template-columns: minmax(0, min(36rem, max(17rem, calc((38vh - 5.4rem) * 3.75)))) minmax(8.5rem, 1fr);
  grid-template-areas: 'screen meta' 'note meta';
  column-gap: 2rem;
  align-items: start;
}
.bezel { grid-area: screen; }
.note { grid-area: note; }
.meta { grid-area: meta; }
/* 窄窗口：读数改成屏幕下面的一行，而不是再占一整列的高度 */
@media (max-width: 640px) {
  .twin { grid-template-columns: minmax(0, 1fr); grid-template-areas: 'screen' 'meta' 'note'; }
  .meta { display: flex; flex-wrap: wrap; gap: 0.4rem 1.6rem; padding-left: 0; border-left: 0; margin-top: 0.6rem; }
}

/* 屏幕外框：像显卡边缘的挡板，只有一条刻线和一点点内凹 */
.bezel {
  position: relative;
  padding: 0.7rem;
  background: linear-gradient(180deg, #171c20, #101316);
  border: 1px solid var(--ui-border);
  border-radius: 6px;
  box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.04), 0 18px 40px -22px #000;
}

/* 裁切标记：框出屏幕的真实可视区域 */
.crop {
  position: absolute;
  width: 0.7rem;
  height: 0.7rem;
  border: 0 solid var(--ui-border-accented);
}
.crop.tl { top: 0.35rem; left: 0.35rem; border-top-width: 1px; border-left-width: 1px; }
.crop.tr { top: 0.35rem; right: 0.35rem; border-top-width: 1px; border-right-width: 1px; }
.crop.bl { bottom: 0.35rem; left: 0.35rem; border-bottom-width: 1px; border-left-width: 1px; }
.crop.br { bottom: 0.35rem; right: 0.35rem; border-bottom-width: 1px; border-right-width: 1px; }

.screen {
  position: relative;
  width: 100%;
  aspect-ratio: 480 / 128;
  container-type: inline-size;
  overflow: hidden;
  background: #04060a;
  border-radius: 2px;
  box-shadow: 0 0 0 1px #000, inset 0 0 38px rgb(0 0 0 / 0.9);
}

.content {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  transition: opacity 0.35s ease;
}

/* 字号用 cqw：480 个设备像素 = 100cqw，所以 1 设备像素 ≈ 0.208cqw */
.silk.big { font-size: 5.4cqw; color: #e8f1ff; text-shadow: 0 0 1.2cqw currentColor; }
.silk.mid { font-size: 3.6cqw; color: #e8f1ff; }
.silk.small { font-size: 2.2cqw; color: #e8f1ff; }
.silk.dim { opacity: 0.55; }
.silk { position: relative; z-index: 1; }

.col { display: grid; justify-items: center; gap: 2.4cqw; width: 70%; }
.bar { width: 100%; height: 3cqw; background: #0e131a; border: 1px solid #263140; }
.bar b { display: block; height: 100%; background: #e8f1ff; transition: width 0.25s linear; }

/* 联动：整面屏幕被状态色浸透，和真实动画的色调一致 */
.wash {
  position: absolute;
  inset: 0;
  background:
    radial-gradient(65% 130% at 50% 50%, color-mix(in oklab, var(--c) 70%, transparent), transparent 72%),
    linear-gradient(180deg, color-mix(in oklab, var(--c) 22%, transparent), transparent);
  animation: breathe 2.4s ease-in-out infinite;
}

/* 空闲：一道缓慢移动的扫光，暗示"这里有一段动画在循环" */
.scan {
  position: absolute;
  inset: 0;
  background: linear-gradient(100deg, transparent 30%, rgb(160 190 255 / 0.1) 50%, transparent 70%);
  background-size: 220% 100%;
  animation: sweep 6s linear infinite;
}

/* 断连：静态雪花点阵 */
.noise {
  position: absolute;
  inset: 0;
  opacity: 0.35;
  background-image: radial-gradient(#6b7a8c 0.6px, transparent 0.8px);
  background-size: 5px 5px;
  mask-image: radial-gradient(70% 100% at 50% 50%, #000, transparent);
}

.peek-tag {
  position: absolute;
  z-index: 2;
  left: 1.4cqw;
  bottom: 1.4cqw;
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.18rem 0.55rem;
  font-size: 0.72rem;
  color: #f4f6f8;
  background: rgb(10 12 15 / 0.78);
  border: 1px solid rgb(255 255 255 / 0.18);
  border-radius: 2px;
}
.peek-tag:hover { border-color: rgb(255 255 255 / 0.5); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }

/* 真实画面上只留一点点栅格质感，不要像隔着纱窗 */
.pixels.faint { opacity: 0.35; }

/* 像素栅格：用叠加的细线模拟 LCD 子像素，不遮挡内容 */
.pixels {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background:
    repeating-linear-gradient(90deg, rgb(0 0 0 / 0.2) 0 1px, transparent 1px 0.4167%),
    repeating-linear-gradient(0deg, rgb(0 0 0 / 0.2) 0 1px, transparent 1px 0.78125%);
  mix-blend-mode: multiply;
}

/* 通电：一条亮线展开成整个画面。整个界面只有这一处自发动画。 */
.screen.boot { animation: power-on 0.95s cubic-bezier(0.2, 0.8, 0.2, 1) both; }

@keyframes power-on {
  0% { clip-path: inset(49.2% 0 49.2% 0); filter: brightness(3.2); }
  35% { clip-path: inset(49.2% 0 49.2% 0); filter: brightness(3.2); }
  100% { clip-path: inset(0 0 0 0); filter: brightness(1); }
}
@keyframes sweep { to { background-position: -220% 0; } }
@keyframes breathe { 50% { opacity: 0.62; } }

/* 读数条：不画卡片，只用留白分栏 */
.meta {
  display: grid;
  grid-template-columns: repeat(2, max-content);
  justify-content: start;
  gap: 0.95rem 2.2rem;
  align-content: start;
  margin: 0.2rem 0 0;
  padding-left: 1.4rem;
  border-left: 1px solid var(--ui-border);
}
.meta > div { display: grid; gap: 0.1rem; }
.meta dt { font-size: 0.7rem; color: var(--ui-text-dimmed); }
.meta dd { font-size: 0.95rem; color: var(--ui-text-highlighted); display: flex; align-items: center; gap: 0.45rem; }
.meta dd.num { font-size: 1.15rem; font-weight: 600; letter-spacing: 0.02em; }
.meta .grow { display: none; }

.led {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: #2a3138;
  box-shadow: inset 0 0 0 1px #3b444d;
}
.led.on { background: var(--led, #e9ecef); box-shadow: 0 0 0.6rem var(--led, #e9ecef); }
.led.warn { --led: var(--color-state-waiting); }

.note { margin: 0.4rem 0.25rem 0; font-size: 0.68rem; color: var(--ui-text-dimmed); }
</style>
