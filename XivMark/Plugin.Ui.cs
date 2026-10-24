using System.Numerics;
using Dalamud.Bindings.ImGui;
using XivMark.Core;

namespace XivMark;

internal readonly record struct UiJob(uint Id, string Code, string Name, byte Role);

public sealed partial class Plugin
{
    private bool enemiesOnly = true;
    private static readonly Vector4 Gold = new(0.91f, 0.73f, 0.40f, 1);
    private static readonly Vector4 Muted = new(0.56f, 0.60f, 0.65f, 1);
    private static readonly Vector4 Green = new(0.40f, 0.80f, 0.64f, 1);
    private static readonly Vector4 Red = new(0.94f, 0.43f, 0.43f, 1);
    private static float Scale => ImGui.GetFontSize() / 17f;
    private static void Gap(float pixels = 8) => ImGui.Dummy(new Vector2(0, pixels * Scale));
    private static void MutedText(string value) => ImGui.TextColored(Muted, value);
    private static void Hint(string value)
    {
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(value);
    }

    private void Draw()
    {
        if (!visible) return;
        var scale = Scale;
        ImGui.SetNextWindowSize(new Vector2(800, 650) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(720, 540) * scale, new Vector2(1600, 1200) * scale);
        if (ImGui.Begin("XivMark###XivMark - Frontline", ref visible))
        {
            var enabled = config.Enabled;
            if (ImGui.Checkbox("Enable automatic marking", ref enabled)) { config.Enabled = enabled; Save(); }
            ImGui.SameLine();
            ImGui.TextDisabled($"  v{typeof(Plugin).Assembly.GetName().Version?.ToString(3)}");
            ImGui.TextWrapped(error ?? status);
            ImGui.Separator();
            Gap(3);
            if (ImGui.BeginTabBar("settingsTabs"))
            {
                string[] tabs = ["General", "Jobs", "Markers", "Diagnostics"];
                for (var i = 0; i < tabs.Length; i++)
                {
                    if (ImGui.BeginTabItem(tabs[i]))
                    {
                        if (ImGui.BeginChild($"tabContent{i}", Vector2.Zero))
                        {
                            Gap(8);
                            switch (i)
                            {
                                case 0: DrawOverview(); break;
                                case 1: DrawJobs(); break;
                                case 2: DrawMarkers(); break;
                                case 3: DrawDiagnostics(); break;
                            }
                        }
                        ImGui.EndChild();
                        ImGui.EndTabItem();
                    }
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    private static void Section(string title, string description)
    {
        ImGui.TextUnformatted(title);
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextWrapped(description);
        ImGui.PopStyleColor();
        Gap(8);
    }

    private void DrawOverview()
    {
        if (ImGui.BeginTable("parameters", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            ImGui.Text("Marking range");
            ImGui.SetNextItemWidth(-12 * Scale);
            var range = config.Range;
            if (ImGui.SliderFloat("##range", ref range, 10, 100, "%.0f yalms")) { config.Range = range; Save(); }
            Hint("Only affects new marks. Existing marks stay on their targets.");
            Gap(12);
            ImGui.Text("Delay after entering Frontline");
            ImGui.SetNextItemWidth(-12 * Scale);
            var delay = config.StartDelaySeconds;
            if (ImGui.SliderInt("##delay", ref delay, 0, 120, "%d seconds")) { config.StartDelaySeconds = delay; Save(); }
            ImGui.TableNextColumn();
            ImGui.Text("Mark / clear interval");
            ImGui.SetNextItemWidth(-12 * Scale);
            var seconds = config.IntervalMs / 1000f;
            if (ImGui.SliderFloat("##interval", ref seconds, 1, 10, "%.2f seconds")) { config.IntervalMs = (int)Math.Round(seconds * 1000); Save(); }
            Gap(12);
            ImGui.Text("Maximum marker slots");
            ImGui.SetNextItemWidth(-12 * Scale);
            var capacity = config.MaxMarkers;
            if (ImGui.SliderInt("##capacity", ref capacity, 0, 13, "%d slots")) { config.MaxMarkers = capacity; Save(); }
            Hint("Uses the first N enabled markers in your chosen order.");
            ImGui.EndTable();
        }
        Gap(14);
        ImGui.Separator();
        Gap(12);
        var combat = config.CombatOnly;
        if (ImGui.Checkbox("Only mark enemies in combat", ref combat)) { config.CombatOnly = combat; Save(); }
        Gap(4);
        var dead = config.PauseWhenDead;
        if (ImGui.Checkbox("Pause while I am dead", ref dead)) { config.PauseWhenDead = dead; Save(); }
        Gap(4);
        var cleanup = config.ClearOtherJobs;
        if (ImGui.Checkbox("Clear marks on unselected enemy jobs", ref cleanup)) { config.ClearOtherJobs = cleanup; Save(); }
        Hint("Includes marks placed by others and marks left before reload. Allies and unknown jobs are kept.");
        Gap(14);
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextWrapped("Marks stay fixed through death and distance. Job cleanup also checks existing marks placed by others. Changes save automatically.");
        ImGui.PopStyleColor();
    }

    private void DrawJobs()
    {
        Section("Target jobs", "Choose which enemy jobs to mark.");
        if (ImGui.Button("DRK + DNC")) { config.Jobs = [32, 38]; Save(); }
        Hint("Replace the selection with Dark Knight and Dancer.");
        ImGui.SameLine();
        if (ImGui.Button("Select all")) { config.Jobs = jobs.Select(j => j.Id).ToArray(); Save(); }
        ImGui.SameLine();
        if (ImGui.Button("Clear selection")) { config.Jobs = []; Save(); }
        Gap(12);
        foreach (var (role, label, color) in new (byte, string, Vector4)[]
        {
            (1, "Tanks", Muted),
            (4, "Healers", Muted),
            (2, "Melee", Muted),
            (3, "Ranged / casters", Muted),
        })
        {
            ImGui.TextColored(color, label);
            Gap(5);
            if (ImGui.BeginTable($"jobs{role}", 4, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var job in jobs.Where(j => j.Role == role))
                {
                    ImGui.TableNextColumn();
                    var selected = config.Jobs.Contains(job.Id);
                    if (ImGui.Checkbox($"{job.Code}##job{job.Id}", ref selected))
                    {
                        config.Jobs = selected ? config.Jobs.Append(job.Id).ToArray() : config.Jobs.Where(j => j != job.Id).ToArray();
                        Save();
                    }
                    Hint(job.Name);
                }
                ImGui.EndTable();
            }
            Gap(12);
        }
        ImGui.Separator();
        Gap(10);
        ImGui.PushStyleColor(ImGuiCol.Text, config.ClearOtherJobs ? Gold : Muted);
        ImGui.TextWrapped(config.ClearOtherJobs
            ? "Job cleanup is ON. Removing a job can clear enabled marks already on enemies of that job."
            : "Job cleanup is OFF. Removing a job affects new marks; existing marks are kept.");
        ImGui.PopStyleColor();
    }

    private void DrawMarkers()
    {
        Section("Markers", "Use from top to bottom. Reordering keeps existing assignments.");
        MutedText($"{config.EnabledMarkers.Length} enabled  /  {Settings.ActiveSlots.Count()} in use with current capacity");
        Gap(8);
        if (ImGui.BeginTable("markerOrder", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, 32 * Scale);
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28 * Scale);
            ImGui.TableSetupColumn("Marker", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Current state", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("Order", ImGuiTableColumnFlags.WidthFixed, 100 * Scale);
            ImGui.TableHeadersRow();
            var activeSlots = Settings.ActiveSlots.ToHashSet();
            for (var i = 0; i < config.MarkerOrder.Length; i++)
            {
                var slot = config.MarkerOrder[i];
                var enabled = config.EnabledMarkers.Contains(slot);
                ImGui.PushID(slot);
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 31 * Scale);
                ImGui.TableNextColumn();
                if (ImGui.Checkbox("##enabled", ref enabled))
                {
                    config.EnabledMarkers = enabled ? config.EnabledMarkers.Append(slot).ToArray() : config.EnabledMarkers.Where(s => s != slot).ToArray();
                    Save();
                }
                ImGui.TableNextColumn();
                MutedText((i + 1).ToString("00"));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(MarkerPlanner.MarkerName(slot));
                ImGui.TableNextColumn();
                var holder = markers[slot];
                var job = jobs.FirstOrDefault(j => j.Id == candidates.FirstOrDefault(c => c.Id == holder).Job).Code;
                var text = planner.Unconfirmed.ContainsKey(slot) ? "Awaiting update" : holder != 0 ? $"Assigned / {job ?? holder.ToString("X8")}" : !enabled ? "Disabled" : !activeSlots.Contains(slot) ? "Over capacity" : "Available";
                ImGui.TextColored(planner.Unconfirmed.ContainsKey(slot) ? Gold : holder != 0 ? Green : Muted, text);
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(i == 0);
                var up = ImGui.SmallButton("Up");
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(i == config.MarkerOrder.Length - 1);
                var down = ImGui.SmallButton("Down");
                ImGui.EndDisabled();
                ImGui.PopID();
                if (up || down)
                {
                    var other = up ? i - 1 : i + 1;
                    (config.MarkerOrder[i], config.MarkerOrder[other]) = (config.MarkerOrder[other], config.MarkerOrder[i]);
                    Save();
                    break;
                }
            }
            ImGui.EndTable();
        }
        Gap(8);
        MutedText("Capacity and timing are on General. Changes save automatically.");
    }

    private void DrawDiagnostics()
    {
        Section("Diagnostics", "Only players loaded by your client are shown.");
        ImGui.Checkbox("Enemy players only", ref enemiesOnly);
        ImGui.SameLine();
        MutedText($"Territory {territory}  /  {planner.YieldedSlots} yielded  /  {planner.Unconfirmed.Count} unconfirmed");
        if (planner.LastWarning != null)
        {
            Gap(8);
            ImGui.TextColored(Gold, "LAST WARNING");
            ImGui.TextWrapped(planner.LastWarning);
        }
        Gap(10);
        if (ImGui.BeginTable("players", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 45 * Scale);
            ImGui.TableSetupColumn("Actor", ImGuiTableColumnFlags.WidthFixed, 94 * Scale);
            ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed, 70 * Scale);
            ImGui.TableSetupColumn("Observation");
            ImGui.TableSetupColumn("Marker");
            ImGui.TableHeadersRow();
            var rows = 0;
            var settings = Settings;
            foreach (var candidate in candidates.Where(c => (!enemiesOnly || c.Enemy) && (settings.SupportsJob(c.Job) || markers.Contains(c.Id))))
            {
                rows++;
                var slot = Array.IndexOf(markers, candidate.Id);
                var code = jobs.FirstOrDefault(j => j.Id == candidate.Job).Code ?? candidate.Job.ToString();
                var state = !candidate.Enemy ? "Friendly / unknown" : settings.ShouldClearJob(candidate.Job) && slot >= 0 ? "Cleanup candidate" : !candidate.Alive ? "Dead" : !candidate.Targetable ? "Untargetable" : candidate.Distance > settings.Range ? "Out of range" : settings.CombatOnly && !candidate.InCombat ? "Not in combat" : settings.SupportsJob(candidate.Job) ? "Eligible" : "Not selected";
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 28 * Scale);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(code);
                ImGui.TableNextColumn(); MutedText(candidate.Id.ToString("X8"));
                ImGui.TableNextColumn(); ImGui.Text($"{candidate.Distance:0}y");
                ImGui.TableNextColumn(); ImGui.TextColored(state == "Eligible" ? Green : state == "Cleanup candidate" ? Gold : Muted, state);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(slot < 0 ? "-" : MarkerPlanner.MarkerName(slot));
            }
            ImGui.EndTable();
            if (rows == 0) { Gap(20); MutedText("No matching players currently loaded."); }
        }
        Gap(16);
        if (ImGui.CollapsingHeader("Engine details"))
        {
            ImGui.TextWrapped(native.Status);
            ImGui.Text($"Frontline: {frontline}  /  Pending operation: {planner.Waiting}");
            MutedText("Use /xivmark status to write a snapshot to chat and the log.");
        }
    }

}
