using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>界面语言：替换应用级字符串字典，XAML 通过 DynamicResource 实时刷新。</summary>
public sealed record LanguageOption(string Code, string DisplayName);

public static class LanguageManager
{
    public static IReadOnlyList<LanguageOption> Supported { get; } = new[]
    {
        new LanguageOption(LanguageModes.English, "English"),
        new LanguageOption(LanguageModes.SimplifiedChinese, "简体中文"),
        new LanguageOption(LanguageModes.TraditionalChinese, "繁體中文"),
        new LanguageOption(LanguageModes.Japanese, "日本語"),
        new LanguageOption(LanguageModes.Korean, "한국어"),
        new LanguageOption(LanguageModes.French, "Français"),
        new LanguageOption(LanguageModes.Spanish, "Español"),
        new LanguageOption(LanguageModes.Italian, "Italiano")
    };

    public static string Current { get; private set; } = LanguageModes.English;

    public static event Action? Changed;

    public static void Apply(string language)
    {
        string code = LanguageModes.Normalize(language);
        Current = code;

        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // 语言包缺失时保留系统区域设置，字符串字典仍能工作。
        }

        var dictionaries = Application.Current?.Resources.MergedDictionaries;
        if (dictionaries is null) return;

        // 尚未翻译的语言先用英文字典兜底，避免出现无文案的空白界面。
        string resourceCode = code is LanguageModes.English or LanguageModes.SimplifiedChinese or LanguageModes.TraditionalChinese
            ? code
            : LanguageModes.English;
        var strings = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Localization/Lang.{resourceCode}.xaml")
        };

        int index = -1;
        for (int i = 0; i < dictionaries.Count; i++)
        {
            string source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("/Localization/Lang.", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index >= 0) dictionaries[index] = strings;
        else dictionaries.Add(strings);

        Changed?.Invoke();
    }

    /// <summary>取当前语言的文案；未翻译的键回退为中文原文。</summary>
    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        return Application.Current?.TryFindResource(key) is string value ? value : key;
    }
}
