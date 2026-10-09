# 进度与限时通知

打开“显卡屏幕控制”的“进度 / 通知”页。
- 填进度标题和真实百分比，点“显示 / 更新进度”。
- 填通知文字和持续秒数，点“显示通知”。
- 通知优先显示；期间新进度仍被接收。通知到期后恢复最新进度。
- “停止进度”只结束进度；“清除通知”只清除通知；“恢复原画面”清除两者。
- 无临时内容时恢复最新助手状态；没有助手任务则恢复之前的画面。

## 限制

原生文字通路仅支持 ASCII：英文、数字和常用符号。中文、emoji 会明确报错。
最多三行，每行最多 36 字符。字体来自屏幕固件；桌面预览只是布局示意。
进度为 0–100 的整数，由你或调用脚本提供。工具不会推测 Codex 的完成百分比。
通知为 3–300 秒。进度 30 分钟没有更新自动结束。
只在内容变化时发送，最多每两秒一次；不上传文件、不写屏幕存储、不启动厂商实时刷新定时器。

## PowerShell 示例

保持小工具运行：

    $send = '<安装目录>/GpuLcdSend.exe'
    & $send progress 45 'Downloading'
    & $send notify 10 'Download complete!'
    & $send clear-progress
    & $send clear
    & $send status

多行文字、自定义颜色和位置：

    @{
      action = 'notify'
      text = "Build complete" + [Environment]::NewLine + "All tests passed."
      seconds = 15
      color = '5DCF99'
      x = 12
      y = 8
    } | ConvertTo-Json -Compress | & $send json

真实下载进度的调用方式：

    # receivedBytes 和 totalBytes 来自你的下载器。
    $percent = [int][math]::Floor(100 * $receivedBytes / $totalBytes)
    & $send progress $percent 'Downloading'

只有在 totalBytes > 0 且数值有效时调用。命令成功返回 0，输入错误或未连接返回 1。
任何程序也可向本机命名管道 GpuLcdTool 写一行 UTF-8：

    widget {"action":"progress","title":"Build","percent":60}

读取一行回执。不要直接同时打开屏幕串口。

## 已完成验证

- 15 项输入校验、通知优先级、到期恢复状态测试。
- 实机进度 45 / 70 / 75 / 90 / 100、通知后恢复最新进度、清除后恢复助手动画。
- 用户确认 LCD test / 45% 实际画面正常。
- 测试后仍能读取屏幕设置，USB / COM 普通模式正常。
- 既有基本、轮播、图片动画、文字、实时数据、联动、存储页面保留。
