using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("firework.supplyshelf")]
internal sealed class SupplyShelf
{
	private static Type shelfType;

	private static Type supplyType;

	private static FastInvokeHandler toggle;

	private static FastInvokeHandler setStackLimit;

	private static AccessTools.FieldRef<object, List<Thing>> links;

	private static AccessTools.FieldRef<object, ThingDef> pendingFilter;

	private static MethodInfo covers;

	private static Func<Thing, Thing, bool> coversIngredient;

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

	public SupplyShelf(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(Install);
	}

	private static void Install()
	{
		CommandAuthorityBridge.Install();
		shelfType = AccessTools.TypeByName("SupplyShelf.Building_SupplyShelf");
		supplyType = AccessTools.TypeByName("SupplyShelf.CompDirectSupply");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(supplyType, "Toggle", new Type[1] { typeof(LocalTargetInfo) });
		MethodInfo methodInfo2 = AccessTools.PropertySetter(shelfType, "MaxStacks");
		covers = AccessTools.DeclaredMethod(supplyType, "Covers", new Type[2]
		{
			typeof(Thing),
			typeof(Thing)
		});
		toggle = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		setStackLimit = HarmonyLib.MethodInvoker.GetHandler(methodInfo2);
		links = AccessTools.FieldRefAccess<object, List<Thing>>(AccessTools.Field(supplyType, "benches"));
		pendingFilter = AccessTools.FieldRefAccess<object, ThingDef>(AccessTools.Field(shelfType, "pending"));
		MP.RegisterSyncMethod(typeof(SupplyShelf), "ApplyLink").SetVersion(36);
		MP.RegisterSyncMethod(typeof(SupplyShelf), "ApplyStackLimit").SetVersion(36);
		Patch(methodInfo, "QueueLink");
		Patch(methodInfo2, "QueueStackLimit");
		Patch(AccessTools.DeclaredMethod(supplyType, "PostDrawExtraSelectionOverlays"), "CopySelectionLinks", null, "RestoreSelectionLinks");
		Patch(AccessTools.DeclaredMethod(shelfType, "CheckFilter"), "SkipLocalFilterCheck");
		Patch(AccessTools.DeclaredMethod(shelfType, "ExposeData"), null, "ExposePendingFilter");
		Patch(covers, null, "CheckSupplyOwner");
		foreach (MethodInfo item in AccessTools.GetDeclaredMethods(typeof(ThingFilter)).Where((MethodInfo method) =>
		{
			switch (method.Name)
			{
			case "SetAllow":
			case "SetAllowAll":
			case "SetDisallowAll":
			case "CopyAllowancesFrom":
			case "set_AllowedHitPointsPercents":
			case "set_AllowedQualityLevels":
				return true;
			default:
				return false;
			}
		}))
		{
			Patch(item, "CheckFilterCommand");
		}
		Patch(AccessTools.DeclaredMethod(AccessTools.Inner(AccessTools.TypeByName("SupplyShelf.CollectIngredientsToils_Patch"), "<>c__DisplayClass0_0"), "<Postfix>b__0"), null, null, null, "RevalidateDirectToil");
		coversIngredient = covers.CreateDelegate<Func<Thing, Thing, bool>>();
	}

