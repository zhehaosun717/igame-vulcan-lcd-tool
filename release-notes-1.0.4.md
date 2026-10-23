## 显卡屏幕控制 1.0.4

修复 1.0.3 的一个疏漏：**联动页里没有「接收 DeepSeek Harness 任务事件」这一行**。后端逻辑在 1.0.3 已经有了，但网页界面漏加了这个开关，所以装上 1.0.3 也看不到、也就没法用 DSH 联动。

### 下载

- **GpuLcd-Setup-1.0.4.exe**：安装版，按用户安装，可直接覆盖 1.0.3。
- **GpuLcd-1.0.4-portable.zip**：便携版。
- **dsh-gpulcd-status-0.1.0.zip**：DeepSeek Harness 插件（包里也带了一份）。

### 修复

- 联动页新增「接收 DeepSeek Harness 任务事件」开关（默认开）。
- 界面状态类型补上 `dsh` 字段，读写联动设置不再丢这个值。

### 使用 DSH 联动

1. 安装本版本，打开联动页，确认能看到 DeepSeek Harness 那一行并打开。
2. 把 `dsh-plugin/` 复制进 DSH profile 的 `node_modules`，在 `package.json` 的 `dsh.profile.bundles` 里加上 `dsh-gpulcd-status`，**关闭 DSH 后**再启动。
   注意：不要在 DSH 运行时用 `pnpm add` 装它，pnpm 会重建 `node_modules`，运行中的 DSH 会起不来。
3. 把 `anims/dswork.gif`、`dsask.gif`、`dsdone.gif` 上传到屏幕，命名 `dswork.savi / dsask.savi / dsdone.savi`。

### 验证

前端 `vue-tsc` 与 `vite build` 通过，构建产物里确认含 DSH 开关；C# 编译通过；状态机 50 项、点阵屏 67 项、完成补收 25 项、显示任务 15 项、运行库 5 项断言全过；发行包校验通过（含 11 个插件文件与三张 DSH 动画）。
