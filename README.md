# 屏迹 SnipTrace

**截得准，讲得清。**

Windows 桌面截图 / 标注 / GIF / 录屏工具。完全本地处理，无账号、无云端、无遥测。

- 运行时：.NET 11 + WPF（`net11.0-windows`），Windows 10 22H2+ / Windows 11
- 详细设计基线：[`docs/DESIGN.md`](docs/DESIGN.md)

## 功能一览

| 能力 | 说明 |
|---|---|
| 区域截图 | 全局快捷键冻结桌面 → 框选；跨显示器连续选区、比例锁定、像素级放大镜、窗口吸附（参考 Snipaste） |
| 选区工具条 | Snipaste 式单行图标条 + 右键快捷菜单；悬浮介绍显示功能与快捷键 |
| 覆盖层标注 | 工具条直接选箭头 / 画笔 / 矩形 / 椭圆 / 文字，在截图上直标；也可「完成」进编辑器精标 |
| 标注编辑器 | 矩形 / 椭圆 / 箭头 / 画笔 / 高亮 / 文本 / 序号标签，移动缩放多选、撤销重做、1:1 导出 PNG |
| GIF 录制 | 5–25 FPS 循环动图，自研流式编码（量化 / 局部调色板 / LZW / 重复帧合并） |
| MP4 录制 | 选区 H.264 编码，硬件优先；暂停不计入成片，失败不留半成品 |
| 录制控制条 | QQ 录屏式：先选格式（MP4 / GIF）与清晰度，再倒计时开始；录制中可暂停 / 停止 / 取消 |
| 录制预览 | 「制作MP4 / 制作GIF」预览窗口：播放 / 进度 / 时间码 / 循环 / 另存为，MP4 可一键转 GIF |
| 鼠标高亮 | 录制时在光标处绘制半透明色圈，颜色 / 大小 / 不透明度可配置，GIF 与 MP4 通用 |
| 深浅色主题 | 设置页切换深色 / 浅色，即时生效 |
| 自动保存 | 「截图后自动保存」开关打开后，截图静默存入截图目录；关闭时保存按钮弹出另存为对话框 |
| 快捷键模式 | 「三种独立快捷键」或「统一快捷键」（唤起选区后在工具条选 GIF / 录屏 / 完成）；快捷键注册失败可关闭弹窗提醒 |

默认快捷键：`F1` 区域截图、`Ctrl+Shift+F` 全屏、`Ctrl+Alt+F` 所有显示器、`Ctrl+Shift+G` GIF、`Ctrl+Shift+R` 录屏，均可在设置中修改。选区操作：`Enter` 完成、`Esc` 取消、方向键微调；编辑器内 `V R E A P H T N` 切工具、`Ctrl+Z/Y` 撤销重做、`Ctrl+C/S` 复制保存。

## 数据位置

| 内容 | 路径 |
|---|---|
| 设置 / 最近记录 | `%LOCALAPPDATA%\SnipTrace` |
| 临时文件 | `%LOCALAPPDATA%\SnipTrace\Temp`（启动时清理 24 小时前的文件） |
| 截图 / 录屏输出 | `图片\SnipTrace\Screenshots`、`视频\SnipTrace`（可在设置中修改） |

文件名冲突自动追加序号，不静默覆盖。

## 构建与运行

```powershell
dotnet build SnipTrace.slnx -c Debug
dotnet run --project tests/SnipTrace.Tests                # 单元/集成测试
dotnet run --project src/SnipTrace                        # 启动应用
dotnet publish src/SnipTrace -c Release -r win-x64 --self-contained false -o dist/win-x64
```

验证构建可用：

```powershell
.\dist\win-x64\SnipTrace.exe --selftest                   # 自检：GIF/MP4/导出管线 + UI 冒烟，期望全部 PASS
.\dist\win-x64\SnipTrace.exe --preview 文件.gif|.mp4      # 打开录制预览窗口
.\dist\win-x64\SnipTrace.exe --page settings              # 直接打开指定页面
```

## 工程结构

```
src/SnipTrace.Domain/    领域层：文档、标注、编号、比例、录制会话、设置、布局断点
src/SnipTrace/           WPF 应用：Infrastructure（捕获 / 编码 / 托盘 / 快捷键 / 存储）
                         Presentation（主窗口 / 选区覆盖层 / 编辑器 / 录制控制 / 预览）
                         Themes（Brushes.Dark|Light.xaml 深浅色画刷 + Theme.xaml 样式）
tests/SnipTrace.Tests/   控制台测试（无需测试框架）
docs/DESIGN.md           设计方案（唯一设计基线）
```

分层约定：领域层不依赖 WPF；UI 线程不执行截图、编码或磁盘写入。

## 已知限制

- 受保护内容（DRM）截图可能黑屏，HDR 内容可能颜色失真。
- 显示器之间的物理空隙无法捕获，跨屏截图中显示为黑色（放大镜会提示）。
- 屏幕录制仅支持单显示器选区；跨屏录制为后续增强项。
- 4K 60 FPS 依赖 GDI 抓屏，CPU 占用偏高。
