using XivMark.Core;

var passed = 0;
var settings = new Settings(60, 13);
Candidate Drk(uint id, float distance = 10) => new(id, 32, distance, true, true, true);
Candidate Dnc(uint id, float distance = 10) => new(id, 38, distance, true, true, true);
void Check(bool value, string name)
{
    if (!value) throw new Exception($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}
MarkerAction? Step(MarkerPlanner p, uint[] m, Candidate[] c, long time = 0, bool enabled = true, bool frontline = true, Settings? options = null)
    => p.Next(frontline, enabled, c, m, options ?? settings, time);
void Acknowledge(MarkerPlanner p, uint[] m, Candidate[] c, MarkerAction request, long time = 0)
{
    p.Sent(request, time);
    m[request.Slot] = request.Target;
    Step(p, m, c, time + 250);
}

if (args.Contains("--repro-timeout"))
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1), Dnc(2)];
    p.Sent(Step(p, m, c)!.Value, 0);
    Step(p, m, c, 4000);
    var resumed = Step(p, m, c, 5000);
    Check(resumed == new MarkerAction(1, 2, 0), "one unconfirmed mark must not stop marking other enemies for the rest of the duty");
    return;
}

if (args.Contains("--repro-unselected-marker"))
{
    var p = new MarkerPlanner(); var m = new uint[17];
    m[1] = 99;
    Candidate[] c = [Drk(99) with { Job = 19 }];
    Check(Step(p, m, c) == new MarkerAction(1, 0, 99), "existing marker on an unselected enemy job must be cleared without prior ownership");
    return;
}

if (args.Contains("--repro-empty-slots"))
{
    var p = new MarkerPlanner(); var m = new uint[17];
    Candidate[] c = Enumerable.Range(1, 14).Select(id => Drk((uint)id)).ToArray();
    for (var i = 0; i < 13; i++)
        Acknowledge(p, m, c, Step(p, m, c, i * 1000)!.Value, i * 1000);
    Array.Clear(m);
    Step(p, m, c, 15000);
    Check(Step(p, m, c, 30000) != null, "eligible enemies with all thirteen marker slots visibly empty must not remain blocked for the rest of the duty");
    return;
}

{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1), Dnc(2), Drk(3)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    p.Sent(Step(p, m, c, 1000)!.Value, 1000);
    Check(Step(p, m, c, 4999) == null, "mid-duty delayed second mark waits up to timeout");
    var resumed = Step(p, m, c, 5000);
    Check(resumed == new MarkerAction(2, 3, 0) && p.Owned[0] == 1 && p.Unconfirmed.ContainsKey(1), "second mark timing out still permits third enemy while first assignment stays fixed");
    p.Sent(resumed!.Value, 5000);
    m[1] = 2;
    Check(Step(p, m, c, 5250) == null && p.Owned[1] == 2 && p.Unconfirmed.Count == 0, "late second acknowledgement handled while third operation is pending");
    m[2] = 3;
    Check(Step(p, m, c, 6000) == null && p.Owned.Count == 3, "independent acknowledgements converge without toggling any mark");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1), Dnc(2)];
    p.Sent(Step(p, m, c)!.Value, 0, 10000);
    Check(Step(p, m, c, 4000) == null && p.Unconfirmed.Count == 1, "timeout still respects ten-second configured interval");
    Check(Step(p, m, c, 9999) == null, "quarantine cannot shorten the operation interval");
    Check(Step(p, m, c, 10000) == new MarkerAction(1, 2, 0), "other marks resume at longer configured interval");
    m[0] = 999;
    Step(p, m, [], 10500);
    Check(p.Unconfirmed.Count == 0 && p.YieldedSlots == 1 && p.Owned.Count == 0, "external holder replacing an uncertain operation yields the slot");
    m[0] = 0;
    Check(Step(p, m, [Drk(1)], 11000) == null, "uncertain original target stays reserved after external replacement");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    Candidate[] switched = [Drk(1) with { Job = 19 }, Dnc(2)];
    p.Sent(Step(p, m, switched, 1000)!.Value, 1000);
    Check(Step(p, m, switched, 5000) == new MarkerAction(1, 2, 0), "timed-out job clear does not block marking other targets");
    Check(Step(p, m, switched, 6000) == new MarkerAction(1, 2, 0), "timed-out clear itself is never proposed again");
    m[0] = 0;
    Check(Step(p, m, switched, 7000) == new MarkerAction(0, 2, 0) && p.Owned.Count == 0 && p.Unconfirmed.Count == 0, "late clear acknowledgement releases the original slot");
    Check(Step(p, m, c, 8000) == new MarkerAction(0, 1, 0), "late deliberate cleanup permits the original player after switching back");
}
{
    var p = new MarkerPlanner(); var m = new uint[17];
    Candidate[] c = Enumerable.Range(1, 14).Select(id => Drk((uint)id)).ToArray();
    for (var i = 0; i < 13; i++)
    {
        var action = Step(p, m, c, i * 5000);
        Check(action != null && action.Value.Target == (uint)i + 1, $"consecutive timeout {i + 1} moves to a different target and slot");
        p.Sent(action!.Value, i * 5000);
        Step(p, m, c, i * 5000 + 4000);
    }
    Check(Step(p, m, c, 65000) == null && p.Unconfirmed.Count == 13 && p.Status.Contains("no available slots"), "all uncertain slots are bounded and explained instead of retried");
    m[0] = 1;
    Check(Step(p, m, c, 66000, enabled: false) == null && p.Owned[0] == 1 && p.Unconfirmed.Count == 12, "late updates still settle uncertain slots while paused");
    Step(p, m, c, 67000, frontline: false);
    Check(p.Unconfirmed.Count == 0 && p.LastWarning == null, "new duty resets all uncertain operations");
}
{
    var p = new MarkerPlanner(); var m = new uint[17];
    Step(p, m, [Drk(1, 101)]);
    Check(p.Status.Contains("no eligible enemies") && p.Status.Contains("1 loaded"), "distance-filtered enemies have an explicit wait reason");
    Array.Fill(m, 999u);
    Step(p, m, [Drk(1)]);
    Check(p.Status.Contains("no available slots") && p.Status.Contains("13 occupied"), "occupied slots have an explicit wait reason");
    Step(p, new uint[17], [Drk(1)], options: settings with { MarkerSlots = [] });
    Check(p.Status.Contains("no marker slots enabled"), "empty marker selection has an explicit wait reason");
}

