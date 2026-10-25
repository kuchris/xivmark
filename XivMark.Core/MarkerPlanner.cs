namespace XivMark.Core;

public readonly record struct Candidate(uint Id, uint Job, float Distance, bool Enemy, bool Alive, bool Targetable, bool InCombat = false);
public readonly record struct MarkerAction(int Slot, uint Target, uint Expected);
public readonly record struct Settings(float Range, int MaxMarkers, IReadOnlyList<uint>? Jobs = null,
    IReadOnlyList<int>? MarkerSlots = null, bool CombatOnly = false, bool ClearOtherJobs = true, bool ReuseEmptySlots = true)
{
    public bool SupportsJob(uint job) => job != 0 && (Jobs?.Contains(job) ?? job is MarkerPlanner.DarkKnight or MarkerPlanner.Dancer);
    public bool ShouldClearJob(uint job) => ClearOtherJobs && job != 0 && !SupportsJob(job);
    public IEnumerable<int> ActiveSlots => (MarkerSlots ?? MarkerOptions.DefaultOrder)
        .Where(MarkerOptions.IsSupported).Distinct().Take(Math.Clamp(MaxMarkers, 0, 13));
}

/// <summary>Pure decision logic. Slot numbers are zero-based; native marker commands are one-based.</summary>
public sealed class MarkerPlanner
{
    public const uint DarkKnight = 32;
    public const uint Dancer = 38;
    public const long IntervalMs = 1000;
    public const long AcknowledgementTimeoutMs = 4000;
    public const long EmptySlotGraceMs = 5000;
    private readonly Dictionary<int, uint> owned = new();
    private readonly HashSet<int> yielded = new();
    private readonly HashSet<uint> markedTargets = new();
    private readonly Dictionary<int, MarkerAction> unconfirmed = new();
    private readonly Dictionary<int, uint> previousTargets = new();
    private readonly Dictionary<int, long> emptySince = new();
    // Native array order is Attack 1-5, Bind 1-3, Ignore 1-2, shapes, Attack 6-8.
    private MarkerAction? pending;
    private MarkerAction? proposedClear;
    private long sentAt;
    private long nextActionAt;

    public string? LastWarning { get; private set; }
    public string Status { get; private set; } = "Waiting for a Frontline snapshot.";
    public IReadOnlyDictionary<int, MarkerAction> Unconfirmed => unconfirmed;
    public IReadOnlyDictionary<int, uint> Owned => owned;
    public int YieldedSlots => yielded.Count;
    public int RecoveredSlots { get; private set; }
    public bool Waiting => pending != null;
    public bool IsYielded(int slot) => yielded.Contains(slot);

    public void Reset()
    {
        owned.Clear();
        yielded.Clear();
        markedTargets.Clear();
        unconfirmed.Clear();
        previousTargets.Clear();
        emptySince.Clear();
        RecoveredSlots = 0;
        pending = null;
        proposedClear = null;
        LastWarning = null;
        Status = "Idle - outside Frontline.";
        nextActionAt = 0;
    }

    public static uint Normalize(ulong id) => id == 0 || (uint)id == 0xE0000000 ? 0 : (uint)id;

    public static bool Eligible(Candidate c, Settings settings) =>
        c.Id != 0 && c.Id != 0xE0000000 && c.Enemy && c.Alive && c.Targetable &&
        float.IsFinite(c.Distance) && c.Distance >= 0 && c.Distance <= settings.Range &&
        (!settings.CombatOnly || c.InCombat);

    public static string MarkerName(int slot) => slot switch
    {
        >= 0 and <= 4 => $"Attack {slot + 1}",
        >= 14 and <= 16 => $"Attack {slot - 8}",
        >= 5 and <= 7 => $"Chain {slot - 4}",
        >= 8 and <= 9 => $"Stop {slot - 7}",
        _ => "Other",
    };

