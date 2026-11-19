using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

/// <summary>
/// Supply Shelf by Firework. Linking a bench and changing the stack limit run through synced
/// commands, and a supply comp may not feed another faction's bench.
/// </summary>
/// <remarks>
/// <c>Covers</c> also runs when a bill reserves ingredients, so the postfix is transpiled to
/// require the pawn to hold the reservation. The <c>freeze</c> setting is host-authoritative and
/// carried in a per-game snapshot.
/// </remarks>
[MpCompatFor("firework.supplyshelf")]
internal sealed class SupplyShelf
{
	private sealed class Snapshot : IExposable
	{
		internal bool Freeze;

		public void ExposeData()
		{
			Scribe_Values.Look(ref Freeze, "freeze", false, true);
		}
	}

	private sealed class SettingsEdit
	{
		internal Snapshot Before;

		internal Game Game;

		internal bool Enabled;

		internal Color ContentColor;
	}

	private const string SettingsKey = "firework.supplyshelf.settings";

	private static Type shelfType;

	private static Type supplyType;

	private static FastInvokeHandler toggle;

	private static FastInvokeHandler setStackLimit;

	private static AccessTools.FieldRef<object, List<Thing>> links;

	private static AccessTools.FieldRef<object, ThingDef> pendingFilter;

	private static MethodInfo covers;

	private static Func<Thing, Thing, bool> coversIngredient;

	private static FieldInfo settingsHolder;

	private static FieldInfo freezeField;

	private static Action applySettings;

	internal static bool Installed { get; private set; }

	private static object SettingsObject => settingsHolder.GetValue(null);

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
		shelfType = ModType("Building_SupplyShelf");
		supplyType = ModType("CompDirectSupply");
		Type modType = ModType("SupplyShelfMod");
		Type billPatchType = ModType("DoBillShelfIngredients_Patch");

		MethodInfo toggleMethod = Method(supplyType, "Toggle", typeof(LocalTargetInfo));
		MethodInfo stackLimitSetter = PropertySetter(shelfType, "MaxStacks");
		covers = Method(supplyType, "Covers", typeof(Thing), typeof(Thing));
		toggle = HarmonyLib.MethodInvoker.GetHandler(toggleMethod);
		setStackLimit = HarmonyLib.MethodInvoker.GetHandler(stackLimitSetter);
		links = AccessTools.FieldRefAccess<object, List<Thing>>(Field(supplyType, "benches"));
		pendingFilter = AccessTools.FieldRefAccess<object, ThingDef>(Field(shelfType, "pending"));
		coversIngredient = covers.CreateDelegate<Func<Thing, Thing, bool>>();

		settingsHolder = Field(modType, "Settings");
		freezeField = Field(settingsHolder.FieldType, "freeze");
		applySettings = (Action)Method(modType, "Apply", Type.EmptyTypes).CreateDelegate(typeof(Action));

		MP.RegisterSyncMethod(typeof(SupplyShelf), nameof(ApplyLink)).SetVersion(36);
		MP.RegisterSyncMethod(typeof(SupplyShelf), nameof(ApplyStackLimit)).SetVersion(36);
		MP.RegisterSyncMethod(typeof(SupplyShelf), nameof(SyncFreeze)).SetHostOnly().SetVersion(37);

		Patch(toggleMethod, prefix: nameof(QueueLink));
		Patch(stackLimitSetter, prefix: nameof(QueueStackLimit));
		Patch(Method(supplyType, "PostDrawExtraSelectionOverlays"), prefix: nameof(CopySelectionLinks), finalizer: nameof(RestoreSelectionLinks));
		Patch(Method(shelfType, "CheckFilter"), prefix: nameof(SkipLocalFilterCheck));
		Patch(Method(shelfType, "ExposeData"), postfix: nameof(ExposePendingFilter));
		Patch(covers, postfix: nameof(CheckSupplyOwner));
		Patch(Method(billPatchType, "Postfix", typeof(JobDriver_DoBill), typeof(bool)), transpiler: nameof(RevalidateDirectIngredients));
		Patch(Method(modType, "DoSettingsWindowContents"), prefix: nameof(BeforeSettings), finalizer: nameof(AfterSettings));
		foreach (MethodInfo mutator in AccessTools.GetDeclaredMethods(typeof(ThingFilter)).Where(IsFilterMutator))
		{
			Patch(mutator, prefix: nameof(CheckFilterCommand));
		}

