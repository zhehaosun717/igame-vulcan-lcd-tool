显卡屏幕控制 1.0.0
====================

七彩虹 iGame 火神 RTX 3070 Ti 的 LCD 3.0 侧屏独立控制工具。
不需要 iGameCenter 在后台运行，但需要它的厂商库（C:\Program Files\iGameCenter）。

运行要求
--------
- Windows 10 / 11（x64）
- 已安装 iGameCenter（只用它的库，不需要打开它）
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