    /// <summary>Call with a fresh marker snapshot, then Send exactly once on the framework thread.</summary>
    public MarkerAction? Next(bool inFrontline, bool enabled, IReadOnlyList<Candidate> candidates,
        uint[] markers, Settings settings, long now)
    {
        proposedClear = null;
        if (!inFrontline)
        {
            Reset();
            return null;
        }
        if (markers.Length != 17)
            throw new ArgumentException("A complete 17-slot marker snapshot is required.", nameof(markers));

        // A late update still counts. Quarantine only the uncertain slot, never repeat its toggle,
        // and continue using other slots while waiting for evidence about the original operation.
        foreach (var (slot, lateRequest) in unconfirmed.ToArray())
        {
            if (markers[slot] == lateRequest.Target)
            {
                Complete(lateRequest);
                unconfirmed.Remove(slot);
            }
            else if (markers[slot] != lateRequest.Expected)
            {
                Yield(slot, lateRequest.Target == 0 ? lateRequest.Expected : lateRequest.Target);
                unconfirmed.Remove(slot);
            }
        }

        if (pending is { } request)
        {
            if (markers[request.Slot] == request.Target)
            {
                Complete(request);
                pending = null;
            }
            else if (markers[request.Slot] != request.Expected)
            {
                // Another marker owner won the race. Do not compete for this slot this duty.
                Yield(request.Slot, request.Target == 0 ? request.Expected : request.Target);
                pending = null;
            }
            else if (now - sentAt >= AcknowledgementTimeoutMs)
            {
                unconfirmed[request.Slot] = request;
                LastWarning = $"No state update for {MarkerName(request.Slot)} within 4 seconds. This slot is reserved; other slots continue. No retry was sent.";
                pending = null;
            }
            else
            {
                Status = $"Waiting for {MarkerName(request.Slot)} state update.";
                return null;
            }
        }

        foreach (var (slot, id) in owned.ToArray())
        {
            if (markers[slot] == id)
                continue;
            Yield(slot, id);
        }
        // Empty is different from occupied by another player. Allow stable empty slots to
        // recover after a grace period; never overwrite a marker that is still present.
        foreach (var slot in yielded.ToArray())
        {
            if (markers[slot] != 0)
            {
                emptySince.Remove(slot);
                continue;
            }
            if (!emptySince.TryGetValue(slot, out var since))
            {
                emptySince[slot] = now;
                since = now;
            }
            if (!enabled || !settings.ReuseEmptySlots || now - since < EmptySlotGraceMs)
                continue;
            yielded.Remove(slot);
            emptySince.Remove(slot);
            if (previousTargets.TryGetValue(slot, out var previous) &&
                !owned.Values.Contains(previous) &&
                !unconfirmed.Values.Any(a => a.Target == previous || a.Expected == previous) &&
                !(pending is { } active && (active.Target == previous || active.Expected == previous)))
                markedTargets.Remove(previous);
            RecoveredSlots++;
        }
        if (now < nextActionAt)
        {
            Status = "Waiting for the configured operation interval.";
            return null;
        }

        // Sticky assignments: eligibility only applies when choosing a NEW target.
        // Never infer a job change from absence, death, distance, or an unknown (zero) job.
        if (!enabled)
        {
            Status = "Paused - existing markers are kept.";
            return null;
        }
        // Cleanup is a job policy over the current marker snapshot, not just our in-memory
        // ownership list. This also covers markers left by a reload or placed elsewhere.
        // Previously owned slots remain eligible for cleanup if later disabled in settings.
        foreach (var slot in settings.ActiveSlots.Concat(owned.Keys).Distinct())
        {
            var id = markers[slot];
            if (id != 0 && !unconfirmed.ContainsKey(slot) &&
                candidates.Any(c => c.Id == id && c.Enemy && settings.ShouldClearJob(c.Job)))
            {
                Status = $"Clearing {MarkerName(slot)} from an enemy whose job is not selected.";
                proposedClear = new MarkerAction(slot, 0, id);
                return proposedClear;
            }
        }
        if (owned.Count >= Math.Clamp(settings.MaxMarkers, 0, 13))
        {
            Status = $"Waiting - configured marker limit reached ({owned.Count}/{settings.MaxMarkers}); keeping assignments.";
            return null;
        }

        var eligible = candidates.Where(c => Eligible(c, settings) && settings.SupportsJob(c.Job))
            .OrderBy(c => c.Distance).ThenBy(c => c.Id).ToArray();
        var slots = settings.ActiveSlots.ToArray();
        if (slots.Length == 0)
        {
            Status = "Waiting - no marker slots enabled.";
            return null;
        }
        foreach (var slot in slots)
        {
            if (markers[slot] != 0 || yielded.Contains(slot) || unconfirmed.ContainsKey(slot))
                continue;
            var available = eligible.Where(c => !markers.Contains(c.Id) && !markedTargets.Contains(c.Id)).ToArray();
            // If the original holder is still eligible, restore the same marker first.
            var candidate = previousTargets.TryGetValue(slot, out var preferred)
                ? available.FirstOrDefault(c => c.Id == preferred) : default;
            if (candidate.Id == 0)
                candidate = available.FirstOrDefault();
            if (candidate.Id != 0)
            {
                Status = $"Assigning {MarkerName(slot)} to {candidate.Id:X8}.";
                return new MarkerAction(slot, candidate.Id, 0);
            }
        }
        var free = slots.Count(s => markers[s] == 0 && !yielded.Contains(s) && !unconfirmed.ContainsKey(s));
        if (free == 0)
            Status = $"Waiting - no available slots: {slots.Count(s => markers[s] != 0)} occupied, {slots.Count(yielded.Contains)} yielded, {slots.Count(unconfirmed.ContainsKey)} unconfirmed.";
        else if (eligible.Length == 0)
        {
            var selected = candidates.Where(c => c.Enemy && settings.SupportsJob(c.Job)).ToArray();
            Status = $"Waiting - no eligible enemies: {selected.Length} loaded, {selected.Count(c => c.Alive && c.Targetable && c.Distance <= settings.Range)} alive/targetable/in range. Combat-only: {settings.CombatOnly}.";
        }
        else
            Status = "Waiting - all eligible enemies already marked or reserved after an earlier operation.";
        return null;
    }

