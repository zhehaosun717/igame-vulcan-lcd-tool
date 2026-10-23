# dsh-gpulcd-status

把 **DeepSeek Harness** 会话的工作状态发送给 GpuLcd，让显卡屏动画、SignalRGB 灯效和 Dot Matrix Studio 点阵屏跟着 DSH 一起变化——和 Claude Code / Codex 的两条钩子并列的第三条来源。

## 它做什么

GpuLcd 原本只认 Claude Code 和 Codex 的生命周期钩子。DSH 没有那种钩子，但有插件事件，所以这个插件在 DSH 进程里监听事件，翻译成 GpuLcd 认识的同一套词汇，写进它的 `GpuLcdEvents` 命名管道（client = `dsh`）。GpuLcd 那边不需要知道 DSH 内部长什么样。

| DSH 事件 | 发给 GpuLcd | 屏上效果 |
|---|---|---|
| `agent/status` → running | `UserPromptSubmit` | 开始工作（新的一轮） |
| `tools/pre-execute` | `PreToolUse` | 保持「工作中」 |
| `approval/request` | `PermissionRequest` | 「等你确认」 |
| `user-questions/request` | `PermissionRequest` | 「等你回答」 |
| `agent/status` → idle | `Stop` | 本轮结束（30 秒后恢复默认画面） |
| `agent/disposed` | `SessionEnd` | 立刻清掉该会话的状态 |

不发送任何对话内容，只有事件类型、会话 id 和轮次。

## 安装

1. 先安装并运行 **GpuLcd 1.0.3 或更新版本**（联动页里要有「接收 DeepSeek Harness 任务事件」这一行）。
2. 把本目录放进 DSH 的 profile，并把 `dsh-gpulcd-status` 加进 profile 的 bundle 列表：

```bash
# 源码位置（二选一）：随 GpuLcd release 附带的 zip，或仓库里的 dsh-plugin/ 目录
PROFILE="$HOME/.dsh/profiles/desktop"
cp -r dsh-plugin "$PROFILE/node_modules/dsh-gpulcd-status"
```

3. 在 `$PROFILE/package.json` 的 `dsh.profile.bundles` 数组末尾加上 `"dsh-gpulcd-status"`。
4. 重启 DSH。

## 开关

GpuLcd 联动页勾选「接收 DeepSeek Harness 任务事件」（默认开着）。关掉它，DSH 的事件仍然会送过来，但不会驱动屏幕。

## 配色

和点阵屏一致，和另外两位助手都不同：

| 状态 | DSH | Claude Code | Codex |
|---|---|---|---|
| 工作中 | `#4D6BFE` 深蓝 | `#D97757` 陶橙 | `#3C82FF` 蓝 |
| 等你 | `#FF9F45` 金黄 | `#F0B43C` 琥珀 | `#B478FF` 紫 |
| 完成 | `#2ED3B7` 青绿 | `#78C878` 草绿 | `#3CDCDC` 青 |

## 开发

```bash
node C:/path/to/typescript/bin/tsc -p tsconfig.json   # 输出到 lib/
node --test test/                                      # 单元测试（含假管道）
```

`lib/` 是构建产物，安装时直接用它。改了 `src/` 记得重新编译。

## 限制

- 只识别 DSH 的 main 会话与子代理会话；子代理的事件按各自的 session id 上报，不会与主会话混在一起。
- 插件在 DSH 进程内运行；GpuLcd 没开时事件在 150–400 ms 内丢弃，不会拖慢会话。
