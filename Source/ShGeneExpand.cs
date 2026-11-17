using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("shirohane.gene.expand")]
public sealed class ShGeneExpand
{
	public ShGeneExpand(ModContentPack mod)
	{
		Type type = mod.assemblies.loadedAssemblies.Select((Assembly assembly) => assembly.GetType("SHGE.Gene_PartTwitch")).FirstOrDefault((Type type2) => type2 != null);
		if (!(type == null))
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "Tick"), null, null, new HarmonyMethod(typeof(ShGeneExpand), "IsolateTwitchRandomness"));
		}
	}

	private static IEnumerable<CodeInstruction> IsolateTwitchRandomness(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo chance = AccessTools.DeclaredMethod(typeof(Rand), "Chance", new Type[1] { typeof(float) });
		MethodInfo twitch = AccessTools.DeclaredMethod(typeof(ShGeneExpand), "TwitchChance");
		int sites = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(chance))
			{
				yield return instruction;
				continue;
			}
			sites++;
			yield return new CodeInstruction(instruction)
			{
				opcode = OpCodes.Ldarg_0,
				operand = null
			};
			yield return new CodeInstruction(OpCodes.Call, twitch);
		}
		if (sites != 1)
		{
			throw new InvalidOperationException($"Gene Expand animation random call sites changed: {sites}");
		}
	}

	private static bool TwitchChance(float chance, Gene gene)
	{
		if (!MP.IsInMultiplayer)
		{
			return Rand.Chance(chance);
		}
		int specialSeed = Gen.HashCombineInt(gene.pawn.thingIDNumber, Find.TickManager.TicksGame);
		return Rand.ChanceSeeded(chance, specialSeed);
	}
}
