显卡屏幕控制 1.0.1
====================

七彩虹 iGame 火神 RTX 3070 Ti 的 LCD 3.0 侧屏独立控制工具。
已附带 LCD 厂商库、SAVI / FFmpeg 转换工具和私有 VC90 运行库，无需安装 iGameCenter。

运行要求
--------
- Windows 10 / 11（x64）
- 保持 vendor 文件夹与 GpuLcd.exe 在一起，不要只复制 exe。
- Microsoft Edge WebView2 运行时（Windows 11 自带；Windows 10 多数也有）。
  没有的话程序会自动改用经典界面。

使用
----
- 双击 GpuLcd.exe 打开主界面。关闭窗口后，如果联动 / 熄灯 / 实时刷新还在工作，
  程序会留在右下角托盘继续运行；右键托盘图标选“退出”才会真正关闭。
- 再次双击程序会把窗口叫回来。
- 托盘菜单里的“经典界面（备用）”是旧版窗口，新界面出问题时可以用。
- 命令行：GpuLcdSend.exe progress 50 Build | notify 10 Done | clear | status | links

重要提醒
--------
- 上传图片 / 动画期间请不要关闭程序、休眠或断开电源或 USB。上传不能取消。
  如果上传失败后屏幕卡住（顶部变橙色），需要整机断电：关机后关电源开关 30 秒再开。
- 程序不会上传 .savi 文件，也不会播放屏幕上不存在的文件，这是为了避免把屏幕写坏。

数据位置
--------
- 设置、日志：%APPDATA%\GpuLcdTool
- 新界面的浏览器数据：%LOCALAPPDATA%\GpuLcdTool\webview2
卸载时可以选择是否一并删除这些数据。
