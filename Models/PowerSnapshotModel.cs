using System;

namespace MASLOOPTIMIZER;

public class PowerSnapshotModel
{
    public DateTime CapturedAt { get; set; }

    public Guid OriginalPlanGuid { get; set; }

    public string OriginalPlanName { get; set; } = string.Empty;

    public int OriginalDisplayHz { get; set; } = 60;

    public int OriginalDisplayWidth { get; set; }

    public int OriginalDisplayHeight { get; set; }

    public uint CpuMaxStateAc { get; set; } = 100;
    public uint CpuMinStateAc { get; set; } = 5;
    public uint CpuBoostModeAc { get; set; } = 2;
    public uint CoreParkingMinAc { get; set; } = 100;
    public uint PcieAspmStateAc { get; set; } = 0;

    public int GpuPowerMizerLevel { get; set; } = 0;

    public int AmdUlpsState { get; set; } = 0;
}
