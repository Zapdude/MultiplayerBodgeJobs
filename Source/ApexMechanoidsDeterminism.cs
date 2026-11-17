using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

internal static class ApexMechanoidsDeterminism
{
	private sealed class BeamFields
	{
		internal FieldInfo shotSource;

		internal FieldInfo lastTarget;

		internal FieldInfo pathCells;

		internal FieldInfo hitCells;
	}

	internal struct VisualState
	{
		internal bool active;

		internal bool previousLocalIds;
	}

	internal struct ReloadState
	{
		internal bool active;

		internal int first;

		internal int second;
	}

	internal struct CacheState
	{
		internal bool active;

		internal IntVec3 cell;

		internal int tick;
	}

	internal struct BeamQueryState
	{
		internal bool active;

		internal Vector3 lastTarget;
	}

	private static Type unityType;

	private static Type gasType;

	private static Type purifierType;

	private static Type conditionType;

	private static FieldInfo unityOffset;

	private static FieldInfo gasDestroyTick;

	private static FieldInfo gasEffectDelay;

	private static FieldInfo purifierCell;

	private static FieldInfo purifierCacheTick;

	private static FieldInfo conditionToxicity;

	private static FieldInfo localIdsOverride;

	private static PropertyInfo inInterface;

	private static readonly Dictionary<Type, BeamFields> beamFields = new Dictionary<Type, BeamFields>();

