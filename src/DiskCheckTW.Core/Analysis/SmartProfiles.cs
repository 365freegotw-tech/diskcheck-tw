using System.Globalization;
using DiskCheckTW.Core.Models;

namespace DiskCheckTW.Core.Analysis;

/// <summary>CDI 語系表的名稱與可辨識的 SATA SSD 設定檔。</summary>
public static class SmartProfiles
{
    private static readonly Dictionary<(string Profile, byte Id), (string Zh, string En)> Names = LoadNames();

    public static string Detect(PhysicalDiskInfo disk, IReadOnlyList<SmartAttribute> attributes)
    {
        if (disk.Kind == DriveKind.Hdd) return "Smart";
        if (disk.Kind != DriveKind.SataSsd) return "SmartSsd";

        var model = disk.Model.ToUpperInvariant();
        var firmware = (disk.Firmware ?? "").ToUpperInvariant();
        bool Has(byte id) => attributes.Any(a => a.Id == id);
        bool Starts(params string[] prefixes) => prefixes.Any(model.StartsWith);
        bool Contains(params string[] parts) => parts.Any(model.Contains);
        bool StartsWithIds(params byte[] ids) => attributes.Count >= ids.Length &&
            ids.Select((id, index) => attributes[index].Id == id).All(match => match);

        // CDI AtaSmart.cpp: IsSsdSanDisk, IsSsdWdc, then controller and model checks.
        if (Contains("SANDISK", "SD ULTRA", "SDLF1"))
        {
            if (Contains("SDSSDP", "SDSSDRC")) return "SmartSanDisk";
            if (Contains("1006")) return Contains("8U") ? "SmartSanDiskHpVenus" : "SmartSanDiskHp";
            if (Contains("G1001")) return Contains("6S", "7S", "8U") ? "SmartSanDiskLenovoHelenVenus" : "SmartSanDiskLenovo";
            if (Contains("G1012", "Z400S 2.5", "Z400")) return "SmartSanDiskDell";
            if (Contains("SDLF1CRR-", "SDLF1DAR-", "SDLF1CRM-", "SDLF1DAM-")) return "SmartSanDiskCloud";
            if (Contains("X600", "X400", "X300", "X110", "SD5") && Has(0xAF)) return "SmartSanDiskDell";
            return "SmartSanDiskGb";
        }
        if (Starts("ADATA_IM2S", "ADATA_IMSS", "ADATA_ISSS", "IM2S", "IMSS", "ISSS")) return "SmartAdataIndustrial";
        if (Starts("WDC ", "WD ", "WDS")) return "SmartWdc";
        if (StartsWithIds(0x0C, 0x09, 0xC2, 0xE5, 0xE8, 0xE9)) return "SmartJMicron60x";
        if (StartsWithIds(0x01, 0x02, 0x03, 0x05, 0x07, 0x08, 0x09, 0x0A, 0x0C, 0xA8, 0xAF, 0xC0, 0xC2)) return "SmartJMicron61x";
        if (StartsWithIds(0x01, 0x02, 0x03, 0x05, 0x07, 0x08, 0x09, 0x0A, 0x0C, 0xA7, 0xA8, 0xA9, 0xAA, 0xAD, 0xAF)
            || Starts("ADATA SU700")) return "SmartJMicron66x";
        if (StartsWithIds(0x01, 0x09, 0x0C, 0xB8, 0xC3, 0xC4)) return "SmartIndilinx";
        if (Contains("INTEL", "SOLIDIGM")) return Contains("SSDSCKHB") ? "SmartIntelDc" : "SmartIntel";
        if (Contains("SAMSUNG", "MZ-", "MZ7")) return "SmartSamsung";
        if (Starts("CRUCIAL", "MICRON", "MTFD", "CT") ||
            Starts("M300", "M400", "M500", "M600", "P300", "P400", "P500", "P600", "C300", "C400", "C500", "C600"))
            return Contains("MX500SSD", "BX500SSD", "MX300SSD", "BX300SSD", "MX200SSD", "BX200SSD", "MX100SSD", "BX100SSD") && !firmware.StartsWith("MU01")
                || Contains("MU03") || firmware.StartsWith("MU03") ? "SmartMicronMU03" : "SmartMicron";
        if (Contains("KINGSTON"))
        {
            if (Contains("SA400")) return "SmartKingstonSA400";
            if (Contains("KC600")) return "SmartKingstonKC600";
            if (Contains("DC500")) return "SmartKingstonDC500";
            if (Contains("SUV400", "SUV500")) return "SmartKingstonSuv";
            return "SmartKingston";
        }
        if (Contains("SAND FORCE", "SANDFORCE") ||
            StartsWithIds(0x01, 0x05, 0x09, 0x0C, 0x0D, 0x64, 0xAA) ||
            StartsWithIds(0x01, 0x05, 0x09, 0x0C, 0xAB, 0xAC)) return "SmartSandForce";
        if (Starts("MTRON") || attributes.Count == 1 && attributes[0].Id == 0xBB) return "SmartMtron";
        if (Starts("OCZ", "RADEON R7", "PANASONIC RP-SSB"))
            return Starts("OCZ-TRION") || StartsWithIds(0x01, 0x03, 0x04, 0x05, 0x09, 0x0C, 0xE8, 0xE9)
                ? "SmartOcz" : "SmartOczVector";
        if (Starts("PLEXTOR", "LITEON", "CV6-CQ") || StartsWithIds(0x01, 0x05, 0x09, 0x0C, 0xB1, 0xB2, 0xB5, 0xB6)) return "SmartPlextor";
        if (Contains("INDILINX")) return "SmartIndilinx";
        if (StartsWithIds(0x01, 0x05, 0x09, 0x0C, 0xA1, 0xA2, 0xA3, 0xA4, 0xA6, 0xA7)) return "SmartRealtek";
        if (Contains("CVC-")) return "SmartSiliconMotionCVC";
        if (Contains("SILICON MOTION", "SMI")) return "SmartSiliconMotion";
        if (Contains("TOSHIBA")) return "SmartToshiba";
        if (Contains("KIOXIA")) return "SmartKioxia";
        if (Contains("SK HYNIX", "HFS", "SHG")) return "SmartSKhynix";
        if (Contains("CV8-", "CVB-", "ER2-")) return "SmartSsstc";
        if (Starts("APACER", "ZADAK") || Starts("ADATA AP") && firmware.StartsWith("AP")) return "SmartApacer";
        if (Contains("ZHITAI")) return "SmartYmtc";
        if (Starts("SCY")) return "SmartScy";
        if (Starts("RECADATA")) return "SmartRecadata";
        if (Starts("MAXIO", "CUSO C5S-EVO")) return "SmartMaxiotek";
        if (Starts("CORSAIR")) return "SmartCorsair";
        if (Contains("PHISON") || StartsWithIds(0x01, 0x09, 0x0C, 0xA8, 0xAA, 0xAD, 0xC0, 0xC2, 0xDA, 0xE7, 0xF1)
            || StartsWithIds(0x01, 0x09, 0x0C, 0xA8, 0xAA, 0xAD, 0xC0, 0xDA, 0xE7, 0xF1)) return "SmartPhison";
        return "SmartSsd";
    }

    /// <summary>繁中介面顯示「中文 / English」，其他語言只顯示英文（CDI 名稱表）。</summary>
    public static string NameOf(string profile, byte id)
    {
        var zh = S.Language.Name == "zh-TW";
        return Names.TryGetValue((profile, id), out var n)
            ? (zh ? $"{n.Zh} / {n.En}" : n.En)
            : (zh ? "廠商定義 / Vendor-specific" : "Vendor-specific");
    }

    private static Dictionary<(string, byte), (string Zh, string En)> LoadNames()
    {
        using var stream = typeof(SmartProfiles).Assembly.GetManifestResourceStream("DiskCheckTW.Core.SmartNames.tsv")
            ?? throw new InvalidOperationException("SMART name table resource is missing.");
        using var reader = new StreamReader(stream);
        var names = new Dictionary<(string, byte), (string Zh, string En)>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#')) continue;
            var parts = line.Split('\t');
            if (parts.Length != 4 || !byte.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                throw new InvalidDataException("SMART name table has an invalid line.");
            names[(parts[0], id)] = (parts[2], parts[3]);
        }
        return names;
    }
}
