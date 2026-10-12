import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import ui from '@nuxt/ui/vite'

// base: './' —— 产物用 file:// 或虚拟主机加载，必须相对路径
// router: false —— 本工具不装 vue-router，用自管理页签切换
// colorMode: false —— 深色由 index.html 的 class="dark" 固定
// ui.colors.primary: cyan —— 主色
export default defineConfig({
  base: './',
  plugins: [
    vue(),
    ui({
      router: false,
      colorMode: false,
      ui: {
        // 主色在 main.css 里直接设成墨色；这里只固定"状态语义色"，让它们和孪生屏的状态色一致
        colors: {
          primary: 'zinc',
          neutral: 'zinc',
          info: 'blue',
          success: 'emerald',
          warning: 'amber',
          error: 'red'
        }
      }
    })
  ],
  build: {
    outDir: 'dist'
  }
})
