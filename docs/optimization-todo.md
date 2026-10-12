历史审查笔记：第 1、2 项上传互斥已有后续修改；其余条目未经本次同步重新审计。以当前代码和测试为准。

# 待优化清单

一次代码审查后整理的改进点，按重要性排序。

## 关键（可能把屏幕写坏，优先修）

1. **上传时串口缺少互斥。** `GpuLcd.cs` 的 `DoUpload` 在后台线程做转换+上传，但 `linkTimer`（1s）和 `widgetTimer`（0.5s）仍在 UI 线程触发串口写入（`TickLinks` → `PlayExternal` → `PlayMovie`；`TickWidgets` → `DrawNative`）。上传途中到达的钩子事件 / 空闲恢复 / 挂件指令会与上传 worker 并发写同一串口，字节交错 → 屏幕卡死，需断电恢复。
   修：加一个共享 `uploading` 标志，所有串口写入路径（`PlayExternal`、`DrawNative`、`TickLinks`、`TickWidgets`）都检查它，上传期间直接跳过。
2. **管道的 `play`/`flash`/`widget` 指令上传时未拦。** `HandleCommand`、`WidgetCommand` 只检查 `connected` 和存储维护标志，没检查是否在上传。外部指令（GpuLcdSend、钩子）能插进上传。
   修：这些分支加 `if(!btnUpload.Enabled) return "ERR 上传进行中";`。

## 重要

3. **UI 线程上有阻塞 `Thread.Sleep`。** `Quiet<T>` 睡 1200ms，被 `RefreshFiles`/`UseSelected`/`DeleteSelected`/`NightStatus` 同步调用；夜间模式多处 150+200/1200ms 也在 UI 线程。期间窗口冻结，且因为管道派发走 `Invoke`，管道指令和钩子事件被一起堵住。
   修：把 `Quiet` 和夜间硬件操作移到后台线程（参考 `StorageForm.Run` 的 await 写法）。
4. **`StartPipe` 后台循环没有退出路径。** `while(true)` 不检查 `IsDisposed`，窗口关闭后仍可能对死句柄 `Invoke`，且连接断开后每 500ms 空转。
   修：改为 `while(!IsDisposed)`，捕获 `ObjectDisposedException` 后退出（参考 `StartLinks`）。
5. **`signalBridge` 在 UI 线程被 Dispose 时，读取 Task 可能正在用它**（`StartSignalBridge` 的 `Task.Run` 可能阻塞在 `reader.Read`），会误杀/重启 SignalRGB MCP 进程。
   修：用忙标志或锁保护 Dispose。
6. **两个对话框未 Dispose**（`OpenFileDialog`、`ColorDialog`），每次点按钮漏一个 GDI/句柄。
   修：`using` 包裹。

## 次要

7. `SignalReadClient.Request` 用 `ReadLineAsync().Wait()` 忙等，丢弃不匹配的 JSON-RPC 帧——对异步通知脆弱。
8. `Sensors` 的 `PerformanceCounter` 静态对象从不 Dispose（进程级句柄）。
9. 多处空 `catch { }` 吞真实错误：`Cfg.Load`/`Save` 对损坏/不可写的 `settings.json` 静默丢弃，会让工具“忘记”设置且无诊断。至少写进 `log.txt`。
10. 空闲时定时器仍每秒轮询（`widgetTimer` 500ms 常开、`TickLinks` 每秒读会话记录）。可在无活动时退避。
11. 客户端分发逻辑（前缀 `ab`/`cx`、状态标签、事件字符串）在 `Integrations.cs` 多处和 `LegacyAsstB` 重复；钩子→事件映射在 `Hook.cs` 和 `LegacyAsstB` 重复。可抽成一个 `ClientProfile` 辅助类，顺带消除重命名隐患。
12. 硬编码路径：厂商库 `C:\Program Files\iGameCenter`、SignalRGB 端口 16034、MCP 发现路径 `%LocalAppData%\VortxEngine\app-*`、钩子命令绝对路径。可做成配置项。
13. 死代码：`SignalBridge` 里不再使用的 HTTP 监听注释、`StorageForm.Preview` 恒为 false、`DisplayRequest.font` UI 未暴露等。
