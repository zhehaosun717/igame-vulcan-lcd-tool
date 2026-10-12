<script setup lang="ts">
import { computed, ref } from 'vue'
import { bridge } from '../bridge'
import type { WidgetStatus } from '../bridge/types'
import Plate from './Plate.vue'

const props = defineProps<{
  status: WidgetStatus | null
  disabled: boolean
}>()

const emit = defineEmits<{
  (e: 'cleared'): void
}>()

const toast = useToast()

const KIND_LABELS: Record<string, string> = {
  progress: '进度',
  notify: '通知'
}

const clearing = ref(false)

const kindLabel = computed(() => {
  const kind = props.status?.kind
  if (!kind) return '—'
  return KIND_LABELS[kind] ?? kind
})

const canClear = computed(() => !props.disabled && !clearing.value)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

async function clear(): Promise<void> {
  if (!canClear.value) return
  clearing.value = true
  try {
    const result = await bridge.call('sendWidget', { action: 'clear' })
    toast.add({
      title: '已清除临时显示',
      description: result.message,
      color: 'success'
    })
    emit('cleared')
  } catch (error) {
    toast.add({
      title: '清除失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    clearing.value = false
  }
}
</script>

<template>
  <Plate title="当前挂件状态" hint="屏幕此刻是否被临时挂件占用。">
    <template #aside>
      <UBadge color="neutral" variant="outline" size="sm" class="state">
        <i class="dot" :data-on="status?.active ? 'true' : 'false'" />{{ status?.active ? '占用屏幕' : '空闲' }}
      </UBadge>
    </template>

    <dl class="read">
      <div>
        <dt>占用</dt>
        <dd>{{ status?.active ? '是' : '否' }}</dd>
      </div>
      <div>
        <dt>类型</dt>
        <dd>{{ kindLabel }}</dd>
      </div>
      <div>
        <dt>已发送</dt>
        <dd class="num">{{ status?.sent || '—' }}</dd>
      </div>
      <div>
        <dt>错误</dt>
        <dd class="err" :class="{ on: !!status?.error }">{{ status?.error || '—' }}</dd>
      </div>
    </dl>

    <template #footer>
      <UButton
        color="neutral"
        variant="outline"
        :loading="clearing"
        :disabled="!canClear"
        @click="clear"
      >
        清除临时显示
      </UButton>
    </template>
  </Plate>
</template>

<style scoped>
.state { display: inline-flex; align-items: center; gap: 0.4rem; }
.dot {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: var(--ui-border-accented);
}
.dot[data-on='true'] {
  background: var(--color-state-working);
  box-shadow: 0 0 0.55rem var(--color-state-working);
}

.read { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1.1rem 2rem; }
.read dt { font-size: 0.72rem; color: var(--ui-text-dimmed); }
.read dd { margin-top: 0.15rem; font-size: 1rem; color: var(--ui-text-highlighted); overflow-wrap: anywhere; }
.read dd.num { font-size: 1.25rem; font-weight: 600; letter-spacing: 0.02em; }
.read dd.err.on { color: var(--color-state-error); }
</style>