    private void Complete(MarkerAction request)
    {
        if (request.Target == 0)
        {
            owned.Remove(request.Slot);
            // Deliberate job-change cleanup releases the target, unlike an external edit.
            markedTargets.Remove(request.Expected);
            yielded.Remove(request.Slot);
            previousTargets.Remove(request.Slot);
            emptySince.Remove(request.Slot);
        }
        else
            owned[request.Slot] = request.Target;
    }

    private void Yield(int slot, uint previousTarget)
    {
        owned.Remove(slot);
        yielded.Add(slot);
        previousTargets[slot] = previousTarget;
        emptySince.Remove(slot);
    }

    public void Sent(MarkerAction request, long now, int intervalMs = 1000)
    {
        if (pending != null)
            throw new InvalidOperationException("A marker request is already pending.");
        var assignment = request.Target is not (0 or 0xE0000000) && request.Expected == 0;
        var approvedClear = request.Target == 0 && proposedClear == request;
        if (!MarkerOptions.IsSupported(request.Slot) || unconfirmed.ContainsKey(request.Slot) || (!assignment && !approvedClear))
            throw new ArgumentException("Only empty-slot assignments or clears approved by the latest marker/job snapshot are allowed.", nameof(request));
        pending = request;
        proposedClear = null;
        if (request.Target != 0)
        {
            markedTargets.Add(request.Target);
            previousTargets.Remove(request.Slot);
        }
        sentAt = now;
        nextActionAt = now + Math.Clamp(intervalMs, 1000, 10000);
    }
}
