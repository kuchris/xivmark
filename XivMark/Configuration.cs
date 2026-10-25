using Dalamud.Configuration;
using XivMark.Core;

namespace XivMark;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;
    public bool Enabled { get; set; }
    public float Range { get; set; } = 60;
    public int MaxMarkers { get; set; } = 13;
    public int IntervalMs { get; set; } = 1000;
    public int StartDelaySeconds { get; set; }
    public bool CombatOnly { get; set; }
    public bool PauseWhenDead { get; set; }
    public bool ClearOtherJobs { get; set; } = true;
    public bool ReuseEmptySlots { get; set; } = true;
    public uint[] Jobs { get; set; } = [MarkerPlanner.DarkKnight, MarkerPlanner.Dancer];
    public int[] MarkerOrder { get; set; } = MarkerOptions.DefaultOrder;
    public int[] EnabledMarkers { get; set; } = MarkerOptions.DefaultOrder;

    public Settings ToSettings() => new(Range, MaxMarkers, Jobs,
        MarkerOrder.Where(EnabledMarkers.Contains).ToArray(), CombatOnly, ClearOtherJobs, ReuseEmptySlots);

    public void Sanitize()
    {
        Range = float.IsFinite(Range) ? Math.Clamp(Range, 10, 100) : 60;
        MaxMarkers = Math.Clamp(MaxMarkers, 0, 13);
        IntervalMs = Math.Clamp(IntervalMs, 1000, 10000);
        StartDelaySeconds = Math.Clamp(StartDelaySeconds, 0, 120);
        Jobs = (Jobs ?? [MarkerPlanner.DarkKnight, MarkerPlanner.Dancer]).Where(j => j > 0).Distinct().ToArray();
        MarkerOrder = MarkerOptions.NormalizeOrder(MarkerOrder);
        EnabledMarkers = (EnabledMarkers ?? MarkerOptions.DefaultOrder).Where(MarkerOptions.IsSupported).Distinct().ToArray();
        Version = 4;
    }
}
