using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using XivMark.Core;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeTargetType = FFXIVClientStructs.FFXIV.Client.Game.Object.TargetType;

namespace XivMark;

internal sealed unsafe class NativeMarkers
{
    // Native command entry point (one-based mark ID), also documented by Lemegeton Core/State.cs.
    // This is NOT the zero-based local-only marker setter. No hooks or direct marker-array writes.
    private const string Signature = "48 89 5C 24 10 48 89 6C 24 18 57 48 83 EC 20 8D 42";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte MarkDelegate(nint controller, byte marker, uint actorId);
    private readonly MarkDelegate? mark;
    public string Status { get; }
    public bool Ready => mark != null;

    public NativeMarkers(ISigScanner scanner)
    {
        if (!scanner.TryScanText(Signature, out var address))
        {
            Status = "Native marker signature not found. Marking is unavailable on this client.";
            return;
        }
        mark = Marshal.GetDelegateForFunctionPointer<MarkDelegate>(address);
        Status = "Native marker function found (live behavior still needs verification).";
    }

    public uint[]? Read()
    {
        var controller = MarkingController.Instance();
        if (controller == null)
            return null;
        var result = new uint[17];
        for (var i = 0; i < result.Length; i++)
            result[i] = MarkerPlanner.Normalize(controller->Markers[i]);
        return result;
    }

    public bool Send(MarkerAction action, IObjectTable objects, Settings settings)
    {
        var clearing = action.Target == 0;
        if (mark == null || action.Slot is < 0 or >= 17 or (>= 10 and <= 13) ||
            action.Target == 0xE0000000 || (clearing ? action.Expected is 0 or 0xE0000000 : action.Expected != 0))
            return false;
        var controller = MarkingController.Instance();
        if (controller == null || MarkerPlanner.Normalize(controller->Markers[action.Slot]) != action.Expected)
            return false;
        if (clearing)
        {
            // Recheck the actual loaded player's job and enemy state before clearing any mark.
            var player = objects.OfType<IPlayerCharacter>().FirstOrDefault(p => p.EntityId == action.Expected);
            if (player == null || !settings.ShouldClearJob(player.ClassJob.RowId) ||
                (player.StatusFlags & (StatusFlags.PartyMember | StatusFlags.AllianceMember)) != 0 ||
                ((NativeObject*)player.Address)->GetTargetType() != NativeTargetType.Enemy)
                return false;
        }
        else
        {
            // Do not take a target already marked in any other slot, including shapes.
            for (var i = 0; i < 17; i++)
                if (MarkerPlanner.Normalize(controller->Markers[i]) == action.Target)
                    return false;
        }

        // Repeating a mark on its current holder toggles it off. Never repeat while awaiting state confirmation.
        _ = mark((nint)controller, (byte)(action.Slot + 1), clearing ? action.Expected : action.Target);
        return true; // Request issued, not server acknowledgement. Planner observes the next snapshot.
    }
}
