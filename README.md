# 屏迹 SnipTrace

**截得准，讲得清。**

屏迹是一款 Windows 桌面截图与录屏工具：按 `F1` 冻结桌面框选，支持跨屏连续选区、比例锁定与窗口吸附；选区工具条可直接标注图形（矩形 / 椭圆 / 直线）、箭头、画笔、文字、序号与马赛克，复制即带标注进剪贴板，「精标」还能进编辑器继续调整——多段折线箭头、荧光笔、七种序号形状、撤销重做一应俱全。截图可一键钉在桌面置顶，右键复制、另存或精修；GIF 与 MP4 录制支持暂停、预览与互转。全部本地处理，无账号、无云端、无遥测。

- 运行时：.NET 11 + WPF（`net11.0-windows`），Windows 10 22H2+ / Windows 11
- 设计基线：[`docs/DESIGN.md`](docs/DESIGN.md)

## 功能

- **截图**：区域 / 全屏 / 所有显示器；跨屏连续选区、比例锁定、窗口吸附、放大镜；选区四角可拖动改大小。
- **选区工具条**：Snipaste 式单行图标条，带 回退 / 前进 两步历史；设置页直接渲染同款工具条，点按钮开关、按住拖动排序。已标注后仍可调选区，标注跟着贴合。
- **覆盖层标注**：图形（矩形 / 椭圆 / 直线）、箭头（箭头 / 直线）、画笔、文字、序号、马赛克笔刷（沿拖动轨迹打码）；图形 / 箭头点按钮弹层切换子选项并记住上次选择，滚轮微调当前标注（马赛克改块大小），`Ctrl+Z` / `Ctrl+Y` 回退前进。
- **标注编辑器**：多段折线箭头（单向 / 双向 / 无头 / 直角）、高亮荧光笔（固定 38% 不透明度）、马赛克笔刷、文本、序号标签（7 种形状 × 填充 / 边框）、橡皮；选中文本 / 图形后浮出样式条（字号或线宽 + 颜色，参考微信截图），移动缩放多选、撤销重做、1:1 导出、清除全部。
- **钉图**：置顶贴图，保留可编辑标注——鼠标悬浮到标注上即可选中，直接拖动或拖四角调整大小；右键唤出工具条（复制 / 另存为 / 存到目录 / 精修）；滚轮缩放，`Alt+滚轮` 半透明，复制 / 保存时把标注烘焙进图片。
- **录制**：GIF（5–25 FPS，自研流式编码）与 MP4（H.264 硬件优先，暂停不计入成片）；录制前控制条 + 录制预览，MP4 可一键转 GIF；可选鼠标高亮。
- **外观**：深浅色主题（新装默认浅色）、标注字体可换、标注 / 文本颜色走系统取色对话框（也可滚轮切预设色）、托盘图标自适应任务栏明暗（也可固定彩色 / 浅色）。
- **其他**：设置即改即生效（悬浮显示说明）、设置文件带备份，覆盖升级不会重置本地配置、截图自动保存、开机自动启动（静默驻留托盘）、启动时检查更新（可在设置关闭，关于页可手动检查）、三种独立 / 统一快捷键、关于页。

默认快捷键：`F1` 区域截图、`Ctrl+Shift+F` 全屏、`Ctrl+Alt+F` 所有显示器、`Ctrl+Shift+G` GIF、`Ctrl+Shift+R` 录屏，均可在设置中点一下后直接按键录入（组合键按键帽展示，统一快捷键模式下 GIF / 录屏两行自动隐藏）。选区操作：`Enter` 完成、`Esc` 取消、方向键微调；编辑器内 `V R E A P H M T N` 切工具、`Ctrl+Z/Y` 撤销重做、`Ctrl+C/S` 复制保存。

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
dotnet run --project tests/SnipTrace.Tests                # 单元 / 集成测试
dotnet run --project src/SnipTrace                        # 启动应用（--selftest 跑自检，--page settings|about 直达页面）
```

## 分发包

`tools/package.ps1` 一次产出四个包，版本取 `SnipTrace.csproj` 的 `Version`，文件名带版本与处理器架构，输出到 `dist/packages`：

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

压缩包内顶层目录固定为 `SnipTrace`；MSI 为 per-machine 安装，附带开始菜单与桌面快捷方式。两种分发共用同一 UpgradeCode，可互相升级、二选一安装；发布文件不含 `.pdb`。

## 工程结构

```
src/SnipTrace.Domain/    领域层：文档、标注、编号、比例、录制会话、设置、布局断点
src/SnipTrace/           WPF 应用：Infrastructure（捕获 / 编码 / 托盘 / 快捷键 / 存储）
                         Presentation（主窗口 / 选区覆盖层 / 编辑器 / 录制控制 / 预览）
                         Themes（Brushes.Dark|Light.xaml 深浅色画刷 + Theme.xaml 样式）
tests/SnipTrace.Tests/   控制台测试（无需测试框架）
docs/DESIGN.md           设计方案（唯一设计基线）
docs/images/             测试与验收截图（本地生成，不入库）
```

分层约定：领域层不依赖 WPF；UI 线程不执行截图、编码或磁盘写入。

## 已知限制

- 受保护内容（DRM）截图可能黑屏，HDR 内容可能颜色失真。
- 显示器之间的物理空隙无法捕获，跨屏截图中显示为黑色（放大镜会提示）。
- 屏幕录制仅支持单显示器选区；跨屏录制为后续增强项。
- 4K 60 FPS 依赖 GDI 抓屏，CPU 占用偏高。
