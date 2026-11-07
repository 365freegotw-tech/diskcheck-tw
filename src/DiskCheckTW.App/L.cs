using System.Globalization;
using System.Resources;
using System.Windows.Markup;
using DiskCheckTW.Core;

namespace DiskCheckTW.App;

public static class L
{
    private static readonly ResourceManager Resources = new("DiskCheckTW.App.Strings", typeof(L).Assembly);
    public static string T(string key) => Resources.GetString(key, S.Language)
        ?? throw new MissingManifestResourceException($"Missing translation: {key}");
    public static string F(string key, params object?[] values) => string.Format(S.Language, T(key), values);
}

public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public LocExtension(string key) => Key = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
