using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VExplorationE")]
internal class VanillaLandmarksExpanded
{
	private delegate void CheckpointIntLook(ref int value, string label);

	private static Func<bool> checkpointActive;

	private static CheckpointIntLook checkpointLook;

	private static Type colossalType;

	private static FieldInfo colossalCounter;

	public VanillaLandmarksExpanded(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand("VanillaExplorationExpanded.DeadlifeSprayer:ThrowAirPuffUp");
		PatchingUtilities.PatchPushPopRand("VanillaExplorationExpanded.ToxicSprayer:ThrowAirPuffUp");
		PatchingUtilities.PatchPushPopRand("VanillaExplorationExpanded.RotstinkSprayer:ThrowAirPuffUp");
		PatchingUtilities.PatchPushPopRand("VanillaExplorationExpanded.SmokeSprayer:ThrowAirPuffUp");
		Type type = AccessTools.TypeByName("VanillaExplorationExpanded.Building_RotstinkVent");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "Tick"), null, null, new HarmonyMethod(typeof(VanillaLandmarksExpanded), "GuardRotstinkWatchdog"));
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.Compatibility.CheckpointState");
		if (type2 != null)
		{
			if (!object.Equals(AccessTools.Field(type2, "Version")?.GetRawConstantValue(), 1))
			{
				throw new InvalidOperationException("Unsupported exact checkpoint field API");
			}
			checkpointActive = AccessTools.MethodDelegate<Func<bool>>(AccessTools.PropertyGetter(type2, "Active"));
			checkpointLook = AccessTools.MethodDelegate<CheckpointIntLook>(AccessTools.DeclaredMethod(type2, "Look", new Type[2]
			{
				typeof(int).MakeByRefType(),
				typeof(string)
			}));
			colossalType = AccessTools.TypeByName("VanillaExplorationExpanded.GameCondition_ColossalFauna");
			colossalCounter = AccessTools.Field(colossalType, "tickCounter");
			if (colossalType == null || !typeof(GameCondition).IsAssignableFrom(colossalType) || colossalCounter == null || colossalCounter.IsStatic || colossalCounter.FieldType != typeof(int))
			{
				throw new InvalidOperationException("VLE colossal fauna refresh counter changed");
			}
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameCondition), "ExposeData"), null, new HarmonyMethod(typeof(VanillaLandmarksExpanded), "ExposeColossalCheckpoint"));
		}
	}

	private static void ExposeColossalCheckpoint(GameCondition __instance)
	{
		if (!(__instance.GetType() != colossalType) && checkpointActive())
		{
			int value = (int)colossalCounter.GetValue(__instance);
			checkpointLook(ref value, "mpLiveVleColossalTickCounter");
			colossalCounter.SetValue(__instance, value);
		}
	}

	private static IEnumerable<CodeInstruction> GuardRotstinkWatchdog(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
	{
		List<CodeInstruction> list = instructions.ToList();
		MethodInfo clock = AccessTools.PropertyGetter(typeof(Find), "TickManager");
		FieldInfo fieldInfo = AccessTools.Field(__originalMethod.DeclaringType, "spraySustainer");
		(CodeInstruction, int)[] array = (from p in list.Select((CodeInstruction code, int index) => (code: code, index: index))
			where p.code.Calls(clock)
			select p).ToArray();
		CodeInstruction[] array2 = list.Where((CodeInstruction code) => code.opcode == OpCodes.Ret).ToArray();
		if (fieldInfo == null || fieldInfo.IsStatic || array.Length != 1 || array2.Length != 1 || GenCollection.Count(list, (CodeInstruction code) => code.opcode == OpCodes.Ldstr && object.Equals(code.operand, "Rotstink spray sustainer still playing after 1000 ticks. Force-ending.")) != 1)
		{
			throw new InvalidOperationException("VLE rotstink vent watchdog IL changed");
		}
		Label label = generator.DefineLabel();
		array2[0].labels.Add(label);
		CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(array[0].Item1).MoveBlocksFrom(array[0].Item1);
		list.InsertRange(array[0].Item2, new CodeInstruction[3]
		{
			codeInstruction,
			new CodeInstruction(OpCodes.Ldfld, fieldInfo),
			new CodeInstruction(OpCodes.Brfalse, label)
		});
		return list;
	}
}
