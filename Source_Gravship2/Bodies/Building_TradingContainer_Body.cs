using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class Building_TradingContainer_Body
{
	public static void Destroy(Building_TradingContainer __instance, DestroyMode mode)
	{
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected Obj, but got Unknown
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d1: Unknown result type (might be due to invalid IL or missing references)
		IEnumerable<TraderKindDef> enumerable = DefDatabase<TraderKindDef>.AllDefs.Where((TraderKindDef x) => x.orbital);
		TraderKindDef val = ((enumerable != null) ? GenCollection.RandomElementByWeight<TraderKindDef>(enumerable, (Func<TraderKindDef, float>)((TraderKindDef x) => x.CalculatedCommonality)) : null);
		if (val != null)
		{
			TradeShip val2 = new TradeShip(val, (Faction)null);
			ThingSetMakerParams val3 = new ThingSetMakerParams
			{
				traderDef = val2.TraderKind,
				makingFaction = ((PassingShip)val2).Faction,
				tile = ((Thing)__instance).Map.Tile,
				minSingleItemMarketValuePct = 0.15f
			};
			float num = 4000f;
			List<Thing> list = ThingSetMakerDefOf.TraderStock.root.Generate(val3).ToList();
			list.RemoveAll((Thing t) => t is Pawn || t is MinifiedThing || t.def == ThingDefOf.Gravcore);
			GenList.Shuffle<Thing>((IList<Thing>)list);
			List<Thing> list2 = new List<Thing>();
			float num2 = 0f;
			int num3 = 6;
			foreach (Thing item2 in list)
			{
				if (list2.Count >= num3)
				{
					break;
				}
				float num4 = item2.MarketValue * (float)item2.stackCount;
				if (!(num2 + num4 > num * 1.15f))
				{
					list2.Add(item2);
					num2 += num4;
					if (num2 >= num)
					{
						break;
					}
				}
			}
			if (list2.Count == 0 && list.Count > 0)
			{
				Thing item = list.MinBy((Thing t) => t.MarketValue * (float)t.stackCount);
				list2.Add(item);
			}
			foreach (Thing item3 in list2)
			{
				GenPlace.TryPlaceThing(item3, ((Thing)__instance).Position, ((Thing)__instance).Map, (ThingPlaceMode)1, (Action<Thing, int>)null, (Predicate<IntVec3>)null, (Rot4?)null, 1);
			}
		}
		((Building)__instance).Destroy(mode);
	}
}
