using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VEE")]
internal class VEE
{
	public VEE(ModContentPack mod)
	{
		MpSyncWorkers.Requires<GameCondition>();
		PatchingUtilities.PatchSystemRand("VEE.RegularEvents.MeteoriteShower:TryExecuteWorker", patchPushPop: false);
		PatchingUtilities.PatchPushPopRand("VEE.IncomingSmoker:ThrowBlackSmoke");
		MpCompat.RegisterLambdaDelegate("VEE.Settings.VEESettings", "ResetWorldCondButton", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaDelegate("VEE.Settings.VEESettings", "ResetMapCondButton", default(int)).SetDebugOnly();
		RegisterChoiceLetterLambdas("VEE.ChoiceLetter_AcceptCrashlanders");
		RegisterChoiceLetterLambdas("VEE.ChoiceLetter_WhiteoutRefugees");
		MpCompat.RegisterLambdaMethod("VEE.HediffComp_Traitor", "CompGetGizmos", default(int)).SetDebugOnly();
	}

	private static void RegisterChoiceLetterLambdas(string typeName)
	{
		foreach (MethodInfo lambdum in MpMethodUtil.GetLambda(AccessTools.TypeByName(typeName), "Choices", MethodType.Getter, null, 0, 1))
		{
			MP.RegisterSyncMethod(lambdum);
		}
	}
}
