using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPsycastsE")]
public class VanillaPsycastsExpandedWounds
{
	private static Type woundWithShader;

	private static FieldInfo shaderField;

	private static MethodInfo shaderGetter;

	public VanillaPsycastsExpandedWounds(ModContentPack mod)
	{
		woundWithShader = AccessTools.TypeByName("VanillaPsycastsExpanded.WoundWithShader");
		if (woundWithShader == null)
		{
			return;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(woundWithShader, "Resolve_Transpiler", new Type[2]
		{
			typeof(IEnumerable<CodeInstruction>),
			typeof(ILGenerator)
		});
		shaderField = AccessTools.DeclaredField(woundWithShader, "shader");
		shaderGetter = AccessTools.PropertyGetter(typeof(ShaderTypeDef), "Shader");
		if (!(methodInfo == null) && !(methodInfo.ReturnType != typeof(IEnumerable<CodeInstruction>)) && !(shaderField == null) && !(shaderGetter == null))
		{
			MethodInfo repair = AccessTools.DeclaredMethod(typeof(VanillaPsycastsExpandedWounds), "BalanceShaderBranch");
			Patches patchInfo = Harmony.GetPatchInfo(methodInfo);
			if (patchInfo == null || !patchInfo.Postfixes.Any((Patch p) => p.PatchMethod == repair))
			{
				MpCompat.harmony.Patch(methodInfo, null, new HarmonyMethod(repair)
				{
					priority = 0
				});
			}
		}
	}

	private static IEnumerable<CodeInstruction> BalanceShaderBranch(IEnumerable<CodeInstruction> __result, ILGenerator __1)
	{
		List<CodeInstruction> list = __result.ToList();
		for (int i = 1; i + 7 < list.Count; i++)
		{
			if (IsBrokenBranch(list, i))
			{
				Label label = __1.DefineLabel();
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Nop);
				codeInstruction.labels.Add(label);
				list.Insert(i + 7, new CodeInstruction(OpCodes.Br, label));
				list.Insert(i + 9, codeInstruction);
				i += 9;
			}
		}
		return list;
	}

	private static bool IsBrokenBranch(List<CodeInstruction> code, int i)
	{
		if (code[i - 1].opcode != OpCodes.Stloc_0 || code[i].opcode != OpCodes.Ldarg_0 || code[i + 1].opcode != OpCodes.Isinst || !object.Equals(code[i + 1].operand, woundWithShader) || code[i + 2].opcode != OpCodes.Dup || (code[i + 3].opcode != OpCodes.Brfalse && code[i + 3].opcode != OpCodes.Brfalse_S) || !(code[i + 3].operand is Label item) || code[i + 4].opcode != OpCodes.Ldfld || !object.Equals(code[i + 4].operand, shaderField) || !code[i + 5].Calls(shaderGetter) || code[i + 6].opcode != OpCodes.Stloc_0 || code[i + 7].opcode != OpCodes.Pop || !code[i + 7].labels.Contains(item))
		{
			return false;
		}
		for (int j = i; j <= i + 7; j++)
		{
			if (code[j].blocks.Count != 0)
			{
				return false;
			}
		}
		return true;
	}
}
