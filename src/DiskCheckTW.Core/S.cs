using System.Globalization;
using System.Resources;

namespace DiskCheckTW.Core;

/// <summary>Text shared by diagnosis, advice, reports, and the Windows app.</summary>
public static class S
{
    private static readonly ResourceManager Resources = new("DiskCheckTW.Core.Strings", typeof(S).Assembly);

    public static CultureInfo Language => CultureInfo.CurrentUICulture.Name is "zh-TW" or "zh-Hant" or "zh-HK" or "zh-MO"
        ? CultureInfo.GetCultureInfo("zh-TW") : CultureInfo.GetCultureInfo("en");

    public static string T(string key) => Resources.GetString(key, Language)
        ?? throw new MissingManifestResourceException($"Missing translation: {key}");

    public static string F(string key, params object?[] values) => string.Format(Language, T(key), values);

    private static bool Zh => Language.Name == "zh-TW";
    /// <summary>清單分隔：中文「、」，英文「, 」。</summary>
    public static string ListSep => Zh ? "、" : ", ";
    /// <summary>標籤冒號：中文「：」，英文「: 」。</summary>
    public static string Colon => Zh ? "：" : ": ";
    /// <summary>欄位分隔：中文「｜」，英文「 | 」。</summary>
    public static string Bar => Zh ? "｜" : " | ";
    /// <summary>括號補充：中文「（…）」，英文「 (…)」。</summary>
    public static string Paren(string inner) => Zh ? $"（{inner}）" : $" ({inner})";
}
