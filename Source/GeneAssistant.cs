using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("rimworld.randomcoughdrop.geneassistant")]
public class GeneAssistant
{
	public GeneAssistant(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("RandomsGeneAssistant.PatchGeneAssemblerEject");
		MP.RegisterSyncMethod(AccessTools.Method(type, "EjectDuplicateGenepacks"));
		MP.RegisterSyncMethod(AccessTools.Method(type, "EjectCosmeticGenepacks"));
		MP.RegisterSyncMethod(AccessTools.Method(type, "EjectCombinedGenepacks"));
	}
}
