## 显卡屏幕控制 1.0.3

本版增加 **DeepSeek Harness** 作为第三个状态来源：现在 Claude Code、Codex、DeepSeek Harness 都能驱动显卡屏动画、SignalRGB 灯效和点阵屏，谁在忙就显示谁。

### 下载

- **GpuLcd-Setup-1.0.3.exe**：安装版，按用户安装，可直接覆盖 1.0.2。
- **GpuLcd-1.0.3-portable.zip**：便携版，完整解压后运行 GpuLcd/GpuLcd.exe。
- 包里另带 **`dsh-plugin/`**：DeepSeek Harness 插件，安装见其中的 README。

### 新增

- **DeepSeek Harness 联动**：联动页新增「接收 DeepSeek Harness 任务事件」（默认开）。DSH 没有生命周期钩子，所以用一个 DSH 插件监听会话事件，翻译成和另外两位助手一样的词汇写进事件管道（client = `dsh`）。屏上使用 `dswork / dsask / dsdone.savi` 三张新动画：深蓝鲸鱼工作中、金黄感叹号等你、青绿对勾完成。
- 点阵屏第三套配色：工作 `#4D6BFE` 深蓝、等你 `#FF9F45` 金黄、完成 `#2ED3B7` 青绿，和 Claude 的暖色、Codex 的冷色都不撞。
- 三个客户端可以同时运行，状态机按「等待授权 > 工作中 > 结束提示」合并，切换时保持 15 秒不来回跳。

### 变化

- `build.cmd` 固定使用 Windows 自带的 `tar.exe`：PATH 里若有 GnuWin32 的 tar，原来的写法会静默生成不含前端的 exe。
- `package.ps1` 会把 `dsh-plugin` 一起放进安装包和便携包，并在缺失时直接报错，不再静默跳过。

### 验证

前端生产构建、C# 编译通过；状态机 50 项、点阵屏 67 项、完成补收 25 项、显示任务 15 项、运行库选择 5 项断言全部通过。DSH 插件单元测试 7 项通过（含用真实命名管道验证事件格式、以及 GpuLcd 未运行时快速放弃）。发行包校验通过：逐文件哈希、包内托管和原生库加载、安装器与便携包一致、包内工具独立完成 GIF→SAVI 转换。

### 已知

- DSH 插件在 DSH 进程内运行，需要把 `dsh-plugin` 复制进 DSH profile 的 `node_modules` 并加进 bundle 列表，然后重启 DSH；详见 `dsh-plugin/README.md`。
- 安装包未代码签名；第三方组件不适用项目源码的 MIT 许可证。
