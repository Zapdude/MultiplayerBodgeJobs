using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Poupun.IsekaiCreaturesAddon")]
public class IsekaiCreaturesCompat
{
	private static Type petGizmosPatchType;

	private static Type sealProviderType;

	private static Type tameProviderType;

	private static Func<Pawn, bool> isControllableBeast;

	private static Func<Pawn, bool> isSealable;

	private static Func<Pawn, Pawn, bool> outranks;

	private static Func<Pawn, Thing> findEmptyCube;

	private static Func<Pawn, bool> isTameable;

	private static Func<Pawn, bool> requiresLordOfTheWild;

	private static Func<Pawn, bool> hasLordOfTheWild;

	private static FieldInfo pendingEvolutions;

	private static FieldInfo queuedEvolutionIds;

	private static FieldInfo evolutionPawn;

	private static FieldInfo evolutionFromDef;

	private static FieldInfo evolutionToDef;

	private static MethodInfo dequeueEvolution;

	private static MethodInfo enqueueEvolution;

	private static MethodInfo evolve;

	private static Type mobRankComponent;

	private static Type creatureAggro;

	private static FieldInfo nextIdleCallTick;

	public IsekaiCreaturesCompat(ModContentPack mod)
	{
		IsekaiCreaturesFirefighting.Install();
		petGizmosPatchType = AccessTools.TypeByName("IsekaiCreatures.Taming.Pawn_GetGizmos_Pet_Patch");
		sealProviderType = AccessTools.TypeByName("IsekaiCreatures.Sealing.FloatMenuOptionProvider_SealCreature");
		tameProviderType = AccessTools.TypeByName("IsekaiCreatures.Taming.FloatMenuOptionProvider_TameBeast");
		Type type = AccessTools.TypeByName("IsekaiCreatures.Taming.IsekaiTaming");
		Type type2 = AccessTools.TypeByName("IsekaiCreatures.Sealing.SealingUtil");
		isControllableBeast = BindPredicate(type, "IsControllableBeast");
		isTameable = BindPredicate(type, "IsTameable");
		requiresLordOfTheWild = BindPredicate(type, "RequiresLordOfTheWild");
		hasLordOfTheWild = BindPredicate(type, "HasLordOfTheWild");
		isSealable = BindPredicate(type2, "IsSealable");
		outranks = AccessTools.MethodDelegate<Func<Pawn, Pawn, bool>>(AccessTools.DeclaredMethod(type2, "Outranks"));
		findEmptyCube = AccessTools.MethodDelegate<Func<Pawn, Thing>>(AccessTools.DeclaredMethod(type2, "FindEmptyCube"));
		if (petGizmosPatchType != null)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(petGizmosPatchType, "WithDraftToggle");
			if (methodInfo != null)
			{
				MpCompat.harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(IsekaiCreaturesCompat), "WithDraftTogglePostfix"));
				MP.RegisterSyncMethod(typeof(IsekaiCreaturesCompat), "SyncedSetPetDrafted");
			}
		}
		if (sealProviderType != null)
		{
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(sealProviderType, "GetOptionsFor");
			if (methodInfo2 != null)
			{
				MpCompat.harmony.Patch(methodInfo2, null, new HarmonyMethod(typeof(IsekaiCreaturesCompat), "SealOptionsPostfix"));
				MP.RegisterSyncMethod(typeof(IsekaiCreaturesCompat), "SyncedSetSealDesignation");
			}
		}
		if (tameProviderType != null)
		{
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod(tameProviderType, "GetOptionsFor");
			if (methodInfo3 != null)
			{
				MpCompat.harmony.Patch(methodInfo3, null, new HarmonyMethod(typeof(IsekaiCreaturesCompat), "TameOptionsPostfix"));
				MP.RegisterSyncMethod(typeof(IsekaiCreaturesCompat), "SyncedSetTameDesignation");
			}
		}
		SetupEvolutionTiming();
		IsekaiCreaturesAsyncTime.Install();
		creatureAggro = AccessTools.TypeByName("IsekaiCreatures.CompCreatureAggro");
		nextIdleCallTick = AccessTools.DeclaredField(creatureAggro, "_nextIdleCallTick");
		if (nextIdleCallTick != null)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingComp), "PostExposeData"), null, new HarmonyMethod(typeof(IsekaiCreaturesCompat), "CreatureAggroExposePostfix"));
		}
		Type type3 = AccessTools.TypeByName("IsekaiCreatures.Vfx.IsekaiVfxRenderer");
		if (type3 != null)
		{
			PatchingUtilities.SetupAsyncTime();
			string[] array = new string[2] { "AddPuff", "MapComponentUpdate" };
			foreach (string name in array)
			{
				MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type3, name), new HarmonyMethod(typeof(IsekaiCreaturesCompat), "RendererTimePrefix"), null, null, new HarmonyMethod(typeof(IsekaiCreaturesCompat), "RendererTimeFinalizer"));
			}
		}
	}

	private static Func<Pawn, bool> BindPredicate(Type type, string name)
	{
		return AccessTools.MethodDelegate<Func<Pawn, bool>>(AccessTools.DeclaredMethod(type, name));
	}

	private static void WithDraftTogglePostfix(ref IEnumerable<Gizmo> __result, Pawn pawn)
	{
		if (MP.IsInMultiplayer && pawn != null && __result != null)
		{
			__result = WrapPetGizmos(__result, pawn);
		}
	}

	private static IEnumerable<Gizmo> WrapPetGizmos(IEnumerable<Gizmo> gizmos, Pawn pawn)
	{
		foreach (Gizmo gizmo in gizmos)
		{
			if (gizmo is Command_Toggle command_Toggle && command_Toggle.hotKey == KeyBindingDefOf.Command_ColonistDraft)
			{
				bool drafted = !pawn.Drafted;
				command_Toggle.toggleAction = () =>
				{
					SyncedSetPetDrafted(pawn, drafted);
				};
			}
			yield return gizmo;
		}
	}

	private static void SyncedSetPetDrafted(Pawn pawn, bool drafted)
	{
		if (pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.Downed && isControllableBeast(pawn))
		{
			pawn.drafter.Drafted = drafted;
		}
	}

	private static void SealOptionsPostfix(ref IEnumerable<FloatMenuOption> __result, Pawn clickedPawn, FloatMenuContext context)
	{
		if (MP.IsInMultiplayer && clickedPawn != null && __result != null)
		{
			__result = WrapSealOptions(__result, clickedPawn, context.FirstSelectedPawn);
		}
	}

	private static IEnumerable<FloatMenuOption> WrapSealOptions(IEnumerable<FloatMenuOption> options, Pawn creature, Pawn colonist)
	{
		bool designated = IsDesignated(creature, "IsekaiSealCreature");
		foreach (FloatMenuOption option in options)
		{
			if (option.action != null)
			{
				option.action = () =>
				{
					SyncedSetSealDesignation(creature, colonist, !designated);
				};
			}
			yield return option;
		}
	}

	private static void SyncedSetSealDesignation(Pawn creature, Pawn colonist, bool designated)
	{
		if (CanIssueDesignation(colonist, creature) && (!designated || (isSealable(creature) && outranks(colonist, creature) && findEmptyCube(colonist) != null)))
		{
			SetDesignation(creature, "IsekaiSealCreature", designated);
		}
	}

	private static void TameOptionsPostfix(ref IEnumerable<FloatMenuOption> __result, Thing clickedThing, FloatMenuContext context)
	{
		if (MP.IsInMultiplayer && clickedThing is Pawn beast && __result != null)
		{
			__result = WrapTameOptions(__result, beast, context.FirstSelectedPawn);
		}
	}

	private static IEnumerable<FloatMenuOption> WrapTameOptions(IEnumerable<FloatMenuOption> options, Pawn beast, Pawn colonist)
	{
		bool designated = IsDesignated(beast, "IsekaiTameBeast");
		foreach (FloatMenuOption option in options)
		{
			if (option.action != null)
			{
				option.action = () =>
				{
					SyncedSetTameDesignation(beast, colonist, !designated);
				};
			}
			yield return option;
		}
	}

	private static void SyncedSetTameDesignation(Pawn beast, Pawn colonist, bool designated)
	{
		if (CanIssueDesignation(colonist, beast) && (!designated || (beast.Downed && isTameable(beast) && (!requiresLordOfTheWild(beast) || hasLordOfTheWild(colonist)))))
		{
			SetDesignation(beast, "IsekaiTameBeast", designated);
		}
	}

	private static bool CanIssueDesignation(Pawn colonist, Pawn creature)
	{
		if (colonist != null && creature != null && !colonist.Destroyed && !creature.Destroyed && !colonist.Dead && !creature.Dead && colonist.Spawned && creature.Spawned && colonist.IsColonistPlayerControlled && !colonist.Downed && !colonist.InMentalState)
		{
			return colonist.Map == creature.Map;
		}
		return false;
	}

	private static bool IsDesignated(Pawn creature, string defName)
	{
		DesignationDef namedSilentFail = DefDatabase<DesignationDef>.GetNamedSilentFail(defName);
		if (namedSilentFail != null)
		{
			return creature.Map?.designationManager?.DesignationOn(creature, namedSilentFail) != null;
		}
		return false;
	}

	private static void SetDesignation(Pawn creature, string defName, bool designated)
	{
		DesignationDef namedSilentFail = DefDatabase<DesignationDef>.GetNamedSilentFail(defName);
		DesignationManager designationManager = creature.Map?.designationManager;
		if (namedSilentFail != null && designationManager != null)
		{
			Designation designation = designationManager.DesignationOn(creature, namedSilentFail);
			if (designated && designation == null)
			{
				designationManager.AddDesignation(new Designation(creature, namedSilentFail));
			}
			else if (!designated && designation != null)
			{
				designationManager.RemoveDesignation(designation);
			}
		}
	}

	private static void CreatureAggroExposePostfix(ThingComp __instance)
	{
		if (!(__instance.GetType() != creatureAggro))
		{
			int value = (int)nextIdleCallTick.GetValue(__instance);
			Scribe_Values.Look(ref value, "mpIsekaiNextIdleCallTick", -1);
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				nextIdleCallTick.SetValue(__instance, value);
			}
		}
	}

	private static void SetupEvolutionTiming()
	{
		Type type = AccessTools.TypeByName("IsekaiCreatures.Evolution.IsekaiCreatureEvolution");
		pendingEvolutions = AccessTools.DeclaredField(type, "pending");
		if (!(pendingEvolutions == null))
		{
			Type type2 = pendingEvolutions.FieldType.GetGenericArguments()[0];
			queuedEvolutionIds = AccessTools.DeclaredField(type, "queuedIds");
			evolutionPawn = AccessTools.DeclaredField(type2, "pawn");
			evolutionFromDef = AccessTools.DeclaredField(type2, "fromDefName");
			evolutionToDef = AccessTools.DeclaredField(type2, "toDefName");
			dequeueEvolution = AccessTools.DeclaredMethod(pendingEvolutions.FieldType, "Dequeue");
			enqueueEvolution = AccessTools.DeclaredMethod(pendingEvolutions.FieldType, "Enqueue");
			evolve = AccessTools.DeclaredMethod(type, "Evolve");
			mobRankComponent = AccessTools.TypeByName("IsekaiLeveling.MobRanking.MobRankComponent");
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DrainPendingEvolutions"), new HarmonyMethod(typeof(IsekaiCreaturesCompat), "DrainEvolutionsPrefix"));
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Map), "MapPostTick"), null, new HarmonyMethod(typeof(IsekaiCreaturesCompat), "MapPostTickPostfix"));
		}
	}

	private static bool DrainEvolutionsPrefix()
	{
		return !MP.IsInMultiplayer;
	}

	private static void MapPostTickPostfix(Map __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		object value = pendingEvolutions.GetValue(null);
		int count = ((ICollection)value).Count;
		if (count == 0)
		{
			return;
		}
		HashSet<int> hashSet = (HashSet<int>)queuedEvolutionIds.GetValue(null);
		for (int i = 0; i < count; i++)
		{
			object obj = dequeueEvolution.Invoke(value, null);
			Pawn pawn = (Pawn)evolutionPawn.GetValue(obj);
			if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Destroyed)
			{
				if (pawn != null)
				{
					hashSet.Remove(pawn.thingIDNumber);
				}
				continue;
			}
			if (pawn.Map != __instance)
			{
				enqueueEvolution.Invoke(value, new object[1] { obj });
				continue;
			}
			hashSet.Remove(pawn.thingIDNumber);
			if (pawn.def?.defName != (string)evolutionFromDef.GetValue(obj))
			{
				continue;
			}
			try
			{
				ThingComp thingComp = pawn.AllComps.Find((ThingComp comp) => mobRankComponent.IsInstanceOfType(comp));
				evolve.Invoke(null, new object[3]
				{
					pawn,
					(string)evolutionToDef.GetValue(obj),
					thingComp
				});
			}
			catch (TargetInvocationException ex)
			{
				Log.Warning($"[IsekaiMP] Creature evolution failed: {ex.InnerException ?? ex}");
			}
		}
	}

	private static void RendererTimePrefix(MapComponent __instance, out PatchingUtilities.TimeSnapshot? __state)
	{
		__state = ((MP.IsInMultiplayer && __instance.map != null) ? PatchingUtilities.TimeSnapshot.GetAndSetFromMap(__instance.map) : ((PatchingUtilities.TimeSnapshot?)null));
	}

	private static void RendererTimeFinalizer(PatchingUtilities.TimeSnapshot? __state)
	{
		__state?.Set();
	}
}
