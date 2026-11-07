using DiskCheckTW.Core.Analysis;
using DiskCheckTW.Core.Models;
using Xunit;

namespace DiskCheckTW.Tests;

public class SmartProfilesTests
{
    private static SmartAttribute A(byte id, byte current = 100, ulong raw = 0) =>
        new() { Id = id, Current = current, Raw = raw };

    private static PhysicalDiskInfo Disk(string model, string? firmware, params SmartAttribute[] attributes)
    {
        var baseDisk = new PhysicalDiskInfo { Model = model, Firmware = firmware, Kind = DriveKind.SataSsd };
        return new PhysicalDiskInfo
        {
            Model = model, Firmware = firmware, Kind = DriveKind.SataSsd,
            AtaSmart = new AtaSmartData { Attributes = attributes, VendorProfile = SmartProfiles.Detect(baseDisk, attributes) }
        };
    }

    [Fact]
    public void WdGreenUsesWdcNamesAndRawByteOneLife()
    {
        var disk = Disk("WDC WDS120G1G0A", "X61190WD",
            A(0xA5, raw: 0x3CEB40EF40B6), A(0xA6, raw: 6), A(0xA7), A(0xA8, raw: 74), A(0xA9),
            A(0xAD, raw: 36), A(0xE6, raw: 0x159202041592), A(0xE8, raw: 100),
            A(0xE9, raw: 4345), A(0xEA, raw: 77308), A(0xF1, current: 253, raw: 21956),
            A(0xF2, current: 253, raw: 10839));
        Assert.Equal("SmartWdc", disk.AtaSmart!.VendorProfile);
        Assert.Equal(79, HealthAnalyzer.Analyze(disk).LifeRemainingPercent);
        foreach (var (id, english) in new (byte, string)[] {
            (0xA5, "Block Erase Count (SLC)"), (0xA6, "Minimum P/E Cycles"),
            (0xA7, "Maximum Bad Blocks per die"), (0xA8, "Maximum P/E Cycles"),
            (0xA9, "Total Bad Block"), (0xE6, "Media Wearout Indicator"),
            (0xE8, "Available Reserve Space"), (0xE9, "NAND GB Written"),
            (0xEA, "NAND GB Written (SLC)"), (0xF1, "Total GB Written"), (0xF2, "Total GB Read") })
            Assert.EndsWith(" / " + english, SmartProfiles.NameOf("SmartWdc", id));
    }

    [Theory]
    [InlineData("SanDisk SSD PLUS", null, "SmartSanDiskGb", 0xE6, 100, 0x1500, 79)]
    [InlineData("Samsung SSD 870 EVO", null, "SmartSamsung", 0xB1, 88, 0, 88)]
    [InlineData("Crucial CT500MX500SSD1", null, "SmartMicronMU03", 0xCA, 92, 0, 92)]
    [InlineData("INTEL SSDSC2", null, "SmartIntel", 0xE9, 81, 0, 81)]
    [InlineData("KINGSTON SA400S37", "SBFK62C3", "SmartKingstonSA400", 0xE7, 100, 75, 75)]
    [InlineData("SandForce SSD", null, "SmartSandForce", 0xE7, 73, 0, 73)]
    public void VendorLifeUsesCdiRule(string model, string? firmware, string profile, byte id, byte current, ulong raw, int expected)
    {
        var disk = Disk(model, firmware, A(id, current, raw));
        Assert.Equal(profile, disk.AtaSmart!.VendorProfile);
        Assert.Equal(expected, HealthAnalyzer.Analyze(disk).LifeRemainingPercent);
    }

    [Fact]
    public void PhisonSignatureUsesRawE7AndUnknownWithoutE7HasNoLife()
    {
        var phison = Disk("Generic SATA SSD", null,
            A(0x01), A(0x09), A(0x0C), A(0xA8), A(0xAA), A(0xAD), A(0xC0),
            A(0xDA), A(0xE7, current: 100, raw: 6), A(0xF1));
        Assert.Equal("SmartPhison", phison.AtaSmart!.VendorProfile);
        Assert.Equal(6, HealthAnalyzer.Analyze(phison).LifeRemainingPercent);
        Assert.Equal(HealthStatus.Caution, HealthAnalyzer.Analyze(phison).Status);
        var unknown = Disk("Unidentified SSD", null, A(0xE9, current: 42));
        Assert.Equal("SmartSsd", unknown.AtaSmart!.VendorProfile);
        Assert.Null(HealthAnalyzer.Analyze(unknown).LifeRemainingPercent);
        Assert.Contains(" / ", SmartProfiles.NameOf("SmartSsd", 0xE9));
    }

    [Fact]
    public void HddUsesGenericSmartTable()
    {
        var disk = new PhysicalDiskInfo { Model = "WDC WD10EZEX", Kind = DriveKind.Hdd };
        Assert.Equal("Smart", SmartProfiles.Detect(disk, [A(0xE9)]));
    }
}
