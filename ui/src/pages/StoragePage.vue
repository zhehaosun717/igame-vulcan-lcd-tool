<script setup lang="ts">
import { computed } from 'vue'
import { useDeviceStore } from '../stores/device'
import StorageOpeningCard from '../components/StorageOpeningCard.vue'
import StorageMaintenanceCard from '../components/StorageMaintenanceCard.vue'
import PageHead from '../components/PageHead.vue'
import Notice from '../components/Notice.vue'
import Plate from '../components/Plate.vue'

const store = useDeviceStore()

interface LockReason {
  color: 'warning' | 'error' | 'secondary'
  title: string
  description: string
}

// 开机 / 存储相关命令受写守卫限制：上传中 / 未连接 / 维护 / 熄灯 时禁用控件并说明原因。
const lockReason = computed<LockReason | null>(() => {
  if (store.state.uploading) {
    return {
      color: 'warning',
      title: '正在上传，暂时无法进行开机 / 存储操作',
      description: '上传期间相关写操作会被 C# 拒绝，请等待上传完成。'
    }
  }
  if (!store.state.connected) {
    return {
      color: 'error',
      title: '屏幕未连接',
      description: '请检查 USB 连接与 COM 口后再进行开机 / 存储操作。'
    }
  }
  if (store.state.maintenance) {
    return {
      color: 'warning',
      title: '维护模式中，暂时无法进行开机 / 存储操作',
      description: '屏幕已处于维护模式，相关操作暂不可用。'
    }
  }
  if (store.state.nightMode) {
    return {
      color: 'secondary',
      title: '夜间熄灯模式中，暂时无法进行开机 / 存储操作',
      description: '退出夜间模式后可继续。'
    }
  }
  return null
})

const locked = computed(() => lockReason.value !== null)
</script>

<template>
  <div class="page">
    <PageHead
      title="存储 / 开机"
      hint="屏幕支持 USB 存储维护：进入后暂停播放和外部指令，可以浏览文件、备份、替换或恢复开机动画。"
    />

    <Notice
      v-if="lockReason"
      :color="lockReason.color"
      :title="lockReason.title"
      :description="lockReason.description"
    />

    <Plate title="关于开机方案">
      <div class="about">
        <p>
          固件 1.22 的开机方案 0 使用
          <span class="num">op.savi</span> /
          <span class="num">oploop.savi</span>；方案 1 使用
          <span class="num">op1.savi</span> /
          <span class="num">op1loop.savi</span>。
        </p>
        <p>普通图片动画在 <span class="num">mov</span> 文件夹。</p>
        <p>目前恢复普通控制需要安全弹出存储，再整机断电约 30 秒后开机。</p>
      </div>
    </Plate>

    <StorageOpeningCard :disabled="locked" />
    <!-- 存储维护入口只在上传中禁用：屏幕无响应 / 未连接 / 熄灯时恰恰最可能需要它（原 WinForms 按钮也始终可用） -->
    <StorageMaintenanceCard :disabled="store.state.uploading" />
  </div>
</template>

<style scoped>
.about { display: grid; gap: 0.7rem; }
.about p { font-size: 0.85rem; color: var(--ui-text-muted); line-height: 1.65; max-width: 60ch; }
.about .num { font-size: 0.95rem; color: var(--ui-text-highlighted); }
</style>
