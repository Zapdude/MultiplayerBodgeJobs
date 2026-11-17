using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Mehni.PickUpAndHaul")]
public class PickupAndHaul
{
	public PickupAndHaul(ModContentPack mod)
	{
		MpCompat.harmony.Patch(AccessTools.Method("PickUpAndHaul.WorkGiver_HaulToInventory:PotentialWorkThingsGlobal"), null, null, new HarmonyMethod(typeof(PickupAndHaul), "PotentialWorkThingsGlobalTranspiler"));
		MpCompat.harmony.Patch(AccessTools.Method("PickUpAndHaul.JobDriver_UnloadYourHauledInventory:FirstUnloadableThing"), null, null, new HarmonyMethod(typeof(PickupAndHaul), "FirstUnloadableThingTranspiler"));
		MpCompat.harmony.Patch(AccessTools.Method("PickUpAndHaul.PawnUnloadChecker:CheckIfPawnShouldUnloadInventory", new Type[2]
		{
			typeof(Pawn),
			typeof(bool)
		}), new HarmonyMethod(typeof(PickupAndHaul), "AllowUnloadScheduling"));
	}

	private static bool AllowUnloadScheduling()
	{
		if (MP.IsInMultiplayer)
		{
			if (Scribe.mode != LoadSaveMode.LoadingVars && Scribe.mode != LoadSaveMode.ResolvingCrossRefs)
			{
				return Scribe.mode != LoadSaveMode.PostLoadInit;
			}
			return false;
		}
		return true;
	}

	private static IEnumerable<CodeInstruction> PotentialWorkThingsGlobalTranspiler(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo method = AccessTools.Method(typeof(ListerHaulables), "ThingsPotentiallyNeedingHauling");
		ConstructorInfo constructorInfo = AccessTools.Constructor(typeof(List<Thing>), new Type[1] { typeof(IEnumerable<Thing>) });
		MethodInfo method2 = AccessTools.Method(typeof(Enumerable), "ToList").MakeGenericMethod(typeof(Thing));
		List<CodeInstruction> list = instr.ToList();
		bool flag = false;
		for (int i = 0; i < list.Count; i++)
		{
			CodeInstruction codeInstruction = list[i];
			if (!codeInstruction.Calls(method))
			{
				continue;
			}
			flag = true;
			int j;
			for (j = i + 1; j < list.Count && list[j].opcode == OpCodes.Nop; j++)
			{
			}
			if (j < list.Count && (list[j].Calls(method2) || (list[j].opcode == OpCodes.Newobj && object.Equals(list[j].operand, constructorInfo))))
			{
				continue;
			}
			CodeInstruction codeInstruction2 = new CodeInstruction(OpCodes.Newobj, constructorInfo);
			codeInstruction2.blocks.AddRange(codeInstruction.blocks.Where((ExceptionBlock b) => b.blockType == ExceptionBlockType.EndExceptionBlock));
			if (codeInstruction2.blocks.Count > 0)
			{
				codeInstruction = new CodeInstruction(codeInstruction);
				codeInstruction.blocks.RemoveAll((ExceptionBlock b) => b.blockType == ExceptionBlockType.EndExceptionBlock);
				list[i] = codeInstruction;
			}
			list.Insert(++i, codeInstruction2);
		}
		if (!flag)
		{
			throw new Exception("Failed patching Pickup and Haul: PotentialWorkThingsGlobal");
		}
		return list;
	}

	private static IEnumerable<CodeInstruction> FirstUnloadableThingTranspiler(IEnumerable<CodeInstruction> instr)
	{
		bool patched = false;
		foreach (CodeInstruction ci in instr)
		{
			yield return ci;
			if (!patched && ci.operand is MethodInfo methodInfo && methodInfo.Name.Contains("ThenBy"))
			{
				yield return CodeInstruction.Call(typeof(PickupAndHaul), "SortByThingIDNumber");
				patched = true;
			}
		}
		if (!patched)
		{
			throw new Exception("Failed patching Pickup and Haul: FirstUnloadableThing");
		}
	}

	private static IOrderedEnumerable<Thing> SortByThingIDNumber(IOrderedEnumerable<Thing> carriedThings)
	{
		return carriedThings.ThenBy((Thing x) => x.thingIDNumber);
	}
}
