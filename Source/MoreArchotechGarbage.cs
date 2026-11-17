using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("MrKociak.MoreArchotechStuffandThingsReupload")]
[MpCompatFor("Taranchuk.MoreArchotechGarbageContinued")]
[MpCompatFor("zal.morearchotechgarbage")]
internal class MoreArchotechGarbage
{
	public MoreArchotechGarbage(ModContentPack mod)
	{
		string[] array = new string[9] { "MoreArchotechGarbage.CompSpawnerH", "MoreArchotechGarbage.CompSpawnerResearch", "MoreArchotechGarbage.CompSpawnerResearchMK2", "MoreArchotechGarbage.JoinOrRaidTameOrManhunter", "MoreArchotechGarbage.MechhSummon", "MoreArchotechGarbage.MechhSummonNoRoyalty", "MoreArchotechGarbage.CompSpawnerExtraRaidsOne", "MoreArchotechGarbage.CompSpawnerExtraRaidsTwo", "MoreArchotechGarbage.CompSpawnerExtraRaidsThree" };
		for (int i = 0; i < array.Length; i++)
		{
			PatchingUtilities.PatchSystemRand(array[i] + ":RandomNumber");
		}
		Type parentType = AccessTools.TypeByName("MoreArchotechGarbage.MechhSummon");
		MpCompat.RegisterLambdaDelegate(parentType, "GetGizmos", 0, 2);
		MpCompat.RegisterLambdaMethod(parentType, "GetGizmos", 4);
		parentType = AccessTools.TypeByName("MoreArchotechGarbage.MechhSummonNoRoyalty");
		MpCompat.RegisterLambdaDelegate(parentType, "GetGizmos", default(int));
		MpCompat.RegisterLambdaMethod(parentType, "GetGizmos", 2);
		MpCompat.RegisterLambdaDelegate("MoreArchotechGarbage.PodSummoner", "GetGizmos", default(int));
		MpCompat.RegisterLambdaMethod("MoreArchotechGarbage.CompSpawnerH", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("MoreArchotechGarbage.CompSpawnerResearch", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("MoreArchotechGarbage.CompSpawnerResearchMK2", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("MoreArchotechGarbage.CompSpawnerExtraRaidsOne", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("MoreArchotechGarbage.CompSpawnerExtraRaidsTwo", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("MoreArchotechGarbage.CompSpawnerExtraRaidsThree", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("MoreArchotechGarbage.JoinOrRaidTameOrManhunter", "GetGizmos", 0, 3);
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("MoreArchotechGarbage.Building_MAG_HibernationStarter:MAG_StartupHibernatingParts"));
	}
}
