using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using VanillaGravshipExpanded;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class LandingStructure_EnemyGravshipRaid_Body
{
	public static void OnImpact(LandingStructure_EnemyGravshipRaid __instance, Map map, CellRect cellRect, HashSet<Thing> preExisting)
	{
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Expected Obj, but got Unknown
		if (__instance.spawnedByScenario)
		{
			CellRect.Enumerator enumerator = cellRect.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					IntVec3 current = enumerator.Current;
					if (GenGrid.InBounds(current, map) && GridsUtility.GetTerrain(current, map) != TerrainDefOf.Space)
					{
						map.fogGrid.Unfog(current);
					}
				}
			}
			finally
			{
				((IDisposable)enumerator/*cast due to constrained. prefix*/).Dispose();
			}
		}
		IntVec3 centerCell = cellRect.CenterCell;
		foreach (KeyValuePair<Pawn, IntVec3> item in ((LandingStructure_StructureSet)__instance).pawnPositions.OrderBy((KeyValuePair<Pawn, IntVec3> pair) => ((Thing)pair.Key).thingIDNumber))
		{
			Pawn key = item.Key;
			LandingStructure_EnemyGravshipRaid.EnsureAstrorigEquipped(key);
			GenSpawn.Spawn((Thing)(object)key, centerCell + item.Value, map, (WipeMode)0);
		}
		if (((LandingStructure_StructureSet)__instance).pawnPositions.Count > 0)
		{
			LordMaker.MakeNewLord(((LandingStructure_StructureSet)__instance).shipFaction, (LordJob)new LordJob_AssaultColony(((LandingStructure_StructureSet)__instance).shipFaction, false, false, false, false, false, false, false), map, (IEnumerable<Pawn>)((LandingStructure_StructureSet)__instance).pawnPositions.Keys.OrderBy((Pawn p) => ((Thing)p).thingIDNumber).ToList());
		}
	}

	public static void EnsureAstrorigEquipped(Pawn pawn)
	{
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Expected Obj, but got Unknown
		foreach (Apparel item in pawn.apparel.WornApparel.ToList())
		{
			if (!ApparelUtility.CanWearTogether(InternalDefOf.VGE_Apparel_Astrorig, ((Thing)item).def, pawn.RaceProps.body))
			{
				pawn.apparel.Remove(item);
				((Thing)item).Destroy((DestroyMode)0);
			}
		}
		Apparel val = (Apparel)ThingMaker.MakeThing(InternalDefOf.VGE_Apparel_Astrorig, (ThingDef)null);
		PawnGenerator.PostProcessGeneratedGear((Thing)(object)val, pawn);
		pawn.apparel.Wear(val, false, false);
		CompApparelOxygenProvider comp = ((ThingWithComps)val).GetComp<CompApparelOxygenProvider>();
		if (comp != null)
		{
			comp.RemainingChargesExact = comp.MaxCharges;
		}
	}
}
