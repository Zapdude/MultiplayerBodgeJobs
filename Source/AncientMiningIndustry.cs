using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("XMB.AncientMiningIndustry.MO")]
public class AncientMiningIndustry
{
	private static SyncType questScriptPairType;

	public AncientMiningIndustry(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("AncientMining.Building_BoringMachine", "GetGizmos", 0, 1);
		MpCompat.RegisterLambdaMethod("AncientMining.CompDeepDrillAutomated", "CompGetGizmosExtra", 0, 1).SetDebugOnly();
		MpCompat.RegisterLambdaDelegate("AncientMining.CompQuestScanner", "CompGetGizmosExtra", 1, 3).SetContext(SyncContext.MapSelected);
		questScriptPairType = AccessTools.TypeByName("AncientMining.QuestScriptTexPathPair");
		questScriptPairType.expose = true;
		MP.RegisterSyncWorker<object>(SyncQuestScriptTexPathPair, questScriptPairType.type);
	}

	private static void SyncQuestScriptTexPathPair(SyncWorker sync, ref object obj)
	{
		sync.Bind(ref obj, questScriptPairType);
	}
}
