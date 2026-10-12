<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { Component } from 'vue'
import { isMock } from './bridge'
import { useDeviceStore } from './stores/device'
import ScreenTwin from './components/ScreenTwin.vue'
import MockControl from './components/MockControl.vue'
import BasicPage from './pages/BasicPage.vue'
import ModesPage from './pages/ModesPage.vue'
import ImagePage from './pages/ImagePage.vue'
import TextPage from './pages/TextPage.vue'
import LivePage from './pages/LivePage.vue'
import LinksPage from './pages/LinksPage.vue'
import WidgetsPage from './pages/WidgetsPage.vue'
import StoragePage from './pages/StoragePage.vue'

interface NavItem {
  key: string
  label: string
  component: Component
}

const navItems: NavItem[] = [
  { key: 'basic', label: '基本', component: BasicPage },
  { key: 'modes', label: '轮播页面', component: ModesPage },
  { key: 'image', label: '图片 / 动画', component: ImagePage },
  { key: 'text', label: '文字', component: TextPage },
  { key: 'live', label: '实时数据', component: LivePage },
  { key: 'links', label: '联动', component: LinksPage },
  { key: 'widgets', label: '进度 / 通知', component: WidgetsPage },
  { key: 'storage', label: '存储 / 开机', component: StoragePage }
]

const active = ref('basic')
const current = computed<Component>(() => navItems.find((item) => item.key === active.value)?.component ?? BasicPage)

const store = useDeviceStore()
onMounted(() => {
  store.refresh().catch((error) => console.error('[device] 刷新失败', error))
})
</script>

<template>
  <UApp>
    <div class="shell">
      <header class="plate">
        <ScreenTwin />
      </header>

      <div class="body">
        <nav class="rail" aria-label="页面">
          <button
            v-for="item in navItems"
            :key="item.key"
            type="button"
            class="rail-item"
            :class="{ on: active === item.key }"
            :aria-current="active === item.key ? 'page' : undefined"
            @click="active = item.key"
          >
            {{ item.label }}
          </button>
        </nav>

        <main class="stage">
          <component :is="current" :key="active" />
        </main>
      </div>

      <MockControl v-if="isMock" />
    </div>
  </UApp>
</template>

<style scoped>
.shell {
  display: flex;
  flex-direction: column;
  height: 100vh;
}

/* 铭牌：常驻在顶部，页面内容在它下面滚动 */
.plate {
  flex: none;
  padding: 1.25rem 1.75rem 1.1rem;
  border-bottom: 1px solid var(--ui-border);
  background: linear-gradient(180deg, #14181b, #121518);
}

.body {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 11.5rem minmax(0, 1fr);
}

/* 导航：不做按钮方块，只用一条"点亮的刻度"标出当前页 */
.rail {
  display: flex;
  flex-direction: column;
  padding: 1.25rem 0 1.25rem 1.75rem;
  border-right: 1px solid var(--ui-border);
  overflow-y: auto;
}
.rail-item {
  position: relative;
  text-align: left;
  padding: 0.55rem 1rem 0.55rem 1rem;
  font-size: 0.92rem;
  color: var(--ui-text-muted);
  transition: color 0.15s ease;
}
.rail-item::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0.45rem;
  bottom: 0.45rem;
  width: 2px;
  background: var(--ui-border);
  transition: background 0.15s ease, box-shadow 0.15s ease;
}
.rail-item:hover { color: var(--ui-text-highlighted); }
.rail-item.on { color: var(--ui-text-highlighted); font-weight: 600; }
.rail-item.on::before { background: var(--ui-text-highlighted); box-shadow: 0 0 0.5rem rgb(233 236 239 / 0.55); }

.stage {
  overflow-y: auto;
  padding: 1.75rem 2.25rem 3rem;
}
</style>
