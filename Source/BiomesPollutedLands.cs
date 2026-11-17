using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("BiomesTeam.BiomesPollutedLands")]
public class BiomesPollutedLands
{
	public BiomesPollutedLands(ModContentPack mod)
	{
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("BMT_PollutedLands.Gene_AddHediffWithInterval:Pain")).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("BMT_PollutedLands.Gene_AddOrRemoveHediff", "GetGizmos", default(int)).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("BMT_PollutedLands.Gene_BloodExplusion:Vomit")).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("BMT_PollutedLands.Gene_CarrionMetabolism", "GetGizmos", default(int)).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("BMT_PollutedLands.Gene_MoltingRegeneration:TryHealWound")).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("BMT_PollutedLands.Gene_ToxspewingPores", "GetGizmos", default(int)).SetDebugOnly();
		PatchingUtilities.PatchSystemRand("BMT_PollutedLands.Hediff_Mutapox:Tick");
	}
}
