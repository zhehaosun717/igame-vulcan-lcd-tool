# 第三方组件

本项目的 MIT 许可证仅适用于本项目自己的源码，不授予第三方二进制的权利。

私有发布包包含从本机 iGameCenter 安装目录复制的 LCD / NVAPI 库、SAVI 转换工具和相关编解码库。厂商原始说明保留在 `vendor/iGameCenter/License.txt`；FFmpeg 自报的版本、构建选项和许可文本位于 `licenses/`。此次 FFmpeg 二进制自报 GPL v3 或更高版本，与厂商总说明中的 LGPL 概述不同，应以具体组件许可为准。

其他依赖包括 Microsoft WebView2 SDK、Microsoft Visual C++ 2008 x86 CRT、Vue / Nuxt UI 及前端字体，权利属于各自权利人。CRT 按私有程序集部署，不覆盖 Windows 系统文件；WebView2 Runtime 仍由 Windows / Microsoft 提供，未打包浏览器运行时。

这些附件用于当前私有仓库的个人硬件使用。本次打包没有确认厂商组件可公开再分发或用于商业用途，不应将本项目 MIT 许可证解读为此类授权。
