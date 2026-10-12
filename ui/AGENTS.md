# 工人守则（所有在 ui/ 目录里干活的智能体必须遵守）

本项目是控制七彩虹显卡 LCD 侧屏的桌面工具。**写错串口会把屏幕写坏，只能整机断电 30 秒恢复。**

## 绝对禁止
1. 不得读取、修改、编译、运行 `ui/` 以外的任何文件（包括 `../GpuLcd.cs`、`../*.cs`、`../*.exe`、`../build.cmd`）。需要的信息都在 `ui/docs/bridge-protocol.md`。
2. 不得运行任何 `.exe`，不得访问 COM 口、命名管道、`%APPDATA%\GpuLcdTool`。
3. 不得执行 `git` 命令、不得删除 `ui/` 之外的任何东西。
4. 不得在前端读取本地文件内容、拼接本地路径、或实现"删除屏幕文件"。上传只能通过 `bridge-protocol.md` 阶段 2 列出的 `pickUploadFile` / `getSpace` / `startUpload`（文件选择、二次确认都由 C# 完成，前端只拿 token）。不得使用 `<input type="file">`、FileReader、拖放读取文件。只做协议（阶段 1-3）里列出的命令；页面里用 `import { bridge } from "../bridge"` 调用 `bridge.call(cmd,args)` 是允许的。
5. 不得修改 `ui/docs/bridge-protocol.md` 和 `ui/AGENTS.md`。发现协议不够用，在你的最终回复里写出需求，不要自行加命令。

## 技术栈（固定，不得更换）
- Vue 3（`<script setup lang="ts">`，组合式 API）+ TypeScript（strict）
- Vite（**不是** Nuxt 整套框架；用 `@nuxt/ui/vite` 插件使用 Nuxt UI v4 组件库）
- Tailwind CSS v4（随 Nuxt UI 引入，不要另配 Sass，除非绝对必要）
- 包管理用 `npm`（本机没有 pnpm）
- 构建产物：`npm run build` → `ui/dist/`（静态文件，`base: './'`，用相对路径，因为将用 `file://`/虚拟主机加载）
- 界面语言：简体中文。深色主题为默认，同时支持浅色。

## 通信约束
- 所有与 C# 的通信只经 `src/bridge/` 里的统一接口（`bridge.call(cmd, args)` 和 `bridge.on(event, handler)`）。页面组件里禁止直接写 `window.chrome.webview`。
- 浏览器里没有 `window.chrome?.webview` 时自动用 mock（`src/bridge/mock.ts`），mock 要能模拟：连接成功/未连接/上传中三种状态切换，并让所有命令有合理的假数据，方便不连硬件就能开发和截图。

## 完成标准
- `npm run build` 与 `npx vue-tsc --noEmit` 均 0 错误。
- 用浏览器开发模式（`npm run dev`）能打开并操作。
- 最终回复列出：改了哪些文件、怎么运行、遇到的问题、需要协议新增什么。**不要声称做了没做的事。**
