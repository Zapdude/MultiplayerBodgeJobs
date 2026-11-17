using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("RH2.Faction.VOID")]
public class ChickenCorpsesCompat
{
	public ChickenCorpsesCompat(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand(AccessTools.Method(AccessTools.TypeByName("ChickenCorpses.CompDecayAfterDelay"), "MakeMotes"));
	}
}
