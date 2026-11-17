using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("seekiworksmod.no19")]
public sealed class seekiworks_RandomRelationsBlockerandRomanceWhitelist
{
	private const string Namespace = "seekiworks_RandomRelationsBlockerandRomanceWhitelist.";

	private const string ComponentName = "CompRandomRelationsBlockerandRomanceWhitelist";

	private static Type componentType;

	private static MethodInfo tabComponent;

	private static MethodInfo whitelist;

	private static MethodInfo randomRelationSetter;

	private static FieldInfo dialogComponent;

	private static FieldInfo suppressRomance;

	private static FieldInfo initialized;

	private static bool Intercept
	{
		get
		{
			if (MP.IsInMultiplayer && MP.InInterface)
			{
				return !MP.IsExecutingSyncCommand;
			}
			return false;
		}
	}

	public seekiworks_RandomRelationsBlockerandRomanceWhitelist(ModContentPack mod)
	{
		Assembly? assembly = mod.assemblies.loadedAssemblies.Find((Assembly a) => a.GetType("seekiworks_RandomRelationsBlockerandRomanceWhitelist.CompRandomRelationsBlockerandRomanceWhitelist", throwOnError: false) != null) ?? throw new TypeLoadException("seekiworks_RandomRelationsBlockerandRomanceWhitelist.CompRandomRelationsBlockerandRomanceWhitelist");
		componentType = assembly.GetType("seekiworks_RandomRelationsBlockerandRomanceWhitelist.CompRandomRelationsBlockerandRomanceWhitelist", throwOnError: true);
		Type type = assembly.GetType("seekiworks_RandomRelationsBlockerandRomanceWhitelist.ITab_RandomRelationsBlockerandRomanceWhitelist", throwOnError: true);
		Type type2 = assembly.GetType("seekiworks_RandomRelationsBlockerandRomanceWhitelist.DiaLog_EditRomanceWhiteList", throwOnError: true);
		tabComponent = AccessTools.DeclaredPropertyGetter(type, "CompRandomRelationsBlockerandRomanceWhitelist") ?? throw new MissingMemberException(type.FullName, "CompRandomRelationsBlockerandRomanceWhitelist");
		whitelist = AccessTools.DeclaredPropertyGetter(componentType, "RomanceWhiteList") ?? throw new MissingMemberException(componentType.FullName, "RomanceWhiteList");
		randomRelationSetter = AccessTools.DeclaredPropertySetter(componentType, "SuppressRandomRelation") ?? throw new MissingMemberException(componentType.FullName, "SuppressRandomRelation");
		dialogComponent = RequiredField(type2, "compRandomRelationsBlockerandRomanceWhitelist");
		suppressRomance = RequiredField(componentType, "suppressRomance");
		initialized = RequiredField(componentType, "initialized");
		CommandAuthorityBridge.Install();
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "FillTab"), null, null, Hook("RouteTabChanges"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "DoWindowContents"), null, null, Hook("RouteWhitelistChanges"));
		MP.RegisterSyncMethod(typeof(seekiworks_RandomRelationsBlockerandRomanceWhitelist), "SetSuppression").SetVersion(35);
		MP.RegisterSyncMethod(typeof(seekiworks_RandomRelationsBlockerandRomanceWhitelist), "SetWhitelist").SetVersion(35);
	}

	private static FieldInfo RequiredField(Type type, string name)
	{
		return AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type.FullName, name);
	}

	private static MethodInfo Method(string name)
	{
		return AccessTools.DeclaredMethod(typeof(seekiworks_RandomRelationsBlockerandRomanceWhitelist), name);
	}

	private static HarmonyMethod Hook(string name)
	{
		return new HarmonyMethod(Method(name));
	}

	private static void PushWindow(CodeInstruction instruction, List<CodeInstruction> result)
	{
		CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
		codeInstruction.MoveLabelsFrom(instruction);
		codeInstruction.MoveBlocksFrom(instruction);
		result.Add(codeInstruction);
	}

	private static void Replace(CodeInstruction instruction, string name)
	{
		instruction.opcode = OpCodes.Call;
		instruction.operand = Method(name);
	}

	private static IEnumerable<CodeInstruction> RouteTabChanges(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo method = AccessTools.DeclaredMethod(typeof(Widgets), "CheckboxLabeled", new Type[8]
		{
			typeof(Rect),
			typeof(string),
			typeof(bool).MakeByRefType(),
			typeof(bool),
			typeof(Texture2D),
			typeof(Texture2D),
			typeof(bool),
			typeof(bool)
		});
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		List<CodeInstruction> list = new List<CodeInstruction>();
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(method) && ++num == 2)
			{
				PushWindow(instruction, list);
				Replace(instruction, "RomanceCheckbox");
			}
			else if (instruction.Calls(randomRelationSetter))
			{
				Replace(instruction, "SetRandomRelationFromTab");
				num2++;
			}
			else if (instruction.StoresField(initialized))
			{
				Replace(instruction, "InitializeFromUi");
				num3++;
			}
			list.Add(instruction);
		}
		if (num != 2 || num2 != 1 || num3 != 2)
		{
			throw new InvalidOperationException($"Random Relations tab changed: {num}/{num2}/{num3}");
		}
		return list;
	}

	private static IEnumerable<CodeInstruction> RouteWhitelistChanges(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo method = AccessTools.DeclaredMethod(typeof(HashSet<Pawn>), "Add", new Type[1] { typeof(Pawn) });
		MethodInfo method2 = AccessTools.DeclaredMethod(typeof(HashSet<Pawn>), "Remove", new Type[1] { typeof(Pawn) });
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		List<CodeInstruction> list = new List<CodeInstruction>();
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(method))
			{
				PushWindow(instruction, list);
				Replace(instruction, (++num == 1) ? "AddFromWindow" : "AddReciprocalFromWindow");
			}
			else if (instruction.Calls(method2))
			{
				PushWindow(instruction, list);
				Replace(instruction, (++num2 == 1) ? "RemoveFromWindow" : "RemoveReciprocalFromWindow");
			}
			else if (instruction.StoresField(initialized))
			{
				Replace(instruction, "InitializeFromUi");
				num3++;
			}
			list.Add(instruction);
		}
		if (num != 2 || num2 != 2 || num3 != 2)
		{
			throw new InvalidOperationException($"Random Relations whitelist changed: {num}/{num2}/{num3}");
		}
		return list;
	}

	private static ThingComp Component(Pawn pawn)
	{
		if (pawn?.AllComps == null)
		{
			return null;
		}
		foreach (ThingComp allComp in pawn.AllComps)
		{
			if (componentType.IsInstanceOfType(allComp))
			{
				return allComp;
			}
		}
		return null;
	}

	private static void InitializeFromUi(ThingComp comp, bool value)
	{
		if (!MP.IsInMultiplayer)
		{
			initialized.SetValue(comp, value);
		}
	}

	private static void SetRandomRelationFromTab(ThingComp comp, bool value)
	{
		if (!MP.IsInMultiplayer)
		{
			randomRelationSetter.Invoke(comp, new object[1] { value });
		}
		else if (Intercept && comp?.parent is Pawn pawn && CommandAuthorityBridge.CanActForPawn(pawn))
		{
			SetSuppression(pawn, romance: false, value);
		}
	}

	private static void RomanceCheckbox(Rect rect, string label, ref bool value, bool disabled, Texture2D on, Texture2D off, bool placeCheckboxNearText, bool paintable, ITab tab)
	{
		if (!MP.IsInMultiplayer)
		{
			Widgets.CheckboxLabeled(rect, label, ref value, disabled, on, off, placeCheckboxNearText, paintable);
			return;
		}
		Pawn pawn = (tabComponent.Invoke(tab, null) as ThingComp)?.parent as Pawn;
		bool checkOn = value;
		bool flag = Intercept && CommandAuthorityBridge.CanActForPawn(pawn);
		Widgets.CheckboxLabeled(rect, label, ref checkOn, disabled || !flag, on, off, placeCheckboxNearText, paintable);
		if (flag && checkOn != value)
		{
			SetSuppression(pawn, romance: true, checkOn);
		}
	}

	private static bool AddFromWindow(HashSet<Pawn> list, Pawn target, Window window)
	{
		return EditFromWindow(list, target, window, add: true);
	}

	private static bool RemoveFromWindow(HashSet<Pawn> list, Pawn target, Window window)
	{
		return EditFromWindow(list, target, window, add: false);
	}

	private static bool AddReciprocalFromWindow(HashSet<Pawn> list, Pawn target, Window window)
	{
		if (!MP.IsInMultiplayer)
		{
			return list.Add(target);
		}
		return false;
	}

	private static bool RemoveReciprocalFromWindow(HashSet<Pawn> list, Pawn target, Window window)
	{
		if (!MP.IsInMultiplayer)
		{
			return list.Remove(target);
		}
		return false;
	}

	private static bool EditFromWindow(HashSet<Pawn> list, Pawn target, Window window, bool add)
	{
		if (!MP.IsInMultiplayer)
		{
			return Edit(list, target, add);
		}
		if (Intercept && (dialogComponent.GetValue(window) as ThingComp)?.parent is Pawn pawn && CommandAuthorityBridge.CanActForPawn(pawn))
		{
			SetWhitelist(pawn, target, add);
		}
		return false;
	}

	private static bool Edit(HashSet<Pawn> list, Pawn pawn, bool add)
	{
		if (!add)
		{
			return list.Remove(pawn);
		}
		return list.Add(pawn);
	}

	private static HashSet<Pawn> Whitelist(ThingComp comp)
	{
		return (HashSet<Pawn>)whitelist.Invoke(comp, null);
	}

	private static void SetSuppression(Pawn pawn, bool romance, bool value)
	{
		if (!MP.IsExecutingSyncCommand || !CommandAuthorityBridge.CanActForPawn(pawn) || pawn.Dead)
		{
			return;
		}
		ThingComp thingComp = Component(pawn);
		if (thingComp != null)
		{
			if (romance)
			{
				suppressRomance.SetValue(thingComp, value);
			}
			else
			{
				randomRelationSetter.Invoke(thingComp, new object[1] { value });
			}
			initialized.SetValue(thingComp, true);
		}
	}

	private static void SetWhitelist(Pawn pawn, Pawn target, bool add)
	{
		if (!MP.IsExecutingSyncCommand || !CommandAuthorityBridge.CanActForPawn(pawn) || pawn.Dead || target == null || pawn == target)
		{
			return;
		}
		ThingComp thingComp = Component(pawn);
		if (thingComp == null || (add && (pawn.MapHeld == null || pawn.MapHeld != target.MapHeld || target.Faction != pawn.Faction || !target.IsFreeColonist || target.Dead || target.Destroyed)))
		{
			return;
		}
		HashSet<Pawn> hashSet = Whitelist(thingComp);
		if (add || target.Faction == pawn.Faction || hashSet.Contains(target))
		{
			bool flag = Edit(hashSet, target, add);
			ThingComp thingComp2 = Component(target);
			if (thingComp2 != null)
			{
				flag |= Edit(Whitelist(thingComp2), pawn, add);
			}
			if (flag)
			{
				initialized.SetValue(thingComp, true);
			}
		}
	}
}