	private static void Patch(MethodBase method, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null)
	{
		if (method == null)
		{
			throw new MissingMethodException("Supply Shelf 1.7 patch target missing");
		}
		MpCompat.harmony.Patch(method, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(SupplyShelf), name);
			}
			return null;
		}
	}

	private static bool CanEdit(Thing shelf, Map map)
	{
		if (map != null && shelf != null && shelf.Spawned && shelf.Map == map)
		{
			Faction faction = shelf.Faction;
			if (faction != null && faction.IsPlayer)
			{
				return CommandAuthorityBridge.CanActForFaction(shelf.Faction);
			}
		}
		return false;
	}

	private static bool QueueLink(ThingComp __instance, LocalTargetInfo __0)
	{
		if (!Intercept)
		{
			return true;
		}
		ThingWithComps parent = __instance.parent;
		if (CanEdit(parent, parent.Map))
		{
			Thing thing = __0.Thing;
			if (thing != null && thing.Spawned && thing.Map == parent.Map)
			{
				ApplyLink(__instance, thing, parent.Map);
			}
		}
		return false;
	}

	private static void ApplyLink(ThingComp supply, Thing bench, Map map)
	{
		if (supply != null && supplyType.IsInstanceOfType(supply) && CanEdit(supply.parent, map) && supply.parent.AllComps.Contains(supply) && bench is Building && bench.Spawned && bench is IBillGiver && !(bench is Building_WorkTableAutonomous) && bench.Map == map && bench.Faction == supply.parent.Faction && !(bool)PatchingUtilities.Get(supply.props, "autoLink"))
		{
			toggle(supply, new LocalTargetInfo(bench));
		}
	}

	private static bool QueueStackLimit(Building_Storage __instance, int __0)
	{
		if (!Intercept)
		{
			return true;
		}
		if (CanEdit(__instance, __instance.Map))
		{
			ApplyStackLimit(__instance, __0, __instance.Map);
		}
		return false;
	}

	private static void ApplyStackLimit(Building_Storage shelf, int value, Map map)
	{
		if (shelfType.IsInstanceOfType(shelf) && CanEdit(shelf, map) && value >= 1 && value <= shelf.def.building.maxItemsInCell && value != shelf.MaxItemsInCell)
		{
			setStackLimit(shelf, value);
		}
	}

	private static void CopySelectionLinks(ThingComp __instance, out List<Thing> __state)
	{
		__state = null;
		if (MP.IsInMultiplayer)
		{
			ref List<Thing> reference = ref links(__instance);
			List<Thing> list = SimplePool<List<Thing>>.Get();
			list.AddRange(reference);
			__state = reference;
			reference = list;
		}
	}

	private static Exception RestoreSelectionLinks(ThingComp __instance, List<Thing> __state, Exception __exception)
	{
		if (__state != null)
		{
			ref List<Thing> reference = ref links(__instance);
			List<Thing> list = reference;
			reference = __state;
			list.Clear();
			SimplePool<List<Thing>>.Return(list);
		}
		return __exception;
	}

	private static bool SkipLocalFilterCheck()
	{
		return !MP.IsInMultiplayer;
	}

	private static void ExposePendingFilter(Building_Storage __instance)
	{
		if (Scribe.mode != LoadSaveMode.Saving || MP.IsInMultiplayer)
		{
			Scribe_Defs.Look(ref pendingFilter(__instance), "mpCompatSupplyShelfPendingFilter");
		}
	}

	private static bool CheckFilterCommand(ThingFilter __instance)
	{
		if (!MP.IsExecutingSyncCommand || !(__instance.settingsChangedCallback?.Target is StorageSettings storageSettings))
		{
			return true;
		}
		if (storageSettings.owner is Building_Storage building_Storage && shelfType.IsInstanceOfType(building_Storage))
		{
			return CanEdit(building_Storage, building_Storage.Map);
		}
		IStoreSettingsParent owner = storageSettings.owner;
		StorageGroup group = owner as StorageGroup;
		if (group == null || !group.members.Any(shelfType.IsInstanceOfType))
		{
			return true;
		}
		return group.members.All((IStorageGroupMember member) => member is Thing shelf && CanEdit(shelf, group.Map));
	}

	private static void CheckSupplyOwner(Thing __0, Thing __1, ref bool __result)
	{
		if (!__result || !MP.IsInMultiplayer || !(__0.Position.GetSlotGroup(__0.Map)?.parent is Thing thing) || thing.Faction == __1.Faction)
		{
			return;
		}
		Faction faction = thing.Faction;
		if (faction == null || !faction.IsPlayer)
		{
			Faction faction2 = __1.Faction;
			if (faction2 == null || !faction2.IsPlayer)
			{
				return;
			}
		}
		__result = false;
	}

	private static IEnumerable<CodeInstruction> RevalidateDirectToil(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		IList<LocalVariableInfo> localVariables = __originalMethod.GetMethodBody().LocalVariables;
		if (localVariables.Count < 2 || localVariables[0].LocalType != typeof(Pawn) || localVariables[1].LocalType != typeof(Job))
		{
			throw new InvalidOperationException("Supply Shelf direct ingredient toil locals changed");
		}
		int replacements = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(covers))
			{
				yield return instruction;
				continue;
			}
			replacements++;
			yield return new CodeInstruction(OpCodes.Ldloc_0).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			yield return new CodeInstruction(OpCodes.Ldloc_1);
			yield return CodeInstruction.Call(typeof(SupplyShelf), "CanSupplyDirectly");
		}
		if (replacements != 1)
		{
			throw new InvalidOperationException("Supply Shelf direct ingredient toil changed");
		}
	}

	private static bool CanSupplyDirectly(Thing ingredient, Thing bench, Pawn actor, Job job)
	{
		if (!coversIngredient(ingredient, bench))
		{
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (actor.Map == bench.Map)
		{
			Faction faction = bench.Faction;
			if ((faction == null || !faction.IsPlayer || actor.Faction == bench.Faction) && ingredient.stackCount >= job.count && !ingredient.IsForbidden(actor) && actor.CanReserve(ingredient))
			{
				return actor.Map.reservationManager.ReservedBy(ingredient, actor, job);
			}
		}
		return false;
	}
}
