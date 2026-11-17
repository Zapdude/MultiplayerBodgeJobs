using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class ApexMechanoidsAsyncTime
{
	private static bool installed;

	private static FieldInfo raidPhase;

	private static FieldInfo purifierCondition;

	private static Func<int> worldTicks;

	private static AccessTools.FieldRef<object, int> phaseDeadline;

	internal static void Install()
	{
		if (installed)
		{
			return;
		}
		PatchingUtilities.SetupAsyncTime();
		Type type = AccessTools.TypeByName("ApexMechanoids.CompCelerusRaidController") ?? throw new TypeLoadException("ApexMechanoids.CompCelerusRaidController");
		raidPhase = AccessTools.Field(type, "phase") ?? throw new MissingFieldException(type.FullName, "phase");
		phaseDeadline = AccessTools.FieldRefAccess<int>(type, "phaseEndTick");
		PatchingUtilities.RegisterTimestampFields(type, (object component, int offset) =>
		{
			if (Convert.ToInt32(raidPhase.GetValue(component)) != 0)
			{
				phaseDeadline(component) = phaseDeadline(component) + offset;
			}
		});
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(type, "PhaseExpired"), new HarmonyMethod(typeof(ApexMechanoidsAsyncTime), "PhaseExpired"));
		RegisterPurifierCache();
		worldTicks = AccessTools.MethodDelegate<Func<int>>(AccessTools.Method("Multiplayer.Client.AsyncTimeComp:GetWorldTicks") ?? throw new MissingMethodException("AsyncTimeComp", "GetWorldTicks"));
		Type type2 = AccessTools.TypeByName("ApexMechanoids.CompUseEffect_CallMulBossgroup") ?? throw new TypeLoadException("ApexMechanoids.CompUseEffect_CallMulBossgroup");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "CallBossgroup"), null, null, new HarmonyMethod(typeof(ApexMechanoidsAsyncTime), "BossCallTimestamp"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "CanResolve"), null, null, new HarmonyMethod(typeof(ApexMechanoidsAsyncTime), "BossAvailabilityClock"));
		installed = true;
	}

	private static void RegisterPurifierCache()
	{
		Type type = AccessTools.TypeByName("ApexMechanoids.CompToxicPurifier") ?? throw new TypeLoadException("ApexMechanoids.CompToxicPurifier");
		purifierCondition = AccessTools.Field(type, "conditionCached") ?? throw new MissingFieldException(type.FullName, "conditionCached");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "PostSpawnSetup"), new HarmonyMethod(typeof(ApexMechanoidsAsyncTime), "BeforePurifierSpawn"));
		MethodInfo invalidate = AccessTools.DeclaredMethod(type, "InvalidateCellToUnpolluteCache") ?? throw new MissingMethodException(type.FullName, "InvalidateCellToUnpolluteCache");
		PatchingUtilities.RegisterTimestampFields(type, (object component, int _) =>
		{
			invalidate.Invoke(component, null);
		});
	}

	private static void BeforePurifierSpawn(ThingComp __instance)
	{
		if (MP.IsInMultiplayer && PatchingUtilities.IsAsyncTime && purifierCondition.GetValue(__instance) is GameCondition { gameConditionManager: var gameConditionManager } && gameConditionManager?.ownerMap != __instance.parent.Map)
		{
			purifierCondition.SetValue(__instance, null);
		}
	}

	private static int GlobalBossTicks(int nativeTicks)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return nativeTicks;
		}
		return worldTicks();
	}

	private static IEnumerable<CodeInstruction> BossCallTimestamp(IEnumerable<CodeInstruction> instructions)
	{
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Stfld && instruction.operand is FieldInfo fieldInfo && fieldInfo.DeclaringType == typeof(GameComponent_Bossgroup) && fieldInfo.Name == "lastBossgroupCalled")
			{
				yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ApexMechanoidsAsyncTime), "GlobalBossTicks")).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
				replaced++;
			}
			yield return instruction;
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException("Apex boss call timestamp pattern changed");
		}
	}

	private static IEnumerable<CodeInstruction> BossAvailabilityClock(IEnumerable<CodeInstruction> instructions)
	{
		int replaced = 0;
		MethodInfo ticks = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction;
			if (instruction.Calls(ticks))
			{
				yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ApexMechanoidsAsyncTime), "GlobalBossTicks"));
				replaced++;
			}
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException("Apex boss availability clock pattern changed");
		}
	}

	private static bool PhaseExpired(object __instance, ref bool __result)
	{
		if (!MP.IsInMultiplayer || !PatchingUtilities.IsAsyncTime)
		{
			return true;
		}
		__result = Convert.ToInt32(raidPhase.GetValue(__instance)) != 0 && Find.TickManager.TicksGame >= phaseDeadline(__instance);
		return false;
	}
}
