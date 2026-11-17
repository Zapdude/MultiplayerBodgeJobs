using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class EnemyStructure_Body
{
	public static Dictionary<Thing, PositionData> get_Things(EnemyStructure __instance)
	{
		return __instance.things;
	}

	public static Dictionary<IntVec3, RoofDef> get_Roofs(EnemyStructure __instance)
	{
		return __instance.roofs;
	}

	public static Dictionary<IntVec3, TerrainDef> get_Foundations(EnemyStructure __instance)
	{
		return __instance.foundations;
	}

	public static Dictionary<IntVec3, TerrainDef> get_Terrains(EnemyStructure __instance)
	{
		return __instance.terrains;
	}

	public static Dictionary<IntVec3, ColorDef> get_TerrainColors(EnemyStructure __instance)
	{
		return __instance.terrainColors;
	}

	public static EnemyStructure CaptureFrom(Map map, Thing engine)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected Obj, but got Unknown
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0182: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0205: Invalid comparison between Unknown and I4
		//IL_0211: Unknown result type (might be due to invalid IL or missing references)
		//IL_0217: Invalid comparison between Unknown and I4
		//IL_023c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0241: Unknown result type (might be due to invalid IL or missing references)
		//IL_0243: Unknown result type (might be due to invalid IL or missing references)
		//IL_0245: Unknown result type (might be due to invalid IL or missing references)
		//IL_0256: Unknown result type (might be due to invalid IL or missing references)
		//IL_0258: Unknown result type (might be due to invalid IL or missing references)
		//IL_0252: Unknown result type (might be due to invalid IL or missing references)
		//IL_0229: Unknown result type (might be due to invalid IL or missing references)
		//IL_022f: Invalid comparison between Unknown and I4
		//IL_0269: Unknown result type (might be due to invalid IL or missing references)
		//IL_026b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0265: Unknown result type (might be due to invalid IL or missing references)
		//IL_027f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0281: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0290: Unknown result type (might be due to invalid IL or missing references)
		//IL_0293: Unknown result type (might be due to invalid IL or missing references)
		//IL_0298: Unknown result type (might be due to invalid IL or missing references)
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		//IL_029e: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b0: Expected Obj, but got Unknown
		//IL_027d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0278: Unknown result type (might be due to invalid IL or missing references)
		EnemyStructure val = new EnemyStructure();
		IntVec3 position = engine.Position;
		HashSet<IntVec3> hashSet = new HashSet<IntVec3>();
		Queue<IntVec3> queue = new Queue<IntVec3>();
		queue.Enqueue(position);
		hashSet.Add(position);
		while (queue.Count > 0)
		{
			IntVec3 val2 = queue.Dequeue();
			for (int i = 0; i < 8; i++)
			{
				IntVec3 val3 = val2 + GenAdj.AdjacentCells[i];
				if (GenGrid.InBounds(val3, map) && !hashSet.Contains(val3) && EnemyStructure.IsGravshipCell(map, val3))
				{
					hashSet.Add(val3);
					queue.Enqueue(val3);
				}
			}
		}
		List<Thing> list = new List<Thing>();
		foreach (IntVec3 item in hashSet.OrderBy((IntVec3 c) =>
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			return c.z;
		}).ThenBy((IntVec3 c) =>
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			return c.x;
		}))
		{
			IntVec3 key = item - position;
			TerrainDef val4 = map.terrainGrid.TerrainAt(item);
			if (val4 != TerrainDefOf.Space)
			{
				val.terrains[key] = val4;
			}
			TerrainDef val5 = map.terrainGrid.FoundationAt(item);
			if (val5 != null)
			{
				val.foundations[key] = val5;
			}
			RoofDef val6 = map.roofGrid.RoofAt(item);
			if (val6 != null)
			{
				val.roofs[key] = val6;
			}
			ColorDef val7 = map.terrainGrid.ColorAt(item);
			if (val7 != null)
			{
				val.terrainColors[key] = val7;
			}
			foreach (Thing item2 in (from t in GridsUtility.GetThingList(item, map)
				orderby t.thingIDNumber
				select t).ToList())
			{
				if (!list.Contains(item2) && !item2.Destroyed && !(item2 is Skyfaller) && (int)item2.def.category != 10 && ((int)item2.def.category == 3 || item2 is Pawn || (int)item2.def.category == 2))
				{
					list.Add(item2);
					Rot4 rotation = item2.Rotation;
					RotationDirection val8;
					if (rotation == Rot4.East)
					{
						val8 = (RotationDirection)1;
					}
					else if (rotation == Rot4.South)
					{
						val8 = (RotationDirection)2;
					}
					else
					{
						val8 = ((!(rotation == Rot4.West)) ? ((RotationDirection)0) : ((RotationDirection)3));
					}
					RotationDirection relativeRotation = val8;
					val.things[item2] = new PositionData
					{
						position = item2.Position - position,
						relativeRotation = relativeRotation
					};
				}
			}
		}
		foreach (Thing item3 in list.OrderBy((Thing t) => t.thingIDNumber))
		{
			if (item3.Spawned)
			{
				((Entity)item3).DeSpawn((DestroyMode)0);
			}
		}
		return val;
	}

	public static bool IsGravshipCell(Map map, IntVec3 c)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		TerrainDef val = map.terrainGrid.TerrainAt(c);
		if (val == InternalDefOf.VGE_EnemySubstructure || val == InternalDefOf.VGE_EnemySubarmor)
		{
			return true;
		}
		TerrainDef val2 = map.terrainGrid.FoundationAt(c);
		if (val2 != InternalDefOf.VGE_EnemySubstructure)
		{
			return val2 == InternalDefOf.VGE_EnemySubarmor;
		}
		return true;
	}

	public static void ExposeData(EnemyStructure __instance)
	{
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Invalid comparison between Unknown and I4
		Scribe_Collections.Look<Thing, PositionData>(ref __instance.things, "things", (LookMode)2, (LookMode)2);
		Scribe_Collections.Look<IntVec3, RoofDef>(ref __instance.roofs, "roofs", (LookMode)1, (LookMode)4);
		Scribe_Collections.Look<IntVec3, TerrainDef>(ref __instance.foundations, "foundations", (LookMode)1, (LookMode)4);
		Scribe_Collections.Look<IntVec3, TerrainDef>(ref __instance.terrains, "terrains", (LookMode)1, (LookMode)4);
		Scribe_Collections.Look<IntVec3, ColorDef>(ref __instance.terrainColors, "terrainColors", (LookMode)1, (LookMode)4);
		if ((int)Scribe.mode == 4)
		{
			if (__instance.things == null)
			{
				__instance.things = new Dictionary<Thing, PositionData>();
			}
			if (__instance.roofs == null)
			{
				__instance.roofs = new Dictionary<IntVec3, RoofDef>();
			}
			if (__instance.foundations == null)
			{
				__instance.foundations = new Dictionary<IntVec3, TerrainDef>();
			}
			if (__instance.terrains == null)
			{
				__instance.terrains = new Dictionary<IntVec3, TerrainDef>();
			}
			if (__instance.terrainColors == null)
			{
				__instance.terrainColors = new Dictionary<IntVec3, ColorDef>();
			}
		}
	}
}
