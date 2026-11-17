using System;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

internal static class IsekaiCreaturesAsyncTime
{
	private static bool installed;

	internal static void Install()
	{
		if (!installed)
		{
			Type type = AccessTools.TypeByName("IsekaiCreatures.Combat.CompAoeStrike") ?? throw new TypeLoadException("IsekaiCreatures.Combat.CompAoeStrike");
			AccessTools.FieldRef<object, int> lastStrike = AccessTools.FieldRefAccess<int>(type, "lastStrikeTick");
			AccessTools.FieldRef<object, int> pendingTick = AccessTools.FieldRefAccess<int>(type, "pendingTick");
			AccessTools.FieldRef<object, IntVec3> pendingCell = AccessTools.FieldRefAccess<IntVec3>(type, "pendingCell");
			PatchingUtilities.RegisterTimestampFields(type, (object component, int offset) =>
			{
				lastStrike(component) = lastStrike(component) + offset;
				pendingTick(component) = -1;
				pendingCell(component) = IntVec3.Invalid;
			});
			installed = true;
		}
	}
}
