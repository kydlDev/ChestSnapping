using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KydlCore.Common;

namespace ChestSnapping;

/// <summary>
/// Keeps a stacked chest alive. WearNTear.UpdateSupport skips every neighbour whose m_supports is false, so a chest on a chest finds no
/// support and the game destroys it a moment after it is placed. This transpiler replaces the two reads of the neighbour's
/// <c>m_supports</c> in UpdateSupport by <see cref="Supports"/>, which says "supports" for a chest under another chest and
/// otherwise returns the field unchanged. Nothing else changes: chests never become supports for floors, walls or anything that is not a chest,
/// and the field itself is never written.
/// </summary>
[HarmonyPatch(typeof(WearNTear), "UpdateSupport")]
internal static class StackingSupportPatch
{
    private static readonly FieldInfo SupportsField = AccessTools.Field(typeof(WearNTear), "m_supports");
    private static readonly MethodInfo SupportsMethod = AccessTools.Method(typeof(StackingSupportPatch), nameof(Supports));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && ReferenceEquals(instruction.operand, SupportsField))
            {
                // stack: [neighbour WearNTear] -> push 'this' -> Supports(neighbour, this) -> bool
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, SupportsMethod);
                replaced++;
                continue;
            }

            yield return instruction;
        }

        ModLog.Verbose("StackingSupportPatch: UpdateSupport patched, " + replaced + " read(s) of m_supports replaced");
    }

    /// <summary>Does <paramref name="neighbour"/> count as a support for <paramref name="self"/> in the game's building support?</summary>
    /// <param name="neighbour">The piece underneath / next to it (WearNTear.m_supports is its own value).</param>
    /// <param name="self">The piece whose support is being computed.</param>
    /// <returns>The neighbour's m_supports, or true for a chest under a chest while stacking is on.</returns>
    internal static bool Supports(WearNTear neighbour, WearNTear self)
    {
        if (neighbour.m_supports) return true;
        if (!ModSettings.StackingOn || self == null) return false;

        return ChestSnapPoints.IsChest(self.gameObject) && ChestSnapPoints.IsChest(neighbour.gameObject);
    }
}
