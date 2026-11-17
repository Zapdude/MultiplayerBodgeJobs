using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

internal static class ApexMechanoidsGameplay
{
	private static Type pulseType;

	private static Type blastType;

	private static Type chargerType;

	private static FieldInfo downedCredits;

	private static bool installed;

	internal static void Install()
	{
		if (!installed)
		{
			pulseType = AccessTools.TypeByName("ApexMechanoids.Mote_PulseWaveEmitter") ?? throw new TypeLoadException("Apex pulse wave emitter");
			blastType = AccessTools.TypeByName("ApexMechanoids.Mote_ShockwaveBlastWave") ?? throw new TypeLoadException("Apex shockwave blast wave");
			chargerType = AccessTools.TypeByName("ApexMechanoids.CompMechWirelessCharger") ?? throw new TypeLoadException("Apex wireless charger");
			downedCredits = Field(AccessTools.TypeByName("ApexMechanoids.HediffComp_VampiricMechanites") ?? throw new TypeLoadException("Apex vampiric mechanites"), "downedCredited");
			Patch(typeof(ListerThings), "EverListable", null, "ListGameplayMotes");
			Patch(typeof(Thing), "ExposeData", null, "ExposeGameplayMote");
			Patch(typeof(Mote), "SpawnSetup", "BeforeMoteSpawn", "AfterMoteSpawn");
			Patch(typeof(ThingComp), "PostExposeData", null, "ExposeWirelessWaste");
			Patch(typeof(Pawn), "ExposeData", null, "ExposeDownedCredit");
			LongEventHandler.ExecuteWhenFinished(EnableGameplayMoteSaving);
			installed = true;
		}
	}

	private static void Patch(Type type, string name, string prefix, string postfix)
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name), (prefix == null) ? null : new HarmonyMethod(typeof(ApexMechanoidsGameplay), prefix), (postfix == null) ? null : new HarmonyMethod(typeof(ApexMechanoidsGameplay), postfix));
	}

	private static bool IsGameplayMote(Type type)
	{
		if (!(type == pulseType))
		{
			return type == blastType;
		}
		return true;
	}

	internal static void EnableGameplayMoteSaving()
	{
		foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (IsGameplayMote(item.thingClass))
			{
				item.isSaveable = true;
			}
		}
	}

	private static void ListGameplayMotes(ThingDef def, ListerThingsUse use, ref bool __result)
	{
		if (use != ListerThingsUse.Region && IsGameplayMote(def.thingClass))
		{
			__result = true;
		}
	}

	private static void BeforeMoteSpawn(Mote __instance, bool respawningAfterLoad, out int? __state)
	{
		__state = ((respawningAfterLoad && IsGameplayMote(__instance.GetType())) ? new int?(__instance.spawnTick) : ((int?)null));
	}

	private static void AfterMoteSpawn(Mote __instance, int? __state)
	{
		if (__state.HasValue)
		{
			__instance.spawnTick = __state.Value;
		}
	}

	private static void ExposeWirelessWaste(ThingComp __instance)
	{
		if (__instance.GetType() == chargerType)
		{
			Value<float>(__instance, "wasteProduced");
		}
	}

	private static void ExposeDownedCredit(Pawn __instance)
	{
		ConditionalWeakTable<Pawn, object> conditionalWeakTable = (ConditionalWeakTable<Pawn, object>)downedCredits.GetValue(null);
		bool value = conditionalWeakTable.TryGetValue(__instance, out var _);
		Scribe_Values.Look(ref value, "mpApexDownedCredited", defaultValue: false);
		if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			conditionalWeakTable.Remove(__instance);
			if (value)
			{
				conditionalWeakTable.Add(__instance, new object());
			}
		}
	}

	private static void ExposeGameplayMote(Thing __instance)
	{
		if (!IsGameplayMote(__instance.GetType()))
		{
			return;
		}
		Mote mote = (Mote)__instance;
		Scribe_Values.Look(ref mote.exactPosition, "mpApexExactPosition", Vector3.zero);
		Scribe_Values.Look(ref mote.exactRotation, "mpApexExactRotation", 0f);
		Scribe_Values.Look(ref mote.spawnTick, "mpApexSpawnTick", 0);
		Scribe_Values.Look(ref mote.pausedTicks, "mpApexPausedTicks", 0);
		if (__instance.GetType() != pulseType)
		{
			return;
		}
		Reference<Pawn>(mote, "caster");
		Definition<HediffDef>(mote, "blindHediffDef");
		Value<float>(mote, "radius");
		Value<int>(mote, "ringIntervalTicks");
		Value<int>(mote, "stunTicks");
		Value<int>(mote, "blindTicks");
		Value<float>(mote, "visualScale");
		Value<string>(mote, "fleckDefName");
		Value<int>(mote, "ticksUntilNextRing");
		Value<int>(mote, "currentRing");
		Value<int>(mote, "maxRing");
		HashSet<int> hashSet = (HashSet<int>)Field(pulseType, "affectedPawnIds").GetValue(mote);
		List<int> list = hashSet.OrderBy((int id) => id).ToList();
		Scribe_Collections.Look(ref list, "mpApexAffectedPawnIds", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			hashSet.Clear();
			if (list != null)
			{
				hashSet.UnionWith(list);
			}
		}
	}

	private static FieldInfo Field(Type type, string name)
	{
		return AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
	}

	private static void Value<T>(object owner, string name)
	{
		FieldInfo fieldInfo = Field(owner.GetType(), name);
		T value = (T)fieldInfo.GetValue(owner);
		Scribe_Values.Look(ref value, "mpApex_" + name);
		fieldInfo.SetValue(owner, value);
	}

	private static void Reference<T>(object owner, string name) where T : class, ILoadReferenceable
	{
		FieldInfo fieldInfo = Field(owner.GetType(), name);
		T refee = (T)fieldInfo.GetValue(owner);
		Scribe_References.Look(ref refee, "mpApex_" + name);
		fieldInfo.SetValue(owner, refee);
	}

	private static void Definition<T>(object owner, string name) where T : Def, new()
	{
		FieldInfo fieldInfo = Field(owner.GetType(), name);
		T value = (T)fieldInfo.GetValue(owner);
		Scribe_Defs.Look(ref value, "mpApex_" + name);
		fieldInfo.SetValue(owner, value);
	}
}
