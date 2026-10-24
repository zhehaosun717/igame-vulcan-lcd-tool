# GpuLcd 1.0.6 + DSH 插件 0.1.2

修复 **1.0.5 随包的 DSH 插件无法挂载**的问题：0.1.1 的 inject 声明用了当前 Cordis 不支持的对象写法（对象键会被当作服务名，插件永远等一个叫 optional 的服务），实际启动时插件不激活、不发送任何事件。1.0.5 随包里的插件请丢弃，不要安装。

## 修复

- 插件 0.1.2：inject 改为 Cordis 支持的数组写法 `['agents']`，并加了防回退断言。已在 DSH 0.2.1-rc.2 的隔离 headless profile 里实际挂载验证：插件激活，开始 / 工具进行中 / 会话清理事件按预期送达接收管道。
- 宿主功能与 1.0.5 完全一致，只有打包内容里的插件不同。

## 下载

- **GpuLcd-Setup-1.0.6.exe**：安装版，按用户安装，可直接覆盖。
- **GpuLcd-1.0.6-portable.zip**：便携版。
- **dsh-gpulcd-status-0.1.2.zip**：DeepSeek Harness 插件（安装/便携包内也带同一份）。
- **SHA256SUMS.txt**：校验值。

## 说明

- 插件 0.1.2 必须配套 **GpuLcd >=1.0.5**（1.0.4 的接收器不认识原子等待恢复字段）。
- 1.0.5 的其他内容（DSH 联动、灯效、状态恢复）不受影响，1.0.6 只为纠正插件。
- 已安装 1.0.5 且插件已能工作的用户不需要重装宿主；用 1.0.6 的插件包替换即可。

### English summary

The plugin bundled with 1.0.5 never mounts (unsupported inject declaration). 0.1.2 fixes the declaration and was verified to activate in an isolated headless profile. The host is unchanged from 1.0.5.
