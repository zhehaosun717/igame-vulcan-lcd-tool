<script setup lang="ts">
// 分区：不画盒子，用"标题行 + 一条刻线"来分组，像规格表而不是一排卡片。
// 用法与 UCard 对应：#header 插槽 → 自定义标题行；#footer 插槽；默认插槽 → 内容。
// 简单情形直接给 title / hint；右侧可放 #aside。
defineProps<{ title?: string; hint?: string }>()
</script>

<template>
  <section class="plate">
    <header v-if="title || $slots.header || $slots.aside" class="head">
      <slot name="header">
        <div class="titles">
          <h2>{{ title }}</h2>
          <p v-if="hint">{{ hint }}</p>
        </div>
      </slot>
      <div v-if="$slots.aside" class="aside"><slot name="aside" /></div>
    </header>
    <div class="body"><slot /></div>
    <footer v-if="$slots.footer" class="foot"><slot name="footer" /></footer>
  </section>
</template>

<style scoped>
.plate + .plate { margin-top: 2.25rem; }

.head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
  padding-bottom: 0.7rem;
  border-bottom: 1px solid var(--ui-border);
}
.titles h2 {
  font-size: 1.02rem;
  font-weight: 600;
  letter-spacing: 0.01em;
  color: var(--ui-text-highlighted);
}
.titles p {
  margin-top: 0.2rem;
  font-size: 0.78rem;
  color: var(--ui-text-muted);
  max-width: 46ch;
}
.aside { flex: none; }
.body { padding-top: 1.2rem; }
/* 插槽没有任何元素（例如 v-if 都没命中）时，整块收起，不留空白 */
.body:not(:has(*)) { display: none; }
.foot {
  margin-top: 1.1rem;
  padding-top: 0.9rem;
  border-top: 1px dashed var(--ui-border);
}
</style>
