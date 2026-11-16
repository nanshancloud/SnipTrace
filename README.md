# 屏迹 SnipTrace

**截得准，讲得清。**

Windows 桌面截图 / 标注 / GIF / 录屏工具。完全本地处理，无账号、无云端、无遥测。

- 运行时：.NET 11 + WPF（`net11.0-windows`），Windows 10 22H2+ / Windows 11
- 详细设计基线：[`docs/DESIGN.md`](docs/DESIGN.md)

## 功能一览

| 能力 | 说明 |
|---|---|
| 区域截图 | 全局快捷键冻结桌面 → 框选；跨显示器连续选区、比例锁定、窗口吸附（参考 Snipaste）；放大镜仅在拖动框选时出现；选区四角可拖动改大小 |
| 选区工具条 | Snipaste 式单行图标条，按钮可增删 / 排序（设置页「选区工具条」）；悬浮介绍显示功能与快捷键 |
| 覆盖层标注 | 工具条直接选 选择 / 箭头 / 画笔 / 橡皮 / 矩形 / 椭圆 / 文字 / 序号，在截图上直标；已标注后仍可拖角调整选区，标注跟着贴合；滚轮微调当前标注（矩形 / 椭圆 / 箭头改线宽，文字 / 序号改字号） |
| 覆盖层选择 | 「选择」工具点选图形 / 文字 / 序号 / 笔迹（后画的优先），拖动整体移动，`Delete` 删除 |
| 复制与精标 | 工具条「复制」直接烘焙标注进剪贴板；「精标」进入标注编辑器继续调整，两者图标 + 文字标签区分 |
| 右键行为 | 选区界面右键直接取消本次截图（拖动中右键只中断该次拖拽）；右键菜单只保留在钉图窗口 |
| 序号标记 | 工具条「序号」按钮：点击选区自动递增编号；弹出层可选 圆 / 三角 / 方 / 五边 / 六边 / 正心形 ♥ / 圆角 形状，以及 填充 / 边框 两种模式；编辑器里可开「改样式后重新计数」 |
| 钉图置顶 | 右键（或悬停）唤出 Snipaste 式工具条：复制 / 另存为 / 存到目录 / 缩放百分比 / 更多 / 关闭；滚轮缩放，`Alt+滚轮` 半透明 |
| 标注编辑器 | 矩形 / 椭圆 / 箭头（多段折线、单向 / 双向 / 无头 / 直角）/ 画笔 / 高亮 / 文本 / 序号标签 / 橡皮，移动缩放多选、撤销重做、1:1 导出 PNG；虚线选框与调整手柄只在「选择」工具下出现 |
| 面板与工具联动 | 点右侧「箭头模式」自动切到箭头工具；点「序号形状 / 填充方式」自动切到序号工具，并作为新建号标的默认样式 |
| 高亮半透明 | 高亮按荧光笔渲染（固定 38% 不透明度，可再叠加「透明度」），底下文字与线条始终看得见；覆盖层 / 编辑器 / 导出一致 |
| 领域层几何 | 正心形 ♥（`BadgeShapes`）、箭头端头与多段模型（`ArrowGeometry` + `ShapeAnnotation`）、改样式后重新计数（`AnnotationEditing.ApplyBadgeStyle`）全在不依赖 WPF 的领域层，由单测直接覆盖 |
| 撤销重做 | 只记录真有变化的编辑：选中、点空白、重复点同一样式等空操作不会清空重做栈；撤销 / 重做后清掉过期选中框；按钮按可用状态置灰（`Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Y`） |
| 选择与编辑 | 「选择」工具点选后拖动移动、拉手柄缩放；双击文本改内容；箭头显示端点手柄，拖端点重新定向、拖中间点改折线 |
| 橡皮 | 编辑器与截图界面的「橡皮」共用同一套规则：只擦画笔与高亮笔画，文本 / 箭头 / 序号 / 形状不受影响，一次拖动合并为一步撤销 |
| GIF 录制 | 5–25 FPS 循环动图，自研流式编码（量化 / 局部调色板 / LZW / 重复帧合并） |
| MP4 录制 | 选区 H.264 编码，硬件优先；暂停不计入成片，失败不留半成品 |
| 录制控制条 | QQ 录屏式：先选格式（MP4 / GIF）与清晰度，再倒计时开始；录制中可暂停 / 停止 / 取消 |
| 录制预览 | 「制作MP4 / 制作GIF」预览窗口：播放 / 进度 / 时间码 / 循环 / 另存为，MP4 可一键转 GIF |
| 鼠标高亮 | 录制时在光标处绘制半透明色圈，颜色 / 大小 / 不透明度可配置，GIF 与 MP4 通用 |
| 深浅色主题 | 设置页切换深色 / 浅色，即时生效；深色沿用原有 `#0B0F14 / #131A23` 层次，不做死黑 |
| 设置即改即生效 | 设置页没有保存按钮：目录、开关、快捷键、主题、高亮参数改动后自动写盘并重注册快捷键 |
| 自动保存 | 「截图后自动保存」开关打开后，截图静默存入截图目录；关闭时保存按钮弹出另存为对话框（对话框期间截图画面与标注保持显示，取消保存后才退出截图） |
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
.\dist\win-x64\SnipTrace.exe --selftest                   # 自检：GIF/MP4/导出管线 + UI 冒烟 + 设置即时生效 + 画布交互 + 覆盖层与设置页，期望 46 行 PASS
.\dist\win-x64\SnipTrace.exe --preview 文件.gif|.mp4      # 打开录制预览窗口
.\dist\win-x64\SnipTrace.exe --page settings              # 直接打开指定页面
```

## 分发包

`tools/package.ps1` 一次产出四个包（版本取 `SnipTrace.csproj` 的 `Version`，文件名带版本与处理器架构），输出到 `dist/packages`：

| 类型 | 文件 | 运行时 |
|---|---|---|
| 压缩包 | `SnipTrace-<版本>-win-x64-selfcontained.zip` | 内置 .NET 11 桌面运行时，解压即用 |
| 安装包 | `SnipTrace-<版本>-win-x64-selfcontained-setup.msi` | 同上，装到 `Program Files\SnipTrace` |
| 压缩包 | `SnipTrace-<版本>-win-x64-fx-dependent.zip` | 需系统安装 .NET 11 桌面运行时 |
| 安装包 | `SnipTrace-<版本>-win-x64-fx-dependent-setup.msi` | 同上 |

```powershell
dotnet tool install --global wix --version 5.*   # 首次生成 MSI 需要 WiX v5
pwsh -File tools/package.ps1                     # 可选 -Runtime win-x64 -Arch x64 -OutputDir <目录>
```

压缩包内顶层目录固定为 `SnipTrace`；MSI 为 per-machine 安装，附带开始菜单与桌面快捷方式。
两种分发共用同一 UpgradeCode，可互相升级、二选一安装。发布文件不含 `.pdb`。

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
