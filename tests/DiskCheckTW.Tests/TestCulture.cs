using System.Globalization;
using System.Runtime.CompilerServices;

namespace DiskCheckTW.Tests;

/// <summary>既有測試以繁體中文文字斷言；預設文化固定為 zh-TW，英文測試在測試內自行切換。</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Init()
    {
        var zh = CultureInfo.GetCultureInfo("zh-TW");
        CultureInfo.DefaultThreadCurrentUICulture = zh;
        CultureInfo.DefaultThreadCurrentCulture = zh;
        CultureInfo.CurrentUICulture = zh;
    }
}
