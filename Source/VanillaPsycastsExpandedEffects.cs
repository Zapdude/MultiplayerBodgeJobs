using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPsycastsE")]
public class VanillaPsycastsExpandedEffects
{
	private static Type timeQuakeType;

	private static Type timeSnowType;

	private static FieldInfo quakePawn;

	private static FieldInfo quakeRadius;

	private static FieldInfo snowOverlay;

	private static FieldInfo snowOverlayTemplate;

	public VanillaPsycastsExpandedEffects(ModContentPack mod)
	{
		PatchFireBeamMoteReplacement();
		timeQuakeType = RequiredType("GameCondition_TimeQuake");
		timeSnowType = RequiredType("GameCondition_TimeSnow");
		quakePawn = AccessTools.Field(timeQuakeType, "Pawn") ?? throw new MissingFieldException(timeQuakeType.FullName, "Pawn");
		quakeRadius = AccessTools.Field(timeQuakeType, "SafeRadius") ?? throw new MissingFieldException(timeQuakeType.FullName, "SafeRadius");
		snowOverlay = AccessTools.Field(timeSnowType, "worldOverlayMat") ?? throw new MissingFieldException(timeSnowType.FullName, "worldOverlayMat");
		snowOverlayTemplate = AccessTools.Field(timeSnowType, "TimeSnowOverlay") ?? throw new MissingFieldException(timeSnowType.FullName, "TimeSnowOverlay");
		MpCompat.harmony.CreateReversePatcher(AccessTools.DeclaredMethod(typeof(GameCondition), "End"), new HarmonyMethod(typeof(VanillaPsycastsExpandedEffects), "BaseConditionEnd")).Patch();
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameCondition), "ExposeData"), null, new HarmonyMethod(typeof(VanillaPsycastsExpandedEffects), "ExposeTimeQuake"));
		Patch(timeQuakeType, "GameConditionTick", "BeforeQuakeTick");
		Patch(timeQuakeType, "GameConditionDraw", "BeforeQuakeDraw");
		Patch(timeQuakeType, "End", "BeforeQuakeEnd", "NullSafeSustainerEnd");
		Patch(RequiredType("TimeSphere"), "SpawnSetup", null, "SkipRespawnDiplomacy");
		Patch(RequiredType("TimeSphere"), "Destroy", null, "NullSafeSustainerEnd");
		Patch(RequiredType("GameCondition_RaidPause"), "End", null, "NullSafeSustainerEnd");
	}

	private static Type RequiredType(string name)
	{
		return AccessTools.TypeByName("VanillaPsycastsExpanded.Chronopath." + name) ?? throw new TypeLoadException("VanillaPsycastsExpanded.Chronopath." + name);
	}

	private static void Patch(Type type, string name, string prefix = null, string transpiler = null)
	{
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name), (prefix == null) ? null : new HarmonyMethod(typeof(VanillaPsycastsExpandedEffects), prefix), null, (transpiler == null) ? null : new HarmonyMethod(typeof(VanillaPsycastsExpandedEffects), transpiler));
	}

	private static void ExposeTimeQuake(GameCondition __instance)
	{
		if (Scribe.mode == LoadSaveMode.PostLoadInit && timeSnowType.IsInstanceOfType(__instance))
		{
			snowOverlay.SetValue(__instance, snowOverlayTemplate.GetValue(null));
		}
		if (!(__instance.GetType() != timeQuakeType))
		{
			Pawn refee = (Pawn)quakePawn.GetValue(__instance);
			float value = (float)quakeRadius.GetValue(__instance);
			Scribe_References.Look(ref refee, "mpVpeTimeQuakePawn");
			Scribe_Values.Look(ref value, "mpVpeTimeQuakeSafeRadius", 0f);
			quakePawn.SetValue(__instance, refee);
			quakeRadius.SetValue(__instance, value);
		}
	}

	private static bool HasQuakePawn(GameCondition condition)
	{
		return quakePawn.GetValue(condition) is Pawn;
	}

	private static bool BeforeQuakeTick(GameCondition __instance)
	{
		if (HasQuakePawn(__instance))
		{
			return true;
		}
		BaseConditionEnd(__instance);
		return false;
	}

	private static bool BeforeQuakeDraw(GameCondition __instance)
	{
		return HasQuakePawn(__instance);
	}

	private static bool BeforeQuakeEnd(GameCondition __instance)
	{
		if (HasQuakePawn(__instance))
		{
			return true;
		}
		BaseConditionEnd(__instance);
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void BaseConditionEnd(GameCondition instance)
	{
		throw new NotSupportedException("Harmony reverse patch for GameCondition.End was not applied");
	}

	private static IEnumerable<CodeInstruction> SkipRespawnDiplomacy(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		MethodInfo baseSpawn = AccessTools.DeclaredMethod(typeof(Thing), "SpawnSetup", new Type[2]
		{
			typeof(Map),
			typeof(bool)
		});
		int calls = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction;
			if (instruction.Calls(baseSpawn))
			{
				calls++;
				Label initialSpawn = generator.DefineLabel();
				yield return new CodeInstruction(OpCodes.Ldarg_2);
				yield return new CodeInstruction(OpCodes.Brfalse, initialSpawn);
				yield return new CodeInstruction(OpCodes.Ret);
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Nop);
				codeInstruction.labels.Add(initialSpawn);
				yield return codeInstruction;
			}
		}
		if (calls != 1)
		{
			throw new InvalidOperationException("VPE TimeSphere base SpawnSetup contract changed: " + calls);
		}
	}

	private static IEnumerable<CodeInstruction> NullSafeSustainerEnd(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.DeclaredMethod(typeof(Sustainer), "End");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaPsycastsExpandedEffects), "EndSustainer");
		int calls = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				calls++;
			}
			yield return instruction;
		}
		if (calls != 1)
		{
			throw new InvalidOperationException("VPE optional-sustainer End contract changed: " + calls);
		}
	}

	private static void EndSustainer(Sustainer sustainer)
	{
		sustainer?.End();
	}

	private static void PatchFireBeamMoteReplacement()
	{
		MethodInfo original = AccessTools.DeclaredMethod("VanillaPsycastsExpanded.Conflagrator.FireBeam:StartStrike") ?? throw new MissingMethodException("VPE FireBeam.StartStrike");
		MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(VanillaPsycastsExpandedEffects), "FireBeamMoteReplacement"));
	}

	private static IEnumerable<CodeInstruction> FireBeamMoteReplacement(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo destroy = AccessTools.DeclaredMethod(typeof(Thing), "Destroy", new Type[1] { typeof(DestroyMode) });
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(VanillaPsycastsExpandedEffects), "DestroyReplacedBeamMote");
		int calls = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(destroy))
			{
				calls++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = replacement
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (calls != 1)
		{
			throw new InvalidOperationException("VPE FireBeam mote Destroy contract changed: " + calls);
		}
	}

	private static void DestroyReplacedBeamMote(Thing mote, DestroyMode mode)
	{
		if (mote != null || !MP.IsInMultiplayer)
		{
			mote.Destroy(mode);
		}
	}
}
