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

[MpCompatFor("Small.Guns.of.the.Mechanoid.Race")]
public class SmallGuns
{
	public SmallGuns(ModContentPack mod)
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(BackCompatibilityConverter_Universal), "PostExposeData", new Type[1] { typeof(object) }), null, null, new HarmonyMethod(typeof(SmallGuns), "PreserveConstructoidWeapon"));
	}

	private static IEnumerable<CodeInstruction> PreserveConstructoidWeapon(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> list = instructions.ToList();
		MethodInfo method = AccessTools.DeclaredMethod(typeof(Pawn_EquipmentTracker), "Remove", new Type[1] { typeof(ThingWithComps) });
		MethodInfo method2 = AccessTools.PropertyGetter(typeof(List<ThingWithComps>), "Item");
		FieldInfo field = AccessTools.Field(typeof(Pawn), "equipment");
		FieldInfo field2 = AccessTools.Field(typeof(ThingDefOf), "Mech_Constructoid");
		MethodInfo method3 = AccessTools.DeclaredMethod(typeof(string), "op_Equality", new Type[2]
		{
			typeof(string),
			typeof(string)
		});
		List<int> list2 = new List<int>();
		for (int i = 14; i + 9 < list.Count; i++)
		{
			if (list[i].LoadsConstant("Gun_Slugthrower") && list[i - 14].LoadsField(field2) && (!(list[i - 13].opcode != OpCodes.Bne_Un) || !(list[i - 13].opcode != OpCodes.Bne_Un_S)) && list[i + 1].Calls(method3) && (!(list[i + 2].opcode != OpCodes.Brfalse) || !(list[i + 2].opcode != OpCodes.Brfalse_S)) && LoadsLocal(list[i + 3]) && list[i + 4].LoadsField(field) && SameLocalLoad(list[i - 5], list[i + 5]) && SameLocalLoad(list[i - 4], list[i + 6]) && list[i - 3].Calls(method2) && list[i + 7].Calls(method2) && list[i + 8].Calls(method) && !(list[i + 9].opcode != OpCodes.Ret))
			{
				list2.Add(i + 8);
			}
		}
		if (list2.Count != 1)
		{
			throw new InvalidOperationException($"[SmallGuns] Expected one vanilla Constructoid/Slugthrower removal, found {list2.Count}.");
		}
		list[list2[0]].opcode = OpCodes.Call;
		list[list2[0]].operand = AccessTools.DeclaredMethod(typeof(SmallGuns), "RemoveObsoleteWeapon");
		return list;
	}

	private static bool LoadsLocal(CodeInstruction code)
	{
		if (!(code.opcode == OpCodes.Ldloc) && !(code.opcode == OpCodes.Ldloc_S) && !(code.opcode == OpCodes.Ldloc_0) && !(code.opcode == OpCodes.Ldloc_1) && !(code.opcode == OpCodes.Ldloc_2))
		{
			return code.opcode == OpCodes.Ldloc_3;
		}
		return true;
	}

	private static bool SameLocalLoad(CodeInstruction first, CodeInstruction second)
	{
		if (LoadsLocal(first) && first.opcode == second.opcode)
		{
			return object.Equals(first.operand, second.operand);
		}
		return false;
	}

	private static void RemoveObsoleteWeapon(Pawn_EquipmentTracker tracker, ThingWithComps weapon)
	{
		if (!MP.IsInMultiplayer || tracker?.pawn?.def != ThingDefOf.Mech_Constructoid || !(weapon?.def?.defName == "Gun_Slugthrower"))
		{
			tracker.Remove(weapon);
		}
	}
}
