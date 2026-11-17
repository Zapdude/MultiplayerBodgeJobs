using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class VanillaExpandedFrameworkCustomFire
{
	private sealed class CacheState
	{
		internal readonly object Counts;

		internal readonly object Ticks;

		internal CacheState(object counts, object ticks)
		{
			Counts = counts;
			Ticks = ticks;
		}
	}

	private static readonly ConditionalWeakTable<Map, CacheState> mapCaches = new ConditionalWeakTable<Map, CacheState>();

	private static FieldInfo nativeCounts;

	private static FieldInfo nativeTicks;

	private static CacheState originalCaches;

	private static bool installed;

	internal static void Install()
	{
		if (installed)
		{
			return;
		}
		Type type = AccessTools.TypeByName("VEF.Things.CustomFireDef") ?? throw new TypeLoadException("VEF.Things.CustomFireDef");
		if ((int)AccessTools.Property(typeof(DefDatabase<>).MakeGenericType(type), "DefCount").GetValue(null) == 0)
		{
			return;
		}
		Type type2 = AccessTools.TypeByName("VEF.Things.CustomFire") ?? throw new TypeLoadException("VEF.Things.CustomFire");
		nativeCounts = AccessTools.DeclaredField(type2, "FireCount");
		nativeTicks = AccessTools.DeclaredField(type2, "LastFireCountUpdateTick");
		FieldInfo[] array = new FieldInfo[2] { nativeCounts, nativeTicks };
		foreach (FieldInfo fieldInfo in array)
		{
			if (fieldInfo == null || !fieldInfo.IsStatic || !fieldInfo.IsInitOnly || !fieldInfo.FieldType.IsGenericType || fieldInfo.FieldType.GetGenericTypeDefinition().FullName != "VEF.Things.CustomFireDefMap`1" || !fieldInfo.FieldType.GetGenericArguments().SequenceEqual(new Type[1] { typeof(int) }, null))
			{
				throw new InvalidOperationException("Unsupported VEF CustomFire cache field: " + fieldInfo);
			}
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type2, "TickInterval", new Type[1] { typeof(int) });
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type2, "SpawnSmokeParticles", Type.EmptyTypes);
		Validate(PatchProcessor.GetOriginalInstructions(methodInfo), methodInfo, 2, 2);
		Validate(PatchProcessor.GetOriginalInstructions(methodInfo2), methodInfo2, 1, 0);
		originalCaches = new CacheState(nativeCounts.GetValue(null), nativeTicks.GetValue(null));
		HarmonyMethod harmonyMethod = new HarmonyMethod(typeof(VanillaExpandedFrameworkCustomFire), "UseMapCaches");
		try
		{
			MpCompat.harmony.Patch(methodInfo, null, null, harmonyMethod);
			MpCompat.harmony.Patch(methodInfo2, null, null, harmonyMethod);
			installed = true;
		}
		catch
		{
			MpCompat.harmony.Unpatch(methodInfo, harmonyMethod.method);
			MpCompat.harmony.Unpatch(methodInfo2, harmonyMethod.method);
			throw;
		}
	}

	private static CacheState CacheFor(Thing fire)
	{
		if (MP.IsInMultiplayer)
		{
			Map map = fire.Map;
			if (map != null)
			{
				return mapCaches.GetValue(map, (Map _) => new CacheState(Activator.CreateInstance(nativeCounts.FieldType), Activator.CreateInstance(nativeTicks.FieldType)));
			}
		}
		return originalCaches;
	}

	private static void Validate(IEnumerable<CodeInstruction> instructions, MethodBase method, int counts, int ticks)
	{
		if (method == null || method.IsStatic || !(method is MethodInfo methodInfo) || methodInfo.ReturnType != typeof(void))
		{
			throw new InvalidOperationException("Unsupported VEF CustomFire method: " + method);
		}
		int num = 0;
		int num2 = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is FieldInfo fieldInfo && (!(fieldInfo != nativeCounts) || !(fieldInfo != nativeTicks)))
			{
				if (instruction.opcode != OpCodes.Ldsfld)
				{
					throw new InvalidOperationException("Unsupported VEF CustomFire cache access: " + instruction);
				}
				if (fieldInfo == nativeCounts)
				{
					num++;
				}
				else
				{
					num2++;
				}
			}
		}
		if (num != counts || num2 != ticks)
		{
			throw new InvalidOperationException($"Unsupported VEF CustomFire cache reads in {method}: {num}/{num2}");
		}
	}

	private static IEnumerable<CodeInstruction> UseMapCaches(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
	{
		List<CodeInstruction> list = instructions.ToList();
		if (__originalMethod.Name == "TickInterval")
		{
			Validate(list, __originalMethod, 2, 2);
		}
		else
		{
			if (!(__originalMethod.Name == "SpawnSmokeParticles"))
			{
				throw new InvalidOperationException("Unexpected VEF CustomFire target: " + __originalMethod);
			}
			Validate(list, __originalMethod, 1, 0);
		}
		LocalBuilder operand = generator.DeclareLocal(typeof(CacheState));
		List<CodeInstruction> list2 = new List<CodeInstruction>
		{
			new CodeInstruction(OpCodes.Ldarg_0),
			new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaExpandedFrameworkCustomFire), "CacheFor")),
			new CodeInstruction(OpCodes.Stloc, operand)
		};
		foreach (CodeInstruction item in list)
		{
			if (!(item.operand is FieldInfo fieldInfo) || (fieldInfo != nativeCounts && fieldInfo != nativeTicks))
			{
				list2.Add(item);
				continue;
			}
			CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldloc, operand);
			codeInstruction.labels.AddRange(item.labels);
			codeInstruction.blocks.AddRange(item.blocks);
			list2.Add(codeInstruction);
			list2.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.DeclaredField(typeof(CacheState), (fieldInfo == nativeCounts) ? "Counts" : "Ticks")));
			list2.Add(new CodeInstruction(OpCodes.Castclass, fieldInfo.FieldType));
		}
		return list2;
	}
}
