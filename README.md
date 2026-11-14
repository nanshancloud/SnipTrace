# 屏迹 SnipTrace

**截得准，讲得清。**

Windows 桌面截图 / 标注 / GIF / 录屏工具。完全本地处理，无账号、无云端、无遥测。

- 运行时：.NET 11 + WPF（`net11.0-windows`）
- 平台：Windows 10 22H2+ / Windows 11，x64
- 设计基线：[`docs/DESIGN.md`](docs/DESIGN.md)

## 功能

**截图**

- 全局快捷键唤起冻结帧选区：`F1` 区域截图、`Ctrl+Shift+F` 当前显示器全屏、`Ctrl+Alt+F` 所有显示器全屏。
- 跨显示器连续选区，虚拟桌面物理像素坐标，显示器之间空隙导出填黑并提示。
- 比例锁定：自由 / 原始 / 16:9 / 16:10 / 4:3 / 3:2 / 1:1 / 9:16；按住 `Shift` 临时锁定当前比例。
- 实时显示宽 × 高、坐标与比例，放大镜显示局部像素与颜色值。
- 选区外 55% 遮罩，工具条自动翻转并钳制在工作区内。

**标注编辑器**

- 矩形、椭圆、箭头、画笔、高亮、文本、序号标签，支持颜色 / 线宽 / 字号 / 透明度。
- 选择、移动、缩放、框选多选，撤销重做 100 步。
- 画布缩放 25%–400%，适应窗口；导出严格 1:1 物理像素 PNG。
- 序号标签自动递增、按阅读顺序重排、重置；删除末尾编号后新号复用。
- 复制（PNG + DIB）、保存 PNG、钉图置顶。

**GIF 录制**

- 选区录制循环 GIF，5 / 10 / 12 / 20 / 25 FPS，3 秒倒计时。
- 暂停 / 继续 / 停止 / 取消，实时显示时长、帧数、体积。
- 最大 120 秒、2400 帧保护，最大边长 1280 / 1920 等比缩放，可选高质量抖动。
- 自研流式编码：中位切分量化 ≤256 色、局部调色板、LZW、连续重复帧合并。

**屏幕录制**

- 单显示器选区 H.264 MP4，24 / 30 / 60 FPS，NV12 + Media Foundation SinkWriter。
- 硬件编码优先，失败自动回退软件编码。
- 奇数宽高自动右/下裁 1 px，不拉伸画面。
- 暂停不计入成片时间；显示器分辨率或排列变化时自动停止并完成封装。
- 录制边框与悬浮控制条位于画面之外；磁盘空间不足时先预警，见底时停止并封装已录内容。

## 快捷键

| 快捷键 | 功能 |
|---|---|
| `F1` | 区域截图 |
| `Ctrl+Shift+F` | 全屏截图（当前显示器） |
| `Ctrl+Alt+F` | 全屏截图（所有显示器） |
| `Ctrl+Shift+G` | GIF 录制 |
| `Ctrl+Shift+R` | 屏幕录制 |
| `Enter` / `Esc` | 确认 / 取消选区 |
| `方向键` / `Shift`+`方向键` | 微调选区 1 px / 10 px |
| `Ctrl+Z` / `Ctrl+Shift+Z`、`Ctrl+Y` | 撤销 / 重做（编辑器） |
| `Ctrl+C` / `Ctrl+S` | 复制 / 保存（编辑器） |
| `V R E A P H T N` | 编辑器工具：选择 / 矩形 / 椭圆 / 箭头 / 画笔 / 高亮 / 文本 / 序号 |
| `+` / `-` / `Ctrl+0` | 放大 / 缩小 / 适应窗口 |
| `Delete` | 删除选中标注 |

快捷键冲突会在设置页拦截并提示，托盘菜单为全部功能提供备用入口。

## 使用流程

1. 按 `F1`，主窗口自动隐藏并冻结桌面。
2. 拖动选择区域（可跨显示器），按回车或点击工具条「标注」进入编辑器。
3. 添加标注与序号后复制、保存或钉图。
4. GIF / 录屏：选择区域后倒计时开始，控制条可暂停、停止或取消；结束后结果窗口支持打开、另存为、复制。

## 数据位置

| 内容 | 路径 |
|---|---|
| 设置 | `%LOCALAPPDATA%\SnipTrace\settings.json` |
| 最近记录索引 | `%LOCALAPPDATA%\SnipTrace\recent.json` |
| 缩略图缓存 | `%LOCALAPPDATA%\SnipTrace\Thumbnails` |
| 临时文件 | `%LOCALAPPDATA%\SnipTrace\Temp`（启动时清理 24 小时前的文件） |
| 截图 | `%USERPROFILE%\Pictures\SnipTrace\Screenshots`（可在设置中修改） |
| 录屏 | `%USERPROFILE%\Videos\SnipTrace`（可在设置中修改） |

文件名冲突时自动追加序号，不静默覆盖。

## 工程结构

```
SnipTrace.slnx
src/
  SnipTrace.Domain/        领域层：文档、标注、编号、比例、录制会话、设置、布局断点
  SnipTrace.App/           WPF 应用
    Infrastructure/        捕获、GIF/MP4 编码、录制引擎、托盘、快捷键、存储
    Presentation/          主窗口、选区覆盖层、编辑器、录制控制、结果预览
    Themes/Theme.xaml      深色珊瑚主题
tests/SnipTrace.Tests/     控制台测试（无需测试框架）
tools/GifProbe/            GIF 输出探测工具
docs/DESIGN.md             设计方案（唯一设计基线）
```

分层约定：领域层不依赖 WPF；UI 线程不执行截图、编码或磁盘写入。

## 构建与测试

```powershell
dotnet build SnipTrace.slnx -c Debug
dotnet run --project tests/SnipTrace.Tests
dotnet run --project src/SnipTrace.App          # 启动应用
dotnet run --project src/SnipTrace.App -- --selftest   # GIF / MP4 / 导出管线自检
```

发布可执行版本：

```powershell
dotnet publish src/SnipTrace.App -c Release -r win-x64 --self-contained false -o dist/win-x64
```

产物在 `dist/win-x64/`（`dist/` 已被 git 忽略），直接运行 `SnipTrace.exe` 即可；该版本依赖 .NET 11 桌面运行时。

验证一个构建是否可用：

```powershell
.\dist\win-x64\SnipTrace.exe --selftest   # 期望 7 行 PASS，退出码 0
```

`--selftest` 会在临时目录真实生成一张 GIF 与一个 MP4 并校验尺寸/颜色/循环标记/导出像素，验证完自动清理。

## 已知限制

- 受保护内容（DRM）截图可能黑屏，HDR 内容可能颜色失真。
- 显示器之间的物理空隙无法捕获，跨屏截图中显示为黑色（HUD 会提示）。
- 屏幕录制仅支持单显示器选区；跨屏录制为后续增强项。
- 4K 60 FPS 依赖 GDI 抓屏，CPU 占用偏高。
