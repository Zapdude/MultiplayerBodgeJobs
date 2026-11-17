using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sarg.alphagenes")]
internal class AlphaGenes
{
	private sealed class ToggleState
	{
		internal Type AbilityType;

		internal Type HediffType;

		internal FieldInfo Ability;

		internal AccessTools.FieldRef<object, bool> Active;
	}

	private static AccessTools.FieldRef<HashSet<Pawn>> pawnsToRiseField;

	private static readonly Dictionary<Type, ToggleState> toggles = new Dictionary<Type, ToggleState>();

	private static readonly ConditionalWeakTable<Ability, object> legacyToggles = new ConditionalWeakTable<Ability, object>();

	private static readonly ConditionalWeakTable<Ability, object> savedMpToggles = new ConditionalWeakTable<Ability, object>();

	public AlphaGenes(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompat.RegisterLambdaMethod("AlphaGenes.Ability_MineralOverdrive", "GetGizmos", 0, 2);
		MpCompat.RegisterLambdaMethod("AlphaGenes.Ability_ReactiveArmour", "GetGizmos", 0, 2);
		if (AccessTools.DeclaredMethod("AlphaGenes.CompScorpionCounter:CompGetGizmosExtra") != null)
		{
			MpCompat.RegisterLambdaDelegate("AlphaGenes.CompScorpionCounter", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		}
		ISyncMethod[] array = MpCompat.RegisterLambdaMethod("AlphaGenes.HediffComp_RandomMutation", "CompGetGizmos", default(int));
		for (int i = 0; i < array.Length; i++)
		{
			array[i].SetDebugOnly().SetVersion(16);
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("AlphaGenes.Gene_Randomizer:PostAdd"), new HarmonyMethod(typeof(AlphaGenes), "PrePostAddRandomizerGene"));
	}

	private static void LatePatch()
	{
		pawnsToRiseField = AccessTools.StaticFieldRefAccess<HashSet<Pawn>>(AccessTools.DeclaredField("AlphaGenes.StaticCollectionsClass:pawnsToRise"));
		MpCompat.harmony.Patch(AccessTools.Constructor(AccessTools.TypeByName("AlphaGenes.GameComponent_RaisePawns"), new Type[1] { typeof(Game) }), new HarmonyMethod(new Action(ClearCache)));
		string[] array = new string[2] { "MineralOverdrive", "ReactiveArmour" };
		foreach (string text in array)
		{
			Type type = AccessTools.TypeByName("AlphaGenes.Ability_" + text);
			Type type2 = AccessTools.TypeByName("AlphaGenes.Hediff_" + text);
			ToggleState value = new ToggleState
			{
				AbilityType = type,
				HediffType = type2,
				Ability = AccessTools.DeclaredField(type2, "ability"),
				Active = AccessTools.FieldRefAccess<bool>(type, "active")
			};
			toggles.Add(type, value);
			toggles.Add(type2, value);
			MpCompat.harmony.Patch(AccessTools.DeclaredPropertyGetter(type2, "ShouldRemove"), new HarmonyMethod(typeof(AlphaGenes), "BindToggleAbility"));
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Ability), "ExposeData"), null, new HarmonyMethod(typeof(AlphaGenes), "ExposeToggle"));
		AlphaGenesSettings.Install();
		AlphaGenesAsyncTime.Install();
		AlphaGenesTools.Install();
	}

	private static void ExposeToggle(Ability __instance)
	{
		if (!toggles.TryGetValue(__instance.GetType(), out var state))
		{
			return;
		}
		object value;
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			savedMpToggles.Remove(__instance);
			legacyToggles.Remove(__instance);
			bool flag = Scribe.loader.curXmlParent?["mpAlphaGenesToggleActive"] != null;
			if (!MP.IsInMultiplayer && !flag)
			{
				return;
			}
			savedMpToggles.Add(__instance, new object());
			if (!flag)
			{
				legacyToggles.Add(__instance, new object());
			}
		}
		else if (MP.IsInMultiplayer)
		{
			savedMpToggles.GetValue(__instance, (Ability _) => new object());
		}
		else if (!savedMpToggles.TryGetValue(__instance, out value))
		{
			return;
		}
		Scribe_Values.Look(ref state.Active(__instance), "mpAlphaGenesToggleActive", defaultValue: false, forceSave: true);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && legacyToggles.Remove(__instance))
		{
			ref bool reference = ref state.Active(__instance);
			Pawn pawn = __instance.pawn;
			reference = pawn != null && pawn.health?.hediffSet?.hediffs.Any((Hediff h) => h.GetType() == state.HediffType) == true;
		}
	}

	private static bool BindToggleAbility(Hediff __instance, ref bool __result)
	{
		if (!toggles.TryGetValue(__instance.GetType(), out var state) || state.Ability.GetValue(__instance) != null)
		{
			return true;
		}
		Ability ability = __instance.pawn?.abilities?.abilities.FirstOrDefault((Ability a) => a.GetType() == state.AbilityType);
		if (!MP.IsInMultiplayer && (ability == null || !savedMpToggles.TryGetValue(ability, out var _)))
		{
			return true;
		}
		if (ability != null)
		{
			state.Ability.SetValue(__instance, ability);
			return true;
		}
		__result = true;
		return false;
	}

	private static bool PrePostAddRandomizerGene(Gene __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Pawn pawn = __instance.pawn;
		pawn.genes.SetXenotype(XenotypeDefOf.Baseliner);
		pawn.genes.RemoveGene(__instance);
		return false;
	}

	private static void ClearCache()
	{
		pawnsToRiseField().Clear();
	}
}
