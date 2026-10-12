<script setup lang="ts">
import { computed, ref } from 'vue'
import { useDeviceStore } from '../stores/device'
import PageHead from '../components/PageHead.vue'
import Plate from '../components/Plate.vue'
import Notice from '../components/Notice.vue'
import ImageFileList from '../components/ImageFileList.vue'
import ImageUploadCard from '../components/ImageUploadCard.vue'

const store = useDeviceStore()
const toast = useToast()

const refreshing = ref(false)

const files = computed(() => store.files.value)
const currentFile = computed(() => store.settings.current)
const playingFile = computed(() => store.state.playing)

const blockReason = computed<string | null>(() => {
  if (store.state.uploading) return '正在上传，期间文件操作会被 C# 拒绝。'
  if (!store.state.connected) return '屏幕未连接，请检查 USB 连接与 COM 口。'
  if (store.state.maintenance) return '屏幕正在维护中，暂时无法操作文件。'
  if (store.state.nightMode) return '夜间模式（熄灯）已开启，屏幕不响应操作。'
  return null
})

const alertColor = computed< 'error' | 'warning' | 'secondary' >(() => {
  if (!store.state.connected) return 'error'
  if (store.state.nightMode) return 'secondary'
  return 'warning'
})

const actionsDisabled = computed(() => blockReason.value !== null)

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : '未知错误'
}

async function refresh(): Promise<void> {
  if (refreshing.value) return
  refreshing.value = true
  try {
    await store.refresh()
    toast.add({
      title: '列表已刷新',
      description: `共 ${files.value.length} 个文件`,
      color: 'success'
    })
  } catch (error) {
    toast.add({
      title: '刷新失败',
      description: errorMessage(error),
      color: 'error'
    })
  } finally {
    refreshing.value = false
  }
}
</script>

<template>
  <div class="page">
    <PageHead title="图片 / 动画" hint="屏幕上的文件列表，可将某个文件设为当前全屏动画。" />

    <Notice
      v-if="blockReason"
      :color="alertColor"
      title="当前无法操作"
      :description="blockReason"
    />

    <Plate title="屏幕文件" :hint="`共 ${files.length} 个文件`">
      <template #aside>
        <UButton
          color="neutral"
          variant="outline"
          :loading="refreshing"
          :disabled="actionsDisabled"
          @click="refresh"
        >
          刷新列表
        </UButton>
      </template>

      <ImageFileList
        :files="files"
        :current="currentFile"
        :playing="playingFile"
        :disabled="actionsDisabled"
      />
    </Plate>

    <ImageUploadCard />
  </div>
</template>