		SessionServices.Install();
		SessionServices.RegisterSettings(SettingsKey, typeof(Snapshot), CaptureSettings, () => new Snapshot(), value => ApplySettings((Snapshot)value), value => value is Snapshot, () => (ModSettings)SettingsObject, "mpSupplyShelfSettings");
		Installed = true;
	}

	private static bool IsFilterMutator(MethodInfo method)
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
	}

	private static Type ModType(string name)
	{
		return AccessTools.TypeByName("SupplyShelf." + name) ?? throw new TypeLoadException("Supply Shelf: missing type SupplyShelf." + name);
	}

	private static MethodInfo Method(Type type, string name, params Type[] arguments)
	{
		return AccessTools.DeclaredMethod(type, name, arguments) ?? throw new MissingMethodException(type?.FullName ?? "Supply Shelf", name);
	}

	private static MethodInfo PropertySetter(Type type, string name)
	{
		return AccessTools.PropertySetter(type, name) ?? throw new MissingMethodException(type?.FullName ?? "Supply Shelf", "set_" + name);
	}

	private static FieldInfo Field(Type type, string name)
	{
		return AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type?.FullName ?? "Supply Shelf", name);
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
				return CommandAuthorityBridge.CanActForFaction(faction);
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
		Thing shelf = __instance.parent;
		if (CanEdit(shelf, shelf.Map))
		{
			Thing bench = __0.Thing;
			if (bench != null && bench.Spawned && bench.Map == shelf.Map)
			{
				ApplyLink(__instance, bench, shelf.Map);
			}
		}
		return false;
	}

	private static void ApplyLink(ThingComp supply, Thing bench, Map map)
	{
		if (supply == null || !supplyType.IsInstanceOfType(supply) || !CanEdit(supply.parent, map) || !supply.parent.AllComps.Contains(supply))
		{
			return;
		}
		if (!(bench is Building) || !bench.Spawned || bench.Map != map || bench.Faction != supply.parent.Faction)
		{
			return;
		}
		if (!(bench is IBillGiver) || bench is Building_WorkTableAutonomous)
		{
			return;
		}
		if (PatchingUtilities.Get(supply.props, "autoLink") is bool autoLink && autoLink)
		{
			return;
		}
		toggle(supply, new LocalTargetInfo(bench));
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
			List<Thing> copy = SimplePool<List<Thing>>.Get();
			copy.AddRange(reference);
			__state = reference;
			reference = copy;
		}
	}

	private static Exception RestoreSelectionLinks(ThingComp __instance, List<Thing> __state, Exception __exception)
	{
		if (__state != null)
		{
			ref List<Thing> reference = ref links(__instance);
			List<Thing> copy = reference;
			reference = __state;
			copy.Clear();
			SimplePool<List<Thing>>.Return(copy);
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
		if (!MP.IsExecutingSyncCommand || __instance.settingsChangedCallback?.Target is not StorageSettings storageSettings)
		{
			return true;
		}
		if (storageSettings.owner is Building_Storage shelf && shelfType.IsInstanceOfType(shelf))
		{
			return CanEdit(shelf, shelf.Map);
		}
		if (storageSettings.owner is not StorageGroup group || !group.members.Any(shelfType.IsInstanceOfType))
		{
			return true;
		}
		return group.members.All(member => member is Thing thing && CanEdit(thing, group.Map));
	}

	private static void CheckSupplyOwner(Thing __0, Thing __1, ref bool __result)
	{
		if (!__result || !MP.IsInMultiplayer)
		{
			return;
		}
		if (__0.Position.GetSlotGroup(__0.Map)?.parent is not Thing shelf || shelf.Faction == __1.Faction)
		{
			return;
		}
		Faction shelfFaction = shelf.Faction;
		Faction benchFaction = __1.Faction;
		if ((shelfFaction != null && shelfFaction.IsPlayer) || (benchFaction != null && benchFaction.IsPlayer))
		{
			__result = false;
		}
	}

	private static IEnumerable<CodeInstruction> RevalidateDirectIngredients(IEnumerable<CodeInstruction> instructions)
	{
		int replaced = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(covers))
			{
				yield return instruction;
				continue;
			}
			replaced++;
			yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
			yield return CodeInstruction.Call(typeof(SupplyShelf), nameof(CanSupplyDirectly));
		}
		if (replaced != 1)
		{
			throw new InvalidOperationException($"Supply Shelf reserved ingredient check moved: Covers calls={replaced}");
		}
	}

	private static bool CanSupplyDirectly(Thing ingredient, Thing bench, JobDriver_DoBill driver)
	{
		if (!coversIngredient(ingredient, bench))
		{
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = driver.pawn;
		Job job = driver.job;
		if (pawn?.Map == null || pawn.Map != bench.Map)
		{
			return false;
		}
		Faction owner = bench.Faction;
		if (owner != null && owner.IsPlayer && pawn.Faction != owner)
		{
			return false;
		}
		return !ingredient.IsForbidden(pawn) && pawn.CanReserve(ingredient) && pawn.Map.reservationManager.ReservedBy(ingredient, pawn, job);
	}

	private static IExposable CaptureSettings()
	{
		return new Snapshot
		{
			Freeze = (bool)freezeField.GetValue(SettingsObject)
		};
	}

	private static void ApplySettings(Snapshot selected)
	{
		freezeField.SetValue(SettingsObject, selected.Freeze);
		applySettings();
	}

	internal static IExposable SettingsState(Game game)
	{
		return SessionServices.SettingsForGame(SettingsKey, game);
	}

	private static void BeforeSettings(out SettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.EnsureSettings(SettingsState(Current.Game));
			__state = new SettingsEdit
			{
				Before = (Snapshot)CaptureSettings(),
				Game = Current.Game,
				Enabled = GUI.enabled,
				ContentColor = GUI.contentColor
			};
			GUI.enabled &= MP.IsHosting;
		}
	}

	private static Exception AfterSettings(Exception __exception, SettingsEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		try
		{
			if (!MP.IsInMultiplayer || Current.Game != __state.Game)
			{
				return __exception;
			}
			// The window has already written the field locally, so restore the old value and let the
			// command apply it; re-applying keeps the defs consistent until it runs.
			bool selected = (bool)freezeField.GetValue(SettingsObject);
			if (selected != __state.Before.Freeze)
			{
				ApplySettings(__state.Before);
				if (__exception == null && MP.IsHosting && MP.InInterface)
				{
					SyncFreeze(selected);
				}
			}
			return __exception;
		}
		finally
		{
			GUI.enabled = __state.Enabled;
			GUI.contentColor = __state.ContentColor;
		}
	}

	private static void SyncFreeze(bool selected)
	{
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.ApplySettings(SettingsState(Current.Game), new Snapshot
			{
				Freeze = selected
			});
		}
	}
}
