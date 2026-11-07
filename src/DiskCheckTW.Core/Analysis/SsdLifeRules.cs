using DiskCheckTW.Core.Models;

namespace DiskCheckTW.Core.Analysis;

/// <summary>CDI AtaSmart.cpp Update Life；未知或無效數值不推估。</summary>
public static class SsdLifeRules
{
    public static int? Read(PhysicalDiskInfo disk, AtaSmartData ata)
    {
        var profile = ata.VendorProfile ?? SmartProfiles.Detect(disk, ata.Attributes);
        SmartAttribute? attribute;
        int value;
        switch (profile)
        {
            case "SmartWdc":
            case "SmartSanDiskGb":
                attribute = ata.Get(0xE6);
                value = attribute is null ? -1 : 100 - (int)((attribute.Raw >> 8) & 0xFF);
                break;
            case "SmartSanDisk":
                attribute = ata.Get(0xE6);
                value = attribute is null ? -1 : 100 - (int)(attribute.Raw & 0xFFFF) / 100;
                break;
            case "SmartSanDiskLenovo":
            case "SmartSanDiskDell":
                value = ata.Get(0xE6)?.Current ?? -1;
                break;
            case "SmartSanDiskLenovoHelenVenus":
                value = ata.Get(0xE9)?.Current ?? -1;
                break;
            case "SmartSanDiskHp":
            case "SmartSanDiskHpVenus":
                value = ata.Get(0xC9)?.Current ?? -1;
                break;
            case "SmartSanDiskCloud":
                value = ata.Get(0xF5)?.Current ?? -1;
                break;
            case "SmartSamsung":
                value = ata.Get(0xB1)?.Current ?? -1;
                if (value < 0 && IsSamsungEnterprise(disk.Model)) value = ata.Get(0xE9)?.Current ?? -1;
                break;
            case "SmartMicron":
            case "SmartMicronMU03":
            case "SmartIntelDc":
            case "SmartSiliconMotionCVC":
                value = ata.Get(0xCA)?.Current ?? -1;
                break;
            case "SmartIntel":
            case "SmartOcz":
            case "SmartOczVector":
                value = ata.Get(0xE9)?.Current ?? -1;
                break;
            case "SmartKingstonKC600":
                value = ata.Get(0xA9)?.Current ?? -1;
                break;
            case "SmartSiliconMotion":
                value = ata.Get(0xA9) is { } smi ? (int)(smi.Raw & 0xFF) : -1;
                break;
            case "SmartApacer":
                value = ata.Get(0xE7) is { } apacer ? (int)(apacer.Raw & 0xFF) : -1;
                break;
            case "SmartKingstonSA400":
                attribute = ata.Get(0xE7);
                value = attribute is null ? -1 : disk.Firmware?.StartsWith("03070009", StringComparison.OrdinalIgnoreCase) == true
                    ? attribute.Current : (int)(attribute.Raw & 0xFF);
                break;
            case "SmartPhison":
                attribute = ata.Get(0xE7);
                value = attribute is null ? -1 : disk.Model.StartsWith("aigo PSSD P6", StringComparison.OrdinalIgnoreCase)
                    ? attribute.Current : (int)(attribute.Raw & 0xFF);
                break;
            case "SmartPlextor":
                value = ata.Get(0xE8)?.Current ?? -1;
                break;
            case "SmartIndilinx":
                value = ata.Get(0xD1)?.Current ?? -1;
                break;
            case "SmartKingston":
            case "SmartKingstonSuv":
            case "SmartKingstonDC500":
            case "SmartSandForce":
            case "SmartCorsair":
            case "SmartRealtek":
            case "SmartSsstc":
            case "SmartJMicron60x":
            case "SmartJMicron61x":
            case "SmartJMicron66x":
            case "SmartSeagate":
            case "SmartMaxiotek":
            case "SmartYmtc":
            case "SmartScy":
            case "SmartRecadata":
            case "SmartAdataIndustrial":
            case "SmartSsd":
                value = ata.Get(0xE7)?.Current ?? -1;
                break;
            case "SmartToshiba":
            case "SmartKioxia":
                value = (ata.Get(0xAD)?.Current ?? 0) - 100;
                break;
            default:
                value = -1;
                break;
        }
        return value is >= 0 and <= 100 ? value : null;
    }

    private static bool IsSamsungEnterprise(string model) =>
        new[] { "SM863", "PM863", "SM883", "PM883", "SM843T", "PM853T", "MZ7KM", "MZ7KH", "MZ7LM", "MZ7LH" }
            .Any(part => model.Contains(part, StringComparison.OrdinalIgnoreCase));
}
