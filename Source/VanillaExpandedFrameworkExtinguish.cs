using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

internal static class VanillaExpandedFrameworkExtinguish
{
	private static bool installed;

	internal static void Install()
	{
		if (!installed)
		{
			MethodInfo original = AccessTools.DeclaredMethod(AccessTools.TypeByName("VEF.Things.VanillaExpandedFramework_DamageWorker_Extinguish_Apply_Patch") ?? throw new TypeLoadException("VEF extinguish patch"), "Postfix", new Type[2]
			{
				typeof(DamageInfo),
				typeof(Thing)
			});
			MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(VanillaExpandedFrameworkExtinguish), "NullAttachmentsHaveZeroCount"));
			installed = true;
		}
	}

	private static int AttachmentCount(List<AttachableThing> attachments)
	{
		return attachments?.Count ?? 0;
	}

	private static IEnumerable<CodeInstruction> NullAttachmentsHaveZeroCount(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo count = AccessTools.PropertyGetter(typeof(List<AttachableThing>), "Count");
		int reads = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(count))
			{
				yield return instruction;
				continue;
			}
			reads++;
			yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaExpandedFrameworkExtinguish), "AttachmentCount")).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
		}
		if (reads != 1)
		{
			throw new InvalidOperationException("VEF extinguish attachment count reads changed: " + reads);
		}
	}
}
