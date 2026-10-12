<script setup lang="ts">
// 页头：页面名 + 一句话说明。名字用重字重，说明退到次要色，限制行长。
defineProps<{ title: string; hint?: string }>()
</script>

<template>
  <header class="pagehead">
    <h1>{{ title }}</h1>
    <p v-if="hint">{{ hint }}</p>
  </header>
</template>

<style scoped>
.pagehead { margin-bottom: 2rem; }
h1 { font-size: 1.75rem; font-weight: 700; line-height: 1.15; letter-spacing: 0.01em; color: var(--ui-text-highlighted); }
p { margin-top: 0.4rem; font-size: 0.86rem; color: var(--ui-text-muted); max-width: 52ch; line-height: 1.6; }
</style>
