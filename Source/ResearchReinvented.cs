using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("PeteTimesSix.ResearchReinvented")]
public class ResearchReinvented
{
	public ResearchReinvented(ModContentPack mod)
	{
		MethodInfo original = AccessTools.PropertyGetter("PeteTimesSix.ResearchReinvented.Managers.ResearchOpportunityManager:CurrentProjectOpportunities");
		MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(new Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>>(AddSorting)));
	}

	private static IEnumerable<CodeInstruction> AddSorting(IEnumerable<CodeInstruction> insts)
	{
		MethodInfo target = AccessTools.Method(typeof(Enumerable), "ToList");
		MethodInfo replace = AccessTools.Method(typeof(ResearchReinvented), "SortByUniqueLoadID");
		foreach (CodeInstruction inst in insts)
		{
			if (inst.operand is MethodInfo methodInfo && methodInfo == target)
			{
				inst.operand = replace;
			}
			yield return inst;
		}
	}

	private static List<ILoadReferenceable> SortByUniqueLoadID(IEnumerable<ILoadReferenceable> list)
	{
		return list.OrderBy((ILoadReferenceable a) => a.GetUniqueLoadID()).ToList();
	}
}
