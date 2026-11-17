using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("PeteTimesSix.SimpleSidearms")]
public class SimpleSidearmsCompat
{
	[MpCompatSyncField("SimpleSidearms.rimworld.CompSidearmMemory", "primaryWeaponMode")]
	protected static ISyncField primaryWeaponModeSyncField;

	public SimpleSidearmsCompat(ModContentPack mod)
	{
		MpCompatPatchLoader.LoadPatch(this);
		Type type = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");
		string[] array = new string[4] { "equipSpecificWeapon", "DropSidearm", "EquipSpecificWeaponFromInventoryAsOffhand", "UnequipOffhand" };
		foreach (string name in array)
		{
			MP.RegisterSyncMethod(AccessTools.Method(type, name));
		}
		type = AccessTools.TypeByName("SimpleSidearms.rimworld.CompSidearmMemory");
		array = new string[11]
		{
			"SetWeaponAsForced", "SetRangedWeaponTypeAsDefault", "SetMeleeWeaponTypeAsPreferred", "SetUnarmedAsForced", "SetUnarmedAsPreferredMelee", "UnsetForcedWeapon", "UnsetRangedWeaponDefault", "UnsetMeleeWeaponPreference", "UnsetUnarmedAsForced", "ForgetSidearmMemory",
			"InformOfAddedSidearm"
		};
		foreach (string name2 in array)
		{
			MP.RegisterSyncMethod(AccessTools.Method(type, name2));
		}
		PatchingUtilities.PatchCancelInInterface("PeteTimesSix.SimpleSidearms.Intercepts.Pawn_DraftController_Drafted_Setter_Postfix:DraftedSetter");
		PatchingUtilities.PatchCancelInInterface("PeteTimesSix.SimpleSidearms.Intercepts.ITab_Pawn_Gear_InterfaceDrop_Prefix:InterfaceDrop");
	}

	[MpCompatSyncWorker("SimpleSidearms.rimworld.ThingDefStuffDefPair")]
	private static void SyncWorkerForThingDefStuffDefPair(SyncWorker sync, ref object obj)
	{
		Traverse traverse = Traverse.Create(obj);
		Traverse traverse2 = traverse.Field("thing");
		Traverse traverse3 = traverse.Field("stuff");
		if (sync.isWriting)
		{
			sync.Write(traverse2.GetValue<ThingDef>());
			sync.Write(traverse3.GetValue<ThingDef>());
		}
		else
		{
			traverse2.SetValue(sync.Read<ThingDef>());
			traverse3.SetValue(sync.Read<ThingDef>());
		}
	}

	[MpCompatPrefix("SimpleSidearms.rimworld.Gizmo_SidearmsList", "handleInteraction", MethodType.Normal)]
	private static void HandleInteractionPrefix(ThingComp ___pawnMemory)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			primaryWeaponModeSyncField.Watch(___pawnMemory);
		}
	}

	[MpCompatPostfix("SimpleSidearms.rimworld.Gizmo_SidearmsList", "handleInteraction", MethodType.Normal)]
	private static void HandleInteractionPostfix()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	[MpCompatPrefix("PeteTimesSix.SimpleSidearms.Utilities.GettersFilters", "isManualUse", MethodType.Normal)]
	[MpCompatPrefix("PeteTimesSix.SimpleSidearms.Utilities.GettersFilters", "isDangerousWeapon", MethodType.Normal)]
	[MpCompatPrefix("PeteTimesSix.SimpleSidearms.Utilities.GettersFilters", "isEMPWeapon", MethodType.Normal)]
	[MpCompatPrefix("PeteTimesSix.SimpleSidearms.Utilities.GettersFilters", "findBestRangedWeapon", 8, MethodType.Normal)]
	private static bool PrePrimaryVerbMethodCall(ThingWithComps __0, ref bool __result)
	{
		if (!MP.InInterface)
		{
			return true;
		}
		CompEquippable comp = __0.GetComp<CompEquippable>();
		if (comp == null)
		{
			return true;
		}
		if (comp.verbTracker.verbs != null)
		{
			return true;
		}
		__result = false;
		SyncInitVerbsForComp(comp);
		return false;
	}

	[MpCompatSyncMethod(SyncContext.None)]
	private static void SyncInitVerbsForComp(CompEquippable comp)
	{
		if (comp.verbTracker.verbs == null)
		{
			comp.verbTracker.InitVerbsFromZero();
		}
	}
}
