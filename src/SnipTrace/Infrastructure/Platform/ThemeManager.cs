using System;
using System.Windows;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>深浅色主题切换：替换应用级画刷字典，样式通过 DynamicResource 实时刷新。</summary>
public static class ThemeManager
{
    public static void Apply(string theme)
    {
        string normalized = ThemeModes.Normalize(theme);
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        var brushes = new ResourceDictionary
        {
            Source = new Uri(
                normalized == ThemeModes.Light
                    ? "pack://application:,,,/Themes/Brushes.Light.xaml"
                    : "pack://application:,,,/Themes/Brushes.Dark.xaml")
        };

        // 画刷字典固定在第一个位置，供 StaticResource 引用按旧顺序解析。
        if (dictionaries.Count == 0)
        {
            dictionaries.Insert(0, brushes);
            return;
        }

        dictionaries[0] = brushes;
    }
}
