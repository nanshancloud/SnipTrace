# SnipTrace

截图、标注、钉图与录屏，全部本地完成。

- **截图**：`F1` 冻结桌面框选，跨屏选区、比例锁定、窗口吸附，标注后直接复制或保存
- **标注**：矩形、椭圆、箭头、画笔、文字、序号与马赛克；选区工具条即时标注，编辑器支持撤销重做与多段箭头
- **钉图**：截图置顶桌面，标注可继续编辑，滚轮缩放、`Alt+滚轮` 半透明
- **录制**：GIF 与 H.264 MP4，支持暂停、预览与互转
- **隐私**：无账号、无云端、无遥测；深浅色主题、多语言与快捷键自定义

Windows 10 22H2+ / Windows 11，.NET 11 + WPF。

```powershell
dotnet build SnipTrace.slnx -c Release
dotnet run --project src/SnipTrace -- --selftest
pwsh -File tools/package.ps1    # zip 与 MSI，内置运行时或框架依赖
```

默认安装到 `C:\Program Files\SnipTrace`。MIT License。
