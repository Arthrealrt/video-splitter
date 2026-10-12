# 视频分段工具 / Video Splitter

Windows 桌面小工具，将一个或多个视频从开头连续切成指定秒数的 MP4 片段。

## 功能

- 自定义每段 **0.1–86400 秒**，界面最多支持两位小数。
- 保留 **6 秒 / 15 秒**快捷按钮。
- 批量选择或拖入视频，可选择保留或丢弃不足一段的尾段。
- 支持 FFmpeg 可解码的 MP4、MOV、MKV、AVI、WebM 等常见视频。
- 导出 H.264 视频和 AAC 音频，支持无音频视频。
- 进度、错误日志、取消处理；新建独立结果文件夹，原文件不改写。
- 视频在本机处理，不上传。

例如：19 秒视频，设置 8 秒，得到 8、8、3 秒三个片段。

## 运行与构建

需要 Windows 10 / 11 x64 和 .NET Framework 4.5 或更新版本。

在项目目录执行：

```powershell
.\download-ffmpeg.ps1
.\build.ps1
.\视频分段工具.exe
```

下载脚本获取固定版本的 FFmpeg 9.0.2 Windows 构建，并校验 SHA-256。
运行时 `ffmpeg.exe` 和 `ffprobe.exe` 必须与主程序在同一目录。
仓库包含完整源码；视频处理引擎通过脚本下载，不纳入 Git 历史。

生成包含视频引擎的便携软件包：

```powershell
.\package.ps1
```

结果保存至 `dist/video-splitter-v1.1.0-windows-x64.zip`。

## 命令行

```powershell
.\视频分段工具.exe --split "input.mp4" "output" 7.5
.\视频分段工具.exe --split "input.mp4" "output" 30 --discard-tail
```

小数使用英文句点。退出码：0 成功，1 处理错误，2 参数格式错误。

## 验证

安装 Python 3 后，在构建并下载引擎后运行：

```powershell
python .\tests\verify.py
```

测试会生成合成视频，验证常见格式、自定义时长、尾段处理、无音频、中文路径、输出解码、分段起始帧和非法参数。素材及测试报告保存于 `work/`，无需个人视频。

## 说明

准确分段通过重新编码实现，画质可能有轻微损失。片段实际时长受源帧率及音频采样影响，可能存在一帧或采样级误差。只保留第一条音轨，不导出字幕、章节和其他音轨。HDR 没有专门色调映射。

主程序及源码采用 MIT 许可；FFmpeg / FFprobe 为独立调用的 GPL v3 构建，来源和许可位于 `licenses/`。
