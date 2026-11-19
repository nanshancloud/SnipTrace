using System;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>
/// 系统取色对话框（取色模式）：编辑器颜色面板、设置页高亮颜色、覆盖层标注色共用。
/// 颜色统一用 0xAARRGGBB 的 uint 表示，与 <c>AnnotationStyle.Argb</c> 一致。
/// </summary>
internal static class ColorPicker
{
    public static bool TryPick(uint currentArgb, out uint pickedArgb)
    {
        pickedArgb = currentArgb;
        try
        {
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = ToDrawingColor(currentArgb)
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
            pickedArgb = ToArgb(dialog.Color);
            return true;
        }
        catch (Exception)
        {
            // 没有桌面会话 / 对话框创建失败时保持原色。
            return false;
        }
    }

    private static System.Drawing.Color ToDrawingColor(uint argb) => System.Drawing.Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    private static uint ToArgb(System.Drawing.Color color) =>
        ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
