using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("hlx.GestaltEngine")]
[MpCompatFor("hlx.ReinforcedMechanoids2")]
public class GestaltEngine
{
	private static AccessTools.FieldRef<IEnumerable> gestaltEnginesField;

	private static AccessTools.FieldRef<ThingComp, Pawn> gestaltEngineDummyPawnField;

	private static FastInvokeHandler minLevelGetterCall;

	private static FastInvokeHandler maxLevelGetterCall;

	public GestaltEngine(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("GestaltEngine.CompGestaltEngine");
		MP.RegisterSyncMethod(type, "StartConnect");
		MP.RegisterSyncMethod(type, "StartConnectNonColonyMech");
		MpCompat.RegisterLambdaMethod("GestaltEngine.CompUpgradeable", "CompGetGizmosExtra", 0, 1, 2, 3).TakeLast(2).SetDebugOnly();
		MpCompat.RegisterLambdaDelegate("GestaltEngine.CompBandNode_CompGetGizmosExtra_Patch", "Postfix", 2, 3);
		Type type2 = AccessTools.TypeByName("GestaltEngine.CompUpgradeable");
		minLevelGetterCall = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter(type2, "MinLevel"));
		maxLevelGetterCall = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter(type2, "MaxLevel"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "StartUpgrade"), new HarmonyMethod(typeof(GestaltEngine), "PreUpgradeSafetyChecks"));
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			AccessTools.DeclaredMethod("GestaltEngine.Utils:ResetStaticData").Invoke(null, Array.Empty<object>());
		});
		gestaltEnginesField = AccessTools.StaticFieldRefAccess<IEnumerable>(AccessTools.DeclaredField(type, "compGestaltEngines"));
		gestaltEngineDummyPawnField = AccessTools.FieldRefAccess<ThingComp, Pawn>(AccessTools.DeclaredField(type, "dummyPawn"));
		object value = AccessTools.DeclaredField("Multiplayer.Client.SyncDict:syncWorkers").GetValue(null);
		object obj = AccessTools.DeclaredMethod("Multiplayer.Client.SyncWorkerDictionaryTree:GetOrAddEntry").Invoke(value, new object[3]
		{
			typeof(MechanitorControlGroup),
			false,
			false
		});
		AccessTools.DeclaredMethod("Multiplayer.Client.SyncWorkerEntry:Add", new Type[1] { typeof(MethodInfo) }).Invoke(obj, new object[1] { AccessTools.DeclaredMethod(typeof(GestaltEngine), "SyncMechanitorControlGroup") });
	}

	private static bool PreUpgradeSafetyChecks(ThingComp __instance, int upgradeOffset, int ___level, int ___upgradeProgressTick, int ___downgradeProgressTick)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (___upgradeProgressTick > 0 || ___downgradeProgressTick > 0)
		{
			return false;
		}
		int num = ___level + upgradeOffset;
		if (num < (int)minLevelGetterCall(__instance) || num > (int)maxLevelGetterCall(__instance))
		{
			return false;
		}
		return true;
	}

	private static bool SyncMechanitorControlGroup(SyncWorker sync, ref MechanitorControlGroup group)
	{
		if (sync.isWriting)
		{
			Pawn targetPawn = group.tracker.Pawn;
			if (targetPawn.Spawned)
			{
				sync.Write(obj: true);
				sync.Write(targetPawn);
			}
			else
			{
				ThingComp thingComp = gestaltEnginesField().Cast<ThingComp>().FirstOrDefault((ThingComp c) => gestaltEngineDummyPawnField(c) == targetPawn);
				if (thingComp != null)
				{
					sync.Write(obj: false);
					sync.Write(thingComp);
				}
				else
				{
					sync.Write(obj: true);
					sync.Write(targetPawn);
				}
			}
			sync.Write(group.tracker.controlGroups.IndexOf(group));
		}
		else
		{
			Pawn pawn = ((!sync.Read<bool>()) ? gestaltEngineDummyPawnField(sync.Read<ThingComp>()) : sync.Read<Pawn>());
			int index = sync.Read<int>();
			group = pawn.mechanitor.controlGroups[index];
		}
		return true;
	}
}
