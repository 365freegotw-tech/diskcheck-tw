using System.Globalization;
using System.IO;
using System.Text.Json;
using DiskCheckTW.Core;

namespace DiskCheckTW.App;

internal static class AppLanguage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiskCheck TW", "settings.json");

    internal static void Apply()
    {
        string language = S.Language.Name;
        try
        {
            if (File.Exists(FilePath))
            {
                var saved = JsonSerializer.Deserialize<LanguageSettings>(File.ReadAllText(FilePath));
                if (saved?.Language is "zh-TW" or "en") language = saved.Language;
            }
        }
        catch (IOException) { }
        catch (JsonException) { }
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    internal static void Save(string language)
    {
        if (language is not ("zh-TW" or "en")) throw new ArgumentOutOfRangeException(nameof(language));
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new LanguageSettings { Language = language }));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class LanguageSettings { public string Language { get; set; } = ""; }
}
