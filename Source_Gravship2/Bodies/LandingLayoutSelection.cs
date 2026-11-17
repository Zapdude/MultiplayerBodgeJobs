using System;
using System.Collections.Generic;
using KCSG;
using RimWorld;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class LandingLayoutSelection
{
	internal static void Split(List<Def> selected, out List<PrefabDef> prefabs, out List<KCSG.StructureLayoutDef> layouts, out List<int> order)
	{
		prefabs = new List<PrefabDef>();
		layouts = new List<KCSG.StructureLayoutDef>();
		order = new List<int>();
		if (selected == null)
		{
			return;
		}
		foreach (Def item in selected)
		{
			PrefabDef val = (PrefabDef)(object)((item is PrefabDef) ? item : null);
			if (val == null)
			{
				KCSG.StructureLayoutDef val2 = (KCSG.StructureLayoutDef)(object)((item is KCSG.StructureLayoutDef) ? item : null);
				if (val2 == null)
				{
					if (item != null)
					{
						throw new InvalidOperationException("Unsupported landing layout: " + ((object)item).GetType());
					}
					order.Add(-1);
				}
				else
				{
					order.Add(layouts.Count * 2 + 1);
					layouts.Add(val2);
				}
			}
			else
			{
				order.Add(prefabs.Count * 2);
				prefabs.Add(val);
			}
		}
	}

	internal static List<Def> Restore(List<PrefabDef> prefabs, List<KCSG.StructureLayoutDef> layouts, List<int> order)
	{
		List<Def> list = new List<Def>();
		if (order == null)
		{
			if (prefabs != null)
			{
				list.AddRange((IEnumerable<Def>)prefabs);
			}
			if (layouts != null)
			{
				list.AddRange((IEnumerable<Def>)layouts);
			}
			return list;
		}
		foreach (int item in order)
		{
			if (item == -1)
			{
				list.Add(null);
				continue;
			}
			int num = item / 2;
			bool num2;
			if (item >= 0)
			{
				if ((item & 1) != 0)
				{
					if (layouts != null)
					{
						num2 = num >= layouts.Count;
						goto IL_0070;
					}
				}
				else if (prefabs != null)
				{
					num2 = num >= prefabs.Count;
					goto IL_0070;
				}
			}
			goto IL_0072;
			IL_0072:
			throw new InvalidOperationException("Invalid saved landing layout position: " + item);
			IL_0070:
			if (!num2)
			{
				list.Add((Def)(((item & 1) == 0) ? ((object)prefabs[num]) : ((object)layouts[num])));
				continue;
			}
			goto IL_0072;
		}
		return list;
	}

	internal static void Expose(ref List<Def> selected)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Invalid comparison between Unknown and I4
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Invalid comparison between Unknown and I4
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Invalid comparison between Unknown and I4
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Invalid comparison between Unknown and I4
		if ((int)Scribe.mode == 1 || (int)Scribe.mode == 2)
		{
			List<PrefabDef> prefabs = null;
			List<KCSG.StructureLayoutDef> layouts = null;
			List<int> order = null;
			if ((int)Scribe.mode == 1)
			{
				Split(selected, out prefabs, out layouts, out order);
			}
			Scribe_Collections.Look<PrefabDef>(ref prefabs, "selectedDefs", (LookMode)4, Array.Empty<object>());
			Scribe_Collections.Look<KCSG.StructureLayoutDef>(ref layouts, "selectedDefsKcsg", (LookMode)4, Array.Empty<object>());
			Scribe_Collections.Look<int>(ref order, "selectedDefsOrder", (LookMode)1, Array.Empty<object>());
			if ((int)Scribe.mode == 2)
			{
				selected = Restore(prefabs, layouts, order);
			}
		}
	}
}
