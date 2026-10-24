using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using XivMark.Core;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeTargetType = FFXIVClientStructs.FFXIV.Client.Game.Object.TargetType;

namespace XivMark;

public sealed partial class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IClientState Client { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static ISigScanner Scanner { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;

    private readonly Configuration config;
    private readonly MarkerPlanner planner = new();
    private readonly NativeMarkers native;
    private bool visible;
    private long nextScan;
    private uint territory;
    private bool frontline;
    private long enteredAt;
    private string status = "Waiting for game state.";
    private string? error;
    private string? loggedWarning;
    private Candidate[] candidates = [];
    private uint[] markers = new uint[17];
    private Settings Settings => config.ToSettings();
    private readonly List<UiJob> jobs = new();

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Sanitize();
        Save();
        foreach (var job in Data.GetExcelSheet<ClassJob>())
            if (job.RowId >= 19 && job.Role != 0 && job.Abbreviation.ExtractText() is { Length: > 0 } abbreviation)
                jobs.Add(new UiJob(job.RowId, abbreviation, job.Name.ExtractText(), (byte)job.Role));
        native = new NativeMarkers(Scanner);
        Commands.AddHandler("/xivmark", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open settings. /xivmark on | off | status. Configurable Frontline enemy markers.",
        });
        Framework.Update += Update;
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += Open;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        visible = !config.Enabled;
    }

    private void Open() => visible = true;
    private void Save()
    {
        config.Sanitize();
        PluginInterface.SavePluginConfig(config);
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "on": config.Enabled = true; Save(); Chat.Print("[XivMark] Enabled for Frontline."); break;
            case "off": config.Enabled = false; Save(); Chat.Print("[XivMark] Disabled. Existing markers are kept; clear them manually if needed."); break;
            case "status":
                var report = $"[XivMark {typeof(Plugin).Assembly.GetName().Version}] {error ?? status} Owned: {planner.Owned.Count}, yielded: {planner.YieldedSlots}, unconfirmed: {planner.Unconfirmed.Count}, eligible enemies: {candidates.Count(c => Settings.SupportsJob(c.Job) && MarkerPlanner.Eligible(c, Settings))}. {native.Status}";
                Chat.Print(report);
                Log.Information(report);
                if (planner.LastWarning is { } warning) Chat.Print($"[XivMark] Last warning: {warning}");
                break;
            default: Open(); break;
        }
    }

    private unsafe void Update(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (now < nextScan)
            return;
        nextScan = now + 250;
        try
        {
            if (territory != Client.TerritoryType)
            {
                territory = Client.TerritoryType;
                planner.Reset();
                error = null;
                loggedWarning = null;
                frontline = false;
            }
            // TerritoryIntendedUse.Frontline = 18. Do not enable in Rival Wings, CC, or the Wolves' Den.
            var wasFrontline = frontline;
            frontline = Client.IsLoggedIn && Client.IsPvP &&
                Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory)?.TerritoryIntendedUse.RowId == 18;
            if (frontline && !wasFrontline)
                enteredAt = now;
            if (!frontline)
            {
                planner.Reset();
                candidates = [];
                markers = new uint[17];
                status = "Idle - outside Frontline.";
                return;
            }
            if (Conditions[ConditionFlag.BetweenAreas] || Conditions[ConditionFlag.BetweenAreas51] ||
                Conditions[ConditionFlag.LoggingOut] || Objects.LocalPlayer is not { } local)
            {
                status = "Paused - player unavailable or changing areas.";
                return;
            }
            if (error != null || !native.Ready)
            {
                status = error ?? native.Status;
                return;
            }
            var snapshot = native.Read();
            if (snapshot == null)
            {
                status = "Paused - marker controller unavailable.";
                return;
            }
            markers = snapshot;
            var found = new List<Candidate>();
            foreach (var obj in Objects)
            {
                // Include every loaded job so previously marked players can be observed after switching jobs.
                if (obj is not IPlayerCharacter player || player.EntityId == local.EntityId)
                    continue;
                var friendly = (player.StatusFlags & (StatusFlags.PartyMember | StatusFlags.AllianceMember)) != 0;
                var enemy = !friendly && ((NativeObject*)player.Address)->GetTargetType() == NativeTargetType.Enemy;
                found.Add(new Candidate(player.EntityId, player.ClassJob.RowId,
                    Vector3.Distance(local.Position, player.Position), enemy, player.CurrentHp > 0, player.IsTargetable,
                    (player.StatusFlags & StatusFlags.InCombat) != 0));
            }
            candidates = found.OrderBy(c => c.Distance).ToArray();
            var delayRemaining = AutomationTiming.DelayRemaining(now, enteredAt, config.StartDelaySeconds);
            var pausedDead = config.PauseWhenDead && local.CurrentHp == 0;
            var active = AutomationTiming.CanOperate(config.Enabled, delayRemaining, config.PauseWhenDead, local.CurrentHp == 0);
            var settings = Settings;
            var action = planner.Next(true, active, candidates, markers, settings, now);
            status = !config.Enabled ? "Disabled - existing markers are kept." :
                delayRemaining > 0 ? $"Waiting - start delay: {Math.Ceiling(delayRemaining / 1000d):0}s remaining." :
                pausedDead ? "Paused - your character is dead." : planner.Status;
            if (planner.LastWarning != loggedWarning)
            {
                loggedWarning = planner.LastWarning;
                if (loggedWarning != null) Log.Warning(loggedWarning);
            }
            if (action is { } request && native.Send(request, Objects, settings))
            {
                planner.Sent(request, now, config.IntervalMs);
                Log.Debug($"Marker request: slot {request.Slot + 1}, target {request.Target:X8}, previous {request.Expected:X8}");
                if (request.Target == 0)
                    Log.Information($"Clearing {MarkerPlanner.MarkerName(request.Slot)} from unselected enemy job {candidates.FirstOrDefault(c => c.Id == request.Expected).Job}, actor {request.Expected:X8}.");
            }
            else if (action != null)
                status = "Waiting - marker or job state changed before sending; checking again next scan.";
        }
        catch (Exception ex)
        {
            error = "Paused after an error. Check /xllog and reload the plugin.";
            Log.Error(ex, "XivMark update stopped");
        }
    }

    public void Dispose()
    {
        Framework.Update -= Update;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= Open;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        Commands.RemoveHandler("/xivmark");
        // No asynchronous work and no game writes during unload.
    }
}
