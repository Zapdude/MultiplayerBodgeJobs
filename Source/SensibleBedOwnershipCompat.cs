using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sensiblebedownership.1trickPwnyta")]
public class SensibleBedOwnershipCompat
{
	public SensibleBedOwnershipCompat(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.Method("SensibleBedOwnership.Patch_CompAssignableToPawn_Bed_TryUnassignPawn:Prefix"));
		MP.RegisterSyncMethod(AccessTools.Method("SensibleBedOwnership.Patch_CompAssignableToPawn_DeathrestCasket_TryUnassignPawn:Prefix"));
		MP.RegisterSyncDelegate(AccessTools.TypeByName("SensibleBedOwnership.Patch_CompAssignableToPawn"), "<>c__DisplayClass2_2", "<Postfix>b__3");
	}
}
