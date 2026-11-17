using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;
using ZombieLand;

namespace Multiplayer.Compat;

[MpCompatFor("brrainz.zombieland")]
public class ZombieLand
{
	public ZombieLand(ModContentPack mod)
	{
		Tools.avoider.running = false;
		PatchingUtilities.PatchUnityRand((MethodBase)AccessTools.Method(typeof(SubEffecter_ZombieShocker), "RandomZap"), false);
		PatchingUtilities.PatchUnityRand((MethodBase)AccessTools.Method(typeof(SubEffecter_ZombieShocker), "ZapNextCell"), false);
		HarmonyMethod transpiler = new HarmonyMethod(typeof(ZombieLand), "ReplaceCoroutineCall");
		MpCompat.harmony.Patch(AccessTools.Method(typeof(ZombiesRising), "TryExecute"), null, null, transpiler);
		MpCompat.harmony.Patch(AccessTools.Method(typeof(ZombieAvoider), "UpdateZombiePositions"), null, null, transpiler);
		MpCompat.harmony.Patch(AccessTools.Method(typeof(ZombieAvoider), "GetCostsGrid"), new HarmonyMethod(typeof(ZombieLand), "PreGetCostsGrid"));
		MP.RegisterSyncMethod(typeof(ColonistConfig), "ToggleAutoExtractZombieSerum");
		MP.RegisterSyncMethod(typeof(ColonistConfig), "ToggleAutoDoubleTap");
		MP.RegisterSyncMethod(typeof(ColonistConfig), "ToggleAutoAvoidZombies");
		MP.RegisterSyncWorker<ColonistConfig>(SyncColonistConfig);
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Constants.random = (System.Random)(object)RandRedirector.Instance;
	}

	private static void SyncColonistConfig(SyncWorker sync, ref ColonistConfig config)
	{
		if (sync.isWriting)
		{
			ColonistConfig tempConfig = config;
			Pawn key = ColonistSettings.colonists.FirstOrDefault((KeyValuePair<Pawn, ColonistConfig> x) => x.Value == tempConfig).Key;
			sync.Write(key);
		}
		else
		{
			Pawn key2 = sync.Read<Pawn>();
			config = ColonistSettings.colonists[key2];
		}
	}

	private static void RedirectCoroutine(IEnumerator coroutine)
	{
		ZombieLandMpComponent.Coroutines.Add(coroutine);
	}

	private static bool PreGetCostsGrid(Map map, ref AvoidGrid __result)
	{
		Queue<AvoidGrid> val = ZombieLandMpComponent.QueueForMap(map);
		if (((IEnumerable<AvoidGrid>)val).Any())
		{
			__result = val.Dequeue();
		}
		return false;
	}

	private static IEnumerable<CodeInstruction> ReplaceCoroutineCall(IEnumerable<CodeInstruction> instr, MethodBase original)
	{
		MethodInfo coroutineMethod = AccessTools.Method(typeof(MonoBehaviour), "StartCoroutine", new Type[1] { typeof(IEnumerator) });
		MethodInfo coroutineReplacement = AccessTools.Method(typeof(ZombieLand), "RedirectCoroutine");
		MethodInfo enqueueMethod = AccessTools.Method(typeof(ConcurrentQueue<>).MakeGenericType(typeof(AvoidRequest)), "Enqueue");
		MethodInfo enqueueReplacement = AccessTools.Method(typeof(ZombieLandMpComponent), "Enqueue");
		bool patched = false;
		foreach (CodeInstruction item in instr)
		{
			if (item.opcode == OpCodes.Callvirt && item.operand is MethodInfo methodInfo)
			{
				if (methodInfo == coroutineMethod)
				{
					item.operand = coroutineReplacement;
					patched = true;
				}
				else if (methodInfo == enqueueMethod)
				{
					item.operand = enqueueReplacement;
					item.opcode = OpCodes.Call;
					patched = true;
				}
			}
			yield return item;
		}
		if (!patched)
		{
			Log.Warning("Failed to patch ZombieLand method: " + original.FullDescription());
		}
	}
}