	internal static void Install()
	{
		unityType = RequiredType("CompUnity");
		gasType = RequiredType("Gas_SmokescreenCelerus");
		purifierType = RequiredType("CompToxicPurifier");
		conditionType = RequiredType("GameCondition_ToxicPurifier");
		unityOffset = RequiredField(unityType, "tickOffset");
		gasDestroyTick = RequiredField(gasType, "destroyTick");
		gasEffectDelay = RequiredField(gasType, "effectDelay");
		purifierCell = RequiredField(purifierType, "cellToUnpolluteCached");
		purifierCacheTick = RequiredField(purifierType, "cellToUnpolluteCacheTick");
		conditionToxicity = RequiredField(conditionType, "toxicityLevel");
		Type type = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
		inInterface = ((type == null) ? null : AccessTools.Property(type, "InInterface"));
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.Patches.UniqueIdsPatch");
		localIdsOverride = ((type2 == null) ? null : AccessTools.Field(type2, "useLocalIdsOverride"));
		if (localIdsOverride == null || inInterface == null)
		{
			throw new MissingMemberException("Apex compatibility requires the current Multiplayer visual-ID/interface API");
		}
		string[] array = new string[20]
		{
			"PawnRenderNode_RandomAnimated:GetNextIndex", "ArmsAnimation:Update", "PlatformAnimation:Update", "BlinkVisualUtility:SpawnWarmupStart", "BlinkVisualUtility:SpawnWarmupPulse", "BlinkVisualUtility:SpawnDepartureBurst", "BlinkVisualUtility:SpawnArrivalBurst", "CompAbility_SpawnSirenSongNotes:CompTick", "CompAbility_ShockwaveWarmup:SpawnChargeFlecks", "Mote_ShockwaveEmitter:EmitRing",
			"Mote_PulseWaveEmitter:SpawnRingFlecks", "Mote_ShockwaveBlastWave:EmitElectricalPulseFront", "TerminusOverdriveCapeUtility:SpawnBurst", "CompToxicPurifier:ThrowToxicAirPuffUp", "Gas_SmokescreenCelerus:DoEffect", "Gas_SmokescreenCelerus:DoEffectOn", "Projectile_Orb:SpawnLightningOverlay", "Projectile_Orb:SpawnActivationVisuals", "Projectile_Orb:SpawnImpactVisuals", "Building_GazerEmplacement:UpdateBeamEffects"
		};
		for (int i = 0; i < array.Length; i++)
		{
			string[] array2 = array[i].Split(':');
			PatchVisual(RequiredMethod(RequiredType(array2[0]), array2[1]));
		}
		PatchVisual(AccessTools.DeclaredConstructor(RequiredType("ArmsAnimation"), new Type[1] { RequiredType("ArmsAnimationConfig") }) ?? throw new MissingMethodException("Apex ArmsAnimation constructor"));
		MpCompat.harmony.Patch(RequiredMethod(RequiredType("Mote_PulseWaveEmitter"), "ApplyWaveToPawn"), null, null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "PulseFleckRange"));
		MpCompat.harmony.Patch(RequiredMethod(RequiredType("Mote_ShockwaveBlastWave"), "Initialize"), new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "BeforeRandom"), null, null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "AfterRandom"));
		MpCompat.harmony.Patch(RequiredMethod(unityType, "PostSpawnSetup"), new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "BeforeUnityRespawn"), null, null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "AfterUnityRespawn"));
		MpCompat.harmony.Patch(RequiredMethod(gasType, "SpawnSetup"), new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "BeforeGasRespawn"), null, null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "AfterGasRespawn"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingComp), "PostExposeData"), null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "ExposeUnityOffset"));
		MpCompat.harmony.Patch(RequiredMethod(gasType, "ExposeData"), null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "ExposeGasDelay"));
		MpCompat.harmony.Patch(RequiredMethod(purifierType, "PostExposeData"), null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "ExposePurifierCache"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameCondition), "ExposeData"), null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "ExposeToxicity"));
		MpCompat.harmony.Patch(RequiredMethod(purifierType, "HasCellToUnpolluteCached"), new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "BeforePurifierQuery"), null, null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "AfterPurifierQuery"));
		array = new string[3] { "Verb_ShootSunBeam", "Verb_ShootSunBeamAbility", "Verb_ShootBeamFromOffset" };
		for (int i = 0; i < array.Length; i++)
		{
			Type type3 = RequiredType(array[i]);
			beamFields[type3] = new BeamFields
			{
				shotSource = RequiredField(type3, "shotSourceCell"),
				lastTarget = AccessTools.Field(type3, "LasttargetPosition"),
				pathCells = RequiredField(type3, "pathCells"),
				hitCells = RequiredField(type3, "hitCells")
			};
			PatchVisual(RequiredMethod(type3, "BurstingTick"));
			if (beamFields[type3].lastTarget != null)
			{
				MpCompat.harmony.Patch(AccessTools.DeclaredPropertyGetter(type3, "InterpolatedPosition"), new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "BeforeBeamQuery"), null, null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "AfterBeamQuery"));
			}
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Verb), "ExposeData"), null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "ExposeBeamState"));
	}

	private static Type RequiredType(string name)
	{
		return AccessTools.TypeByName("ApexMechanoids." + name) ?? throw new TypeLoadException("Missing ApexMechanoids." + name);
	}

	private static FieldInfo RequiredField(Type type, string name)
	{
		return AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
	}

	private static MethodInfo RequiredMethod(Type type, string name)
	{
		return AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
	}

	private static void PatchVisual(MethodBase method)
	{
		MpCompat.harmony.Patch(method, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "BeforeVisual"), null, null, new HarmonyMethod(typeof(ApexMechanoidsDeterminism), "AfterVisual"));
	}

	private static void BeforeVisual(out VisualState __state)
	{
		__state = default;
		if (MP.IsInMultiplayer)
		{
			__state.previousLocalIds = (bool)localIdsOverride.GetValue(null);
			Rand.PushState();
			__state.active = true;
			localIdsOverride.SetValue(null, true);
		}
	}

	private static void BeforeRandom(out bool __state)
	{
		__state = false;
		if (MP.IsInMultiplayer)
		{
			Rand.PushState();
			__state = true;
		}
	}

	private static void AfterRandom(bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
	}

	private static void AfterVisual(VisualState __state)
	{
		if (!__state.active)
		{
			return;
		}
		try
		{
			Rand.PopState();
		}
		finally
		{
			localIdsOverride.SetValue(null, __state.previousLocalIds);
		}
	}

	private static IEnumerable<CodeInstruction> PulseFleckRange(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo range = AccessTools.DeclaredMethod(typeof(Rand), "Range", new Type[2]
		{
			typeof(float),
			typeof(float)
		});
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(ApexMechanoidsDeterminism), "VisualRange");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(range))
			{
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Apex PulseWave cosmetic range contract changed: " + count);
		}
	}

	private static float VisualRange(float min, float max)
	{
		if (!MP.IsInMultiplayer)
		{
			return Rand.Range(min, max);
		}
		Rand.PushState();
		try
		{
			return Rand.Range(min, max);
		}
		finally
		{
			Rand.PopState();
		}
	}

	private static void BeforeUnityRespawn(object __instance, bool respawningAfterLoad, out ReloadState __state)
	{
		__state = default;
		if (MP.IsInMultiplayer && respawningAfterLoad)
		{
			__state.first = (int)unityOffset.GetValue(__instance);
			Rand.PushState();
			__state.active = true;
		}
	}

	private static void AfterUnityRespawn(object __instance, ReloadState __state)
	{
		if (!__state.active)
		{
			return;
		}
		try
		{
			unityOffset.SetValue(__instance, __state.first);
		}
		finally
		{
			Rand.PopState();
		}
	}

	private static void BeforeGasRespawn(object __instance, bool respawningAfterLoad, out ReloadState __state)
	{
		__state = default;
		if (MP.IsInMultiplayer && respawningAfterLoad)
		{
			__state.first = (int)gasDestroyTick.GetValue(__instance);
			__state.second = (int)gasEffectDelay.GetValue(__instance);
			Rand.PushState();
			__state.active = true;
		}
	}

	private static void AfterGasRespawn(object __instance, ReloadState __state)
	{
		if (!__state.active)
		{
			return;
		}
		try
		{
			gasDestroyTick.SetValue(__instance, __state.first);
			gasEffectDelay.SetValue(__instance, Math.Max(__state.second, 1));
		}
		finally
		{
			Rand.PopState();
		}
	}

	private static void ExposeUnityOffset(ThingComp __instance)
	{
		if (!(__instance.GetType() != unityType))
		{
			int value = (int)unityOffset.GetValue(__instance);
			Scribe_Values.Look(ref value, "mpCompatApexUnityOffset", -1);
			if (Scribe.mode == LoadSaveMode.LoadingVars && value < 0)
			{
				object value2 = AccessTools.Property(unityType, "Props").GetValue(__instance);
				int count = (int)RequiredField(value2.GetType(), "checkInterval").GetValue(value2);
				value = StableIndex(__instance.parent.thingIDNumber, count);
			}
			unityOffset.SetValue(__instance, value);
		}
	}

	private static void ExposeGasDelay(object __instance)
	{
		int value = (int)gasEffectDelay.GetValue(__instance);
		Scribe_Values.Look(ref value, "mpCompatApexEffectDelay", -1);
		if (Scribe.mode == LoadSaveMode.LoadingVars && value < 1)
		{
			object value2 = AccessTools.Property(gasType, "modExtension").GetValue(__instance);
			IntRange intRange = (IntRange)RequiredField(value2.GetType(), "effectDelay").GetValue(value2);
			int num = Math.Max(intRange.min, 1);
			int num2 = Math.Max(intRange.max, num);
			value = num + StableIndex(((Thing)__instance).thingIDNumber, num2 - num + 1);
		}
		gasEffectDelay.SetValue(__instance, value);
	}

	private static int StableIndex(int id, int count)
	{
		return (int)((uint)id % (uint)Math.Max(count, 1));
	}

	private static void ExposePurifierCache(object __instance)
	{
		IntVec3 value = (IntVec3)purifierCell.GetValue(__instance);
		int value2 = (int)purifierCacheTick.GetValue(__instance);
		Scribe_Values.Look(ref value, "mpCompatApexPurifierCell", IntVec3.Invalid);
		Scribe_Values.Look(ref value2, "mpCompatApexPurifierCacheTick", -1);
		purifierCell.SetValue(__instance, value);
		purifierCacheTick.SetValue(__instance, value2);
	}

	private static void ExposeToxicity(GameCondition __instance)
	{
		if (!(__instance.GetType() != conditionType))
		{
			float value = (float)conditionToxicity.GetValue(__instance);
			Scribe_Values.Look(ref value, "mpCompatApexToxicity", 0f);
			conditionToxicity.SetValue(__instance, value);
		}
	}

	private static void BeforePurifierQuery(object __instance, out CacheState __state)
	{
		__state = default;
		if (MP.IsInMultiplayer && (bool)inInterface.GetValue(null))
		{
			__state.active = true;
			__state.cell = (IntVec3)purifierCell.GetValue(__instance);
			__state.tick = (int)purifierCacheTick.GetValue(__instance);
		}
	}

	private static void AfterPurifierQuery(object __instance, CacheState __state)
	{
		if (__state.active)
		{
			purifierCell.SetValue(__instance, __state.cell);
			purifierCacheTick.SetValue(__instance, __state.tick);
		}
	}

	private static void BeforeBeamQuery(object __instance, out BeamQueryState __state)
	{
		__state = default;
		if (MP.IsInMultiplayer && (bool)inInterface.GetValue(null))
		{
			__state.lastTarget = (Vector3)beamFields[__instance.GetType()].lastTarget.GetValue(__instance);
			__state.active = true;
		}
	}

	private static void AfterBeamQuery(object __instance, BeamQueryState __state)
	{
		if (__state.active)
		{
			beamFields[__instance.GetType()].lastTarget.SetValue(__instance, __state.lastTarget);
		}
	}

	private static void ExposeBeamState(Verb __instance)
	{
		if (beamFields.TryGetValue(__instance.GetType(), out var value))
		{
			IntVec3 value2 = (IntVec3)value.shotSource.GetValue(__instance);
			Scribe_Values.Look(ref value2, "mpCompatApexBeamSource", IntVec3.Invalid);
			value.shotSource.SetValue(__instance, value2);
			if (value.lastTarget != null)
			{
				Vector3 value3 = (Vector3)value.lastTarget.GetValue(__instance);
				Scribe_Values.Look(ref value3, "mpCompatApexBeamLastTarget");
				value.lastTarget.SetValue(__instance, value3);
			}
			ExposeBeamCells(__instance, value.pathCells, "mpCompatApexBeamPathCells");
			ExposeBeamCells(__instance, value.hitCells, "mpCompatApexBeamHitCells");
		}
	}

	private static void ExposeBeamCells(object instance, FieldInfo field, string key)
	{
		HashSet<IntVec3> obj = field.GetValue(instance) as HashSet<IntVec3>;
		List<IntVec3> list = ((obj != null) ? (from c in obj
			orderby c.x, c.y, c.z
			select c).ToList() : null);
		Scribe_Collections.Look(ref list, key, LookMode.Value);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			field.SetValue(instance, (list == null) ? new HashSet<IntVec3>() : new HashSet<IntVec3>(list));
		}
	}
}