Check(MarkerPlanner.Normalize(0xE0000000) == 0 && MarkerPlanner.Normalize(0) == 0 && MarkerPlanner.Normalize(123) == 123, "empty sentinel normalization");
foreach (var candidate in new[] { Drk(0), Drk(0xE0000000), Drk(1) with { Enemy = false }, Drk(1) with { Alive = false }, Drk(1) with { Targetable = false }, Drk(1, 61), Drk(1, float.NaN), Drk(1, float.PositiveInfinity), Drk(1, -1) })
    Check(!MarkerPlanner.Eligible(candidate, settings), $"reject invalid candidate: {candidate}");
Check(MarkerPlanner.Eligible(Drk(1, 60), settings), "range boundary included");

{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1), Dnc(2)];
    Check(Step(p, m, c, frontline: false) == null, "no actions outside Frontline");
    Check(Step(p, m, c, enabled: false) == null, "disabled by default does not mark");
    Check(Step(p, m, c) == new MarkerAction(0, 1, 0), "DRK uses Attack 1");
    Check(Step(new(), m, [Dnc(2)]) == new MarkerAction(0, 2, 0), "DNC uses same first slot as DRK");
    Check(Step(new(), m, [Drk(3) with { Job = 19 }]) == null, "other jobs ignored");
    Check(Step(new(), m, [Drk(4, 20), Drk(3, 5), Drk(2, 5)])?.Target == 2, "nearest first with deterministic tie breaking");
    Check(Step(new(), m, c, options: new Settings(60, 0)) == null, "zero shared slots disables new marks");
    Check(Step(new(), m, [Drk(1, 20), Dnc(2, 5)])?.Target == 2, "both jobs share nearest-first priority");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1), Drk(3, 20), Dnc(2)];
    var request = Step(p, m, c)!.Value;
    p.Sent(request, 0);
    Check(Step(p, m, c, 1000) == null && p.Owned.Count == 0, "no ownership or next request before state update");
    m[0] = 1;
    Check(Step(p, m, c, 250) == null && p.Owned[0] == 1, "observed assignment acknowledged, interval retained");
    Check(Step(p, m, c, 999) == null, "one second minimum interval");
    Check(Step(p, m, c, 1000) == new MarkerAction(1, 2, 0), "DNC uses next Attack slot after DRK");
    Check(Step(p, m, [Drk(1, 30), Drk(4, 1)], 1000) == new MarkerAction(1, 4, 0), "keep existing valid mark despite closer newcomer");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; m[0] = 900; m[14] = 1;
    Check(Step(p, m, [Drk(1), Drk(2)]) == new MarkerAction(1, 2, 0), "respect occupied slots and targets marked outside managed slots");
    Array.Fill(m, 900u);
    Check(Step(p, m, [Drk(2), Dnc(3)]) == null, "all slots occupied means no actions");
}
foreach (var reason in new[] { "death", "despawn", "range", "friendly", "job", "unknown job", "count", "disable", "untargetable" })
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    Candidate[] updated = reason switch
    {
        "death" => [Drk(1) with { Alive = false }],
        "despawn" => [],
        "range" => [Drk(1, 61)],
        "friendly" => [Drk(1) with { Enemy = false }],
        "job" => [Dnc(1)],
        "unknown job" => [Drk(1) with { Job = 0 }],
        "untargetable" => [Drk(1) with { Targetable = false }],
        _ => c,
    };
    var action = Step(p, m, updated, 1000, enabled: reason != "disable", options: reason == "count" ? new Settings(60, 0) : null);
    Check(action == null && p.Owned[0] == 1 && m[0] == 1, $"keep original mark on {reason}");
    Check(Step(p, m, [Drk(1)], 2000) == null, $"returning target is not toggled or reassigned after {reason}");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    m[0] = 999;
    Check(Step(p, m, [], 1000, enabled: false) == null && p.Owned.Count == 0, "do not clear a teammate's replacement");
    m[0] = 0;
    Check(Step(p, m, c, 2000) == null && p.YieldedSlots == 1, "do not give original target a different mark after external edit");
    Check(Step(p, m, [Drk(2)], 2000)?.Slot == 1, "new enemy may use another never-assigned slot");
    Step(p, m, [], 3000, frontline: false);
    Check(Step(p, m, c, 4000)?.Slot == 0 && p.YieldedSlots == 0, "new duty releases yielded slots");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    m[0] = 0;
    Check(Step(p, m, [], 1000) == null && p.YieldedSlots == 1, "manual removal is respected");
    Check(Step(p, m, c, 2000) == null, "removed mark is not immediately reapplied during grace period");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    p.Sent(Step(p, m, c)!.Value, 0);
    m[0] = 999;
    Check(Step(p, m, [], 1000) == null && p.YieldedSlots == 1 && p.Owned.Count == 0, "competing pending assignment yields without claiming ownership");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    p.Sent(Step(p, m, c)!.Value, 0);
    Check(Step(p, m, c, 3999) == null && p.LastWarning == null, "wait for delayed update");
    Check(Step(p, m, c, 4000) == null && p.LastWarning != null && p.Unconfirmed.Count == 1 && !p.Waiting, "timeout quarantines only the uncertain slot without repeating toggle command");
    m[0] = 1;
    Check(Step(p, m, c, 10000) == null && p.Owned[0] == 1 && p.Unconfirmed.Count == 0, "late response restores ownership without sending a toggle");
    Step(p, m, [], 11000, frontline: false);
    Check(p.LastWarning == null && !p.Waiting && p.Unconfirmed.Count == 0, "duty exit resets pending warning");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    p.Sent(Step(p, m, c)!.Value, 0);
    m[0] = 1;
    Check(Step(p, m, c, 1000, enabled: false) == null && p.Owned[0] == 1, "disable during pending request keeps acknowledged mark");
    Check(Step(p, m, c, 2000) == null, "re-enabling preserves existing mark");
}
foreach (var composition in new[] { "DRK", "DNC", "mixed" })
{
    var p = new MarkerPlanner(); var m = new uint[17];
    Candidate[] c = Enumerable.Range(1, 14).Select(id => composition == "DNC" || (composition == "mixed" && id % 2 == 0) ? Dnc((uint)id) : Drk((uint)id)).ToArray();
    int[] expectedSlots = [0, 1, 2, 3, 4, 14, 15, 16, 5, 6, 7, 8, 9];
    string[] names = ["Attack 1", "Attack 2", "Attack 3", "Attack 4", "Attack 5", "Attack 6", "Attack 7", "Attack 8", "Chain 1", "Chain 2", "Chain 3", "Stop 1", "Stop 2"];
    for (var i = 0; i < 13; i++)
    {
        var action = Step(p, m, c, i * 1000)!.Value;
        Check(action.Slot == expectedSlots[i] && action.Target == i + 1 && action.Expected == 0 && MarkerPlanner.MarkerName(action.Slot) == names[i], $"{composition}: slot mapping and shared allocation for {names[i]}");
        Acknowledge(p, m, c, action, i * 1000);
    }
    Check(p.Owned.Count == 13 && m.Skip(10).Take(4).All(id => id == 0), $"{composition}: fills thirteen marks without using shapes");
    Check(Step(p, m, [Drk(999, 1)], 13000) == null, $"{composition}: capacity reached without reshuffling");
    Check(Step(p, m, [], 14000, frontline: false) == null && p.Owned.Count == 0, $"{composition}: duty exit forgets ownership without native clears");
}
{
    var p = new MarkerPlanner(); var m = new uint[17];
    m[0] = 500;
    Check(Step(p, m, [Dnc(1)], options: new Settings(60, 1)) == null, "shared slot limit respects existing manual marks");
    m[0] = 0;
    Acknowledge(p, m, [Drk(1)], Step(p, m, [Drk(1)])!.Value);
    var request = Step(p, m, [Drk(1, 100), Dnc(2, 5)], 1000);
    Check(request == new MarkerAction(1, 2, 0) && p.Owned[0] == 1, "out-of-range holder retains its mark while a new enemy receives another");
    Check(Step(p, m, [Drk(1), Dnc(2)], 1000, options: new Settings(60, 1)) == null, "reducing limit preserves existing marks without adding more");
}
foreach (var action in new[] { new MarkerAction(0, 0, 1), new MarkerAction(0, 2, 1), new MarkerAction(10, 2, 0) })
{
    var rejected = false;
    try { new MarkerPlanner().Sent(action, 0); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, $"reject unowned clear, replacement or shape request: {action}");
}
foreach (var initialJob in new[] { 32u, 38u })
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] original = [Drk(1) with { Job = initialJob }];
    Acknowledge(p, m, original, Step(p, m, original)!.Value);
    Candidate[] switched = [Drk(1, 100) with { Job = 19, Alive = false, Targetable = false }, Dnc(2)];
    Check(Step(p, m, switched, 999) == null, $"{initialJob}: cleanup obeys interval");
    Check(Step(p, m, switched, 1000, enabled: false) == null, $"{initialJob}: disabled mode keeps changed-job mark");
    var clear = Step(p, m, switched, 1000, options: new Settings(60, 0));
    Check(clear == new MarkerAction(0, 0, 1), $"{initialJob}: known other job clears even when out of range, dead, untargetable or slot limit zero");
    p.Sent(clear!.Value, 1000);
    Check(Step(p, m, switched, 2000) == null && p.Owned[0] == 1, $"{initialJob}: do not reuse slot before clear acknowledged");
    m[0] = 0;
    Check(Step(p, m, switched, 2250) == new MarkerAction(0, 2, 0) && p.Owned.Count == 0 && p.YieldedSlots == 0,
        $"{initialJob}: successful job-change clear releases slot for another target");
    Check(Step(p, m, original, 2250) == new MarkerAction(0, 1, 0), $"{initialJob}: switch back to DRK/DNC becomes eligible again");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Dnc(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    Check(Step(p, m, [Drk(1)], 1000) == null && p.Owned[0] == 1, "DNC to DRK keeps same mark");
    m[0] = 999;
    Check(Step(p, m, [Drk(1) with { Job = 19 }], 2000) == null && p.Owned.Count == 0, "job change does not clear teammate replacement");
    var unowned = new uint[17]; unowned[0] = 1;
    Check(Step(new(), unowned, [Drk(1) with { Job = 19 }]) == new MarkerAction(0, 0, 1), "clear pre-existing enabled marks on unselected enemy jobs");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    Candidate[] switched = [Drk(1) with { Job = 19 }];
    p.Sent(Step(p, m, switched, 1000)!.Value, 1000);
    Check(Step(p, m, switched, 4999) == null && p.LastWarning == null, "clear is never resent while pending");
    Check(Step(p, m, switched, 5000) == null && p.LastWarning != null && p.Unconfirmed.Count == 1, "unconfirmed clear reserves its own slot without reusing it");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    p.Sent(Step(p, m, [Drk(1) with { Job = 19 }], 1000)!.Value, 1000);
    m[0] = 999;
    Check(Step(p, m, [], 2000) == null && p.Owned.Count == 0 && p.YieldedSlots == 1, "replacement during pending clear yields the slot");
    m[0] = 0;
    Check(Step(p, m, c, 3000) == null, "conflicted clear does not release original target for remarking");
}
{
    var options = new Settings(30, 13, [19, 24], [8, 14, 5]);
    Candidate[] c = [Drk(1), Drk(2) with { Job = 19 }];
    var p = new MarkerPlanner(); var m = new uint[17];
    Check(Step(p, m, c, options: options) == new MarkerAction(8, 2, 0), "custom job and first marker used together");
    Check(!options.SupportsJob(32) && options.SupportsJob(19) && !options.SupportsJob(0), "custom job list replaces defaults and excludes unknown");
    Acknowledge(p, m, c, Step(p, m, c, options: options)!.Value);
    Check(Step(p, m, [Drk(2) with { Job = 24 }], 1000, options: options) == null, "switch between selected custom jobs keeps marker");
    Check(Step(p, m, [Drk(2)], 1000, options: options) == new MarkerAction(8, 0, 2), "switch to unselected job clears custom marker");
    Check(Step(p, m, [Drk(2)], 1000, options: options with { ClearOtherJobs = false }) == null, "optional job cleanup can be disabled");
    Check(Step(new(), new uint[17], c, options: options with { Jobs = [] }) == null, "empty job selection does not fall back to defaults");
    Check(Step(new(), new uint[17], c, options: options with { MarkerSlots = [] }) == null, "empty marker selection does not fall back to defaults");
    var occupied = new uint[17]; occupied[8] = 999;
    Check(Step(new(), occupied, c, options: options) == new MarkerAction(14, 2, 0), "custom order skips occupied first marker");
    Check((options with { MarkerSlots = [10, 14, 14, -1, 8, 99] }).ActiveSlots.SequenceEqual([14, 8]), "invalid and duplicate custom slots filtered");
    Check((options with { MaxMarkers = 1 }).ActiveSlots.SequenceEqual([8]), "limit applies after custom marker ordering");
    var normalized = MarkerOptions.NormalizeOrder([8, 8, -1, 10, 14]);
    Check(normalized.Length == 13 && normalized.Take(2).SequenceEqual([8, 14]), "saved marker order repairs missing and duplicate rows");
}
{
    var options = settings with { CombatOnly = true };
    var p = new MarkerPlanner(); var m = new uint[17];
    Check(Step(p, m, [Drk(1)], options: options) == null, "combat-only filter rejects idle enemy");
    Candidate[] c = [Drk(1) with { InCombat = true }];
    Check(Step(p, m, c, options: options)?.Target == 1, "combat-only filter accepts fighting enemy");
    Acknowledge(p, m, c, Step(p, m, c, options: options)!.Value);
    Check(Step(p, m, [Drk(1)], 1000, options: options) == null && p.Owned[0] == 1, "leaving combat never moves or clears existing marker");
    Check(Step(p, m, [Drk(1) with { Job = 19 }], 1000, options: options) == new MarkerAction(0, 0, 1), "job cleanup is independent of combat-only selection");
}
foreach (var interval in new[] { 0, 1000, 2500, 10000, 20000 })
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1), Dnc(2)];
    p.Sent(Step(p, m, c)!.Value, 0, interval);
    m[0] = 1;
    var expectedTime = Math.Clamp(interval, 1000, 10000);
    Check(Step(p, m, c, expectedTime - 1) == null, $"custom interval {interval}: blocks early second operation");
    Check(Step(p, m, c, expectedTime) == new MarkerAction(1, 2, 0), $"custom interval {interval}: resumes at configured boundary");
}
{
    Check(AutomationTiming.DelayRemaining(10000, 10000, 15) == 15000, "entry delay starts at territory entry");
    Check(AutomationTiming.DelayRemaining(24999, 10000, 15) == 1, "entry delay final millisecond");
    Check(AutomationTiming.DelayRemaining(25000, 10000, 15) == 0, "entry delay expires at boundary");
    Check(AutomationTiming.DelayRemaining(99999, 10000, 15) == 0, "entry delay does not repeat on toggle");
    Check(!AutomationTiming.CanOperate(true, 1, false, false), "waiting delay pauses operations");
    Check(!AutomationTiming.CanOperate(true, 0, true, true), "death pause blocks operations");
    Check(AutomationTiming.CanOperate(true, 0, true, false), "revival resumes operations");
    Check(AutomationTiming.CanOperate(true, 0, false, true), "death pause is optional");
    Check(!AutomationTiming.CanOperate(false, 0, false, false), "disabled always prevents operations");
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    p.Sent(Step(p, m, c)!.Value, 0);
    m[0] = 1;
    Check(Step(p, m, c, 1000, enabled: false) == null && p.Owned[0] == 1 && !p.Waiting, "pausing still acknowledges in-flight operation and keeps ownership");
}
{
    var previous = System.Text.Json.JsonSerializer.Deserialize<XivMark.Configuration>("{\"Version\":2,\"Enabled\":true,\"Range\":45,\"MaxMarkers\":8}")!;
    previous.Sanitize();
    Check(previous.Enabled && previous.Range == 45 && previous.MaxMarkers == 8 && previous.Jobs.SequenceEqual([32u, 38u]), "old configuration preserves range and switch with default jobs");
    Check(previous.IntervalMs == 1000 && previous.StartDelaySeconds == 0 && previous.ClearOtherJobs && !previous.CombatOnly && !previous.PauseWhenDead, "new options preserve previous behavior during migration");
    var custom = new XivMark.Configuration { Jobs = [19, 19, 0], MarkerOrder = [8, 8, 999], EnabledMarkers = [8, 10, 999], IntervalMs = 1, StartDelaySeconds = 999, Range = float.NaN };
    custom.Sanitize();
    Check(custom.Jobs.SequenceEqual([19u]) && custom.EnabledMarkers.SequenceEqual([8]) && custom.MarkerOrder.Length == 13, "config sanitizes invalid and duplicate job and marker selections");
    Check(custom.IntervalMs == 1000 && custom.StartDelaySeconds == 120 && custom.Range == 60, "config clamps timing and invalid distance");
    var saved = System.Text.Json.JsonSerializer.Serialize(custom);
    var restored = System.Text.Json.JsonSerializer.Deserialize<XivMark.Configuration>(saved)!;
    restored.Sanitize();
    Check(restored.Jobs.SequenceEqual(custom.Jobs) && restored.MarkerOrder.SequenceEqual(custom.MarkerOrder) && restored.EnabledMarkers.SequenceEqual(custom.EnabledMarkers), "custom choices and order survive serialization");
    Check(restored.ToSettings().ActiveSlots.SequenceEqual([8]) && restored.ToSettings().SupportsJob(19), "persisted settings reach planner");
    restored.Jobs = []; restored.EnabledMarkers = []; restored.Sanitize();
    Check(restored.Jobs.Length == 0 && !restored.ToSettings().ActiveSlots.Any(), "explicit empty selections stay empty when saved");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; m[1] = 99;
    Candidate[] c = [Drk(99) with { Job = 19 }, Dnc(2)];
    var clear = Step(p, m, c);
    Check(clear == new MarkerAction(1, 0, 99) && p.Owned.Count == 0, "untracked wrong-job mark is cleared before assigning any new marks");
    p.Sent(clear!.Value, 0);
    Check(Step(p, m, c, 999) == null && p.Waiting, "pre-existing clear waits for acknowledgement without repeat");
    m[1] = 0;
    Check(Step(p, m, c, 1000) == new MarkerAction(0, 2, 0) && !p.Waiting, "pre-existing cleanup acknowledgement permits new assignments");
    var reload = new MarkerPlanner(); m[1] = 99;
    Check(Step(reload, m, c) == clear, "fresh planner after reload still cleans untracked wrong-job marker");
    Check(Step(new(), m, c, enabled: false) == null, "disabled plugin keeps pre-existing markers");
    Check(Step(new(), m, c, options: settings with { ClearOtherJobs = false })?.Target != 0, "cleanup toggle prevents external marker cleanup");
}
foreach (var slot in MarkerOptions.DefaultOrder)
{
    var p = new MarkerPlanner(); var m = new uint[17]; m[slot] = 99;
    Candidate[] c = [Drk(99, 500) with { Job = 19, Alive = false, Targetable = false, InCombat = false }];
    Check(Step(p, m, c, options: settings with { CombatOnly = true }) == new MarkerAction(slot, 0, 99), $"{MarkerPlanner.MarkerName(slot)}: wrong-job cleanup ignores new-target distance/death/combat filters");
}
foreach (var reason in new[] { "ally", "unknown job", "unloaded", "selected job", "disabled marker", "shape" })
{
    var p = new MarkerPlanner(); var m = new uint[17]; m[1] = 99;
    Candidate[] c = [Drk(99) with { Job = 19 }];
    var options = settings;
    switch (reason)
    {
        case "ally": c = [c[0] with { Enemy = false }]; break;
        case "unknown job": c = [c[0] with { Job = 0 }]; break;
        case "unloaded": c = []; break;
        case "selected job": c = [Dnc(99)]; break;
        case "disabled marker": options = settings with { MarkerSlots = [0] }; break;
        case "shape": m[1] = 0; m[10] = 99; break;
    }
    Check(Step(p, m, c, options: options) == null, $"pre-existing cleanup preserves {reason}");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] original = [Drk(1)];
    Acknowledge(p, m, original, Step(p, m, original)!.Value);
    m[0] = 99;
    Candidate[] replacement = [Drk(99) with { Job = 19 }];
    var clear = Step(p, m, replacement, 1000);
    Check(clear == new MarkerAction(0, 0, 99), "explicit job cleanup also checks wrong-job marks in previously yielded slots");
    Acknowledge(p, m, replacement, clear!.Value, 1000);
    Check(p.YieldedSlots == 0 && Step(p, m, [Dnc(2)], 2000) == new MarkerAction(0, 2, 0), "successful deliberate cleanup releases previously yielded slot");
    var approved = Step(p, m, [], 3000);
    var refused = false;
    try { p.Sent(new MarkerAction(0, 0, 123), 3000); }
    catch (ArgumentException) { refused = true; }
    Check(approved == null && refused, "clear request requires approval from latest marker and job snapshot");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; m[1] = 99;
    Candidate[] c = [Drk(99) with { Job = 19 }, Dnc(2)];
    p.Sent(Step(p, m, c)!.Value, 0);
    Check(Step(p, m, c, 4000) == new MarkerAction(0, 2, 0) && p.Unconfirmed.ContainsKey(1), "external cleanup timeout reserves only its own slot");
    m[1] = 100;
    Step(p, m, [], 5000);
    Check(p.Unconfirmed.Count == 0 && p.YieldedSlots == 1 && m[1] == 100, "replacement during external cleanup is not mistaken for successful clear");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1, 20)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    m[0] = 0;
    var single = settings with { MaxMarkers = 1 };
    Candidate[] nearby = [Drk(1, 30), Dnc(2, 1)];
    Check(Step(p, m, nearby, 2000, options: single) == null && p.IsYielded(0), "disappeared mark starts empty-slot grace period");
    Check(Step(p, m, nearby, 6999, options: single) == null && p.RecoveredSlots == 0, "empty slot is not reused before five stable seconds");
    Check(Step(p, m, nearby, 7000, options: single) == new MarkerAction(0, 1, 0) && p.RecoveredSlots == 1, "restore original holder and same marker even when a different enemy is closer");
    Acknowledge(p, m, nearby, Step(p, m, nearby, 7000, options: single)!.Value, 7000);
    Check(Step(p, m, [Drk(1, 99), Dnc(2, 1)], 10000, options: single) == null && m[0] == 1, "restored occupied assignment never switches to a closer enemy");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    m[0] = 0;
    var single = settings with { MaxMarkers = 1 };
    Candidate[] nearby = [Drk(1, 500), Dnc(2, 5)];
    Step(p, m, nearby, 2000, options: single);
    Check(Step(p, m, nearby, 7000, options: single) == new MarkerAction(0, 2, 0), "empty slot can mark another eligible enemy when original holder is out of range");
    Check(p.Owned.Count == 0 && m[0] == 0, "recovery only proposes assignment and does not write marker snapshot");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    m[0] = 999;
    var single = settings with { MaxMarkers = 1 };
    Check(Step(p, m, c, 2000, options: single) == null, "occupied replacement remains protected");
    Check(Step(p, m, c, 20000, options: single) == null && m[0] == 999 && p.RecoveredSlots == 0, "time alone never frees an occupied marker");
    m[0] = 0;
    Step(p, m, c, 21000, options: single);
    m[0] = 888;
    Step(p, m, c, 24000, options: single);
    m[0] = 0;
    Step(p, m, c, 25000, options: single);
    Check(Step(p, m, c, 29000, options: single) == null, "brief reoccupation restarts stable-empty grace period");
    Check(Step(p, m, c, 30000, options: single) == new MarkerAction(0, 1, 0), "yielded marker is usable only after a fresh five-second empty period");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    m[0] = 0;
    var single = settings with { MaxMarkers = 1, ReuseEmptySlots = false };
    Step(p, m, c, 2000, options: single);
    Check(Step(p, m, c, 60000, options: single) == null && p.RecoveredSlots == 0 && p.IsYielded(0), "optional strict mode respects manual removal for the rest of the duty");
    Check(Step(p, m, c, 61000, options: single with { ReuseEmptySlots = true }) == new MarkerAction(0, 1, 0), "enabling refill can recover an already stable empty slot");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    m[0] = 0;
    var single = settings with { MaxMarkers = 1 };
    Step(p, m, c, 2000, enabled: false, options: single);
    Check(Step(p, m, c, 7000, enabled: false, options: single) == null && p.RecoveredSlots == 0, "paused automation does not refill slots");
    Check(Step(p, m, c, 8000, options: single) == new MarkerAction(0, 1, 0), "resuming after pause recovers stable empty slots");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    p.Sent(Step(p, m, c)!.Value, 0, 10000);
    m[0] = 1;
    Step(p, m, c, 250);
    m[0] = 0;
    Step(p, m, c, 1000);
    Check(Step(p, m, c, 6000) == null && p.RecoveredSlots == 1, "refill grace does not bypass a longer configured operation interval");
    Check(Step(p, m, c, 10000) == new MarkerAction(0, 1, 0), "refill proceeds when configured interval also expires");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    Acknowledge(p, m, c, Step(p, m, c)!.Value);
    m[0] = 0; m[14] = 1;
    var single = settings with { MaxMarkers = 1 };
    Step(p, m, c, 2000, options: single);
    Check(Step(p, m, c, 7000, options: single) == null, "recovery does not duplicate a target now marked in another slot");
    Check(Step(p, m, [Drk(1), Dnc(2)], 8000, options: single) == new MarkerAction(0, 2, 0), "recovered slot may serve another enemy while original mark remains elsewhere");
}
{
    var p = new MarkerPlanner(); var m = new uint[17];
    Candidate[] c = Enumerable.Range(1, 14).Select(id => Drk((uint)id)).ToArray();
    for (var i = 0; i < 13; i++) Acknowledge(p, m, c, Step(p, m, c, i * 1000)!.Value, i * 1000);
    Array.Clear(m);
    Step(p, m, c, 15000);
    Check(Step(p, m, c, 19999) == null, "all empty slots remain guarded during initial grace period");
    for (var i = 0; i < 13; i++)
    {
        var action = Step(p, m, c, 20000 + i * 1000);
        Check(action?.Target == (uint)i + 1 && action?.Slot == MarkerOptions.DefaultOrder[i], $"full-pool refill {i + 1} preserves original target-to-marker mapping");
        Acknowledge(p, m, c, action!.Value, 20000 + i * 1000);
    }
    Check(p.Owned.Count == 13 && p.RecoveredSlots == 13 && Step(p, m, c, 34000) == null, "full-pool recovery stops at capacity without moving any restored mark");
    Step(p, m, c, 35000, frontline: false);
    Check(p.RecoveredSlots == 0 && p.YieldedSlots == 0, "new duty resets recovery counters and grace periods");
}
{
    var p = new MarkerPlanner(); var m = new uint[17]; Candidate[] c = [Drk(1)];
    p.Sent(Step(p, m, c)!.Value, 0);
    Step(p, m, c, 4000);
    Check(Step(p, m, c, 30000) == null && p.Unconfirmed.Count == 1 && p.RecoveredSlots == 0, "refill never retries an unresolved native operation");
    var migrated = System.Text.Json.JsonSerializer.Deserialize<XivMark.Configuration>("{\"Version\":3,\"Enabled\":true,\"Range\":100}")!;
    migrated.Sanitize();
    Check(migrated.Version == 4 && migrated.ReuseEmptySlots && migrated.ToSettings().ReuseEmptySlots && migrated.Enabled && migrated.Range == 100, "old configuration enables refill without losing existing settings");
    migrated.ReuseEmptySlots = false;
    var restored = System.Text.Json.JsonSerializer.Deserialize<XivMark.Configuration>(System.Text.Json.JsonSerializer.Serialize(migrated))!;
    restored.Sanitize();
    Check(!restored.ReuseEmptySlots && !restored.ToSettings().ReuseEmptySlots, "strict manual-removal preference persists across reload");
}
Console.WriteLine($"\n{passed} checks passed.");
