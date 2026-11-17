using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using VanillaGravshipExpanded;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class LandingStructure_EnemyStructure_Body
{
	public static void Impact(LandingStructure_EnemyStructure __instance)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		Map map = ((Thing)__instance).Map;
		Rand.PushState(((LandingStructureBase)__instance).randomSeed);
		try
		{
			__instance.RestoreStructureToMap(map, ((Thing)__instance).Position, __instance.structure);
		}
		finally
		{
			Rand.PopState();
		}
		CellRect structureBounds = LandingStructure_EnemyStructure.GetStructureBounds(__instance.structure);
		CellRect val = structureBounds.MovedBy(((Thing)__instance).Position);
		LandingStructure_EnemyStructure.Refog(map, val);
		((Thing)__instance).Destroy((DestroyMode)0);
	}

	public static IEnumerator CaptureGravshipCoroutine(LandingStructure_EnemyStructure __instance)
	{
		((LandingStructureBase)__instance).coroutineStarted = true;
		Map originalMap = Current.Game.CurrentMap;
		Camera mainCamera = Find.Camera;
		CameraDriver cameraDriver = ((Component)mainCamera).GetComponent<CameraDriver>();
		bool wasCamDriverEnabled = ((Behaviour)cameraDriver).enabled;
		bool wasCamEnabled = ((Behaviour)mainCamera).enabled;
		((Behaviour)cameraDriver).enabled = false;
		((Behaviour)mainCamera).enabled = false;
		Current.Game.CurrentMap = null;
		MapParent mapParent = default;
		Map tempMap = default;
		LandingStructureBase.CreateTempMap(new IntVec3(250, 1, 250), ((Thing)__instance).Map, out mapParent, out tempMap);
		Current.Game.CurrentMap = tempMap;
		yield return (object)new WaitForEndOfFrame();
		yield return (object)new WaitForEndOfFrame();
		Rand.PushState(((LandingStructureBase)__instance).randomSeed);
		__instance.RestoreStructureToMap(tempMap, tempMap.Center, __instance.structure);
		CellRect structureBounds = LandingStructure_EnemyStructure.GetStructureBounds(__instance.structure);
		CellRect val = structureBounds.MovedBy(tempMap.Center);
		foreach (IntVec3 allCell in tempMap.AllCells)
		{
			if (tempMap.terrainGrid.TerrainAt(allCell) == TerrainDefOf.Space)
			{
				tempMap.terrainGrid.SetTerrain(allCell, VGEDefOf.VGE_FakeTerrain);
			}
		}
		Building_GravEngine val2 = default;
		((LandingStructureBase)__instance).ScanGeneratedLayout(tempMap, val, out val2);
		((LandingStructureBase)__instance).enginePos = IntVec3.Invalid;
		((LandingStructureBase)__instance).landingRotation = Rot4.North;
		((LandingStructureBase)__instance).RenderAndSaveTexture(tempMap, mainCamera, val, val2);
		foreach (KeyValuePair<Thing, PositionData> item in __instance.structure.Things.OrderBy((KeyValuePair<Thing, PositionData> kvp) => kvp.Key.thingIDNumber))
		{
			if (item.Key.Spawned)
			{
				((Entity)item.Key).DeSpawn((DestroyMode)0);
			}
		}
		Rand.PopState();
		Current.Game.CurrentMap = originalMap;
		((Behaviour)mainCamera).enabled = wasCamEnabled;
		((Behaviour)cameraDriver).enabled = wasCamDriverEnabled;
		Find.WorldObjects.Remove((WorldObject)(object)mapParent);
		Find.Maps.Remove(tempMap);
		((LandingStructureBase)__instance).coroutineStarted = false;
	}

	public static void RestoreStructureToMap(LandingStructure_EnemyStructure __instance, Map map, IntVec3 center, EnemyStructure structure)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Expected Obj, but got Unknown
		//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01da: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_026c: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0274: Unknown result type (might be due to invalid IL or missing references)
		//IL_0279: Unknown result type (might be due to invalid IL or missing references)
		//IL_0281: Unknown result type (might be due to invalid IL or missing references)
		//IL_0306: Unknown result type (might be due to invalid IL or missing references)
		//IL_0309: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0313: Unknown result type (might be due to invalid IL or missing references)
		//IL_031b: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<Faction, List<Pawn>> dictionary = new Dictionary<Faction, List<Pawn>>();
		foreach (KeyValuePair<Thing, PositionData> item in structure.Things.OrderBy((KeyValuePair<Thing, PositionData> kvp) => kvp.Key.thingIDNumber))
		{
			Thing key = item.Key;
			PositionData value = item.Value;
			IntVec3 val = center + value.position;
			Rot4 rotationFromRelative = LandingStructure_EnemyStructure.GetRotationFromRelative(value.relativeRotation);
			Pawn val2 = (Pawn)(object)((key is Pawn) ? key : null);
			if (val2 != null)
			{
				GenSpawn.Spawn((Thing)(object)val2, val, map, (WipeMode)0);
				if (((Thing)val2).Faction != null && ((Thing)val2).Faction != Faction.OfPlayer)
				{
					if (!dictionary.TryGetValue(((Thing)val2).Faction, out var value2))
					{
						value2 = new List<Pawn>();
						dictionary[((Thing)val2).Faction] = value2;
					}
					value2.Add(val2);
				}
			}
			else
			{
				GenSpawn.Spawn(key, val, map, rotationFromRelative, (WipeMode)0, false, false);
			}
			Building_TurretGun val3 = (Building_TurretGun)(object)((key is Building_TurretGun) ? key : null);
			if (val3 != null)
			{
				CompWorldArtillery val4 = ThingCompUtility.TryGetComp<CompWorldArtillery>((Thing)(object)val3);
				if (val4 != null)
				{
					val4.Reset();
					val3.ResetForcedTarget();
				}
			}
		}
		foreach (KeyValuePair<Faction, List<Pawn>> item2 in dictionary)
		{
			LordMaker.MakeNewLord(item2.Key, (LordJob)new LordJob_AssaultColony(item2.Key, false, true, false, false, true, false, false), map, (IEnumerable<Pawn>)item2.Value);
		}
		foreach (KeyValuePair<IntVec3, TerrainDef> item3 in structure.Terrains.OrderBy((KeyValuePair<IntVec3, TerrainDef> kvp) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			return kvp.Key.z;
		}).ThenBy((KeyValuePair<IntVec3, TerrainDef> kvp) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			return kvp.Key.x;
		}))
		{
			IntVec3 val5 = center + item3.Key;
			map.terrainGrid.SetTerrain(val5, item3.Value);
		}
		foreach (KeyValuePair<IntVec3, TerrainDef> item4 in structure.Foundations.OrderBy((KeyValuePair<IntVec3, TerrainDef> kvp) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			return kvp.Key.z;
		}).ThenBy((KeyValuePair<IntVec3, TerrainDef> kvp) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			return kvp.Key.x;
		}))
		{
			IntVec3 val6 = center + item4.Key;
			map.terrainGrid.SetFoundation(val6, item4.Value);
		}
		foreach (KeyValuePair<IntVec3, RoofDef> item5 in structure.Roofs.OrderBy((KeyValuePair<IntVec3, RoofDef> kvp) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			return kvp.Key.z;
		}).ThenBy((KeyValuePair<IntVec3, RoofDef> kvp) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			return kvp.Key.x;
		}))
		{
			IntVec3 val7 = center + item5.Key;
			map.roofGrid.SetRoof(val7, item5.Value);
		}
	}

	public static CellRect GetStructureBounds(EnemyStructure structure)
	{
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
		int num = int.MaxValue;
		int num2 = int.MaxValue;
		int num3 = int.MinValue;
		int num4 = int.MinValue;
		foreach (KeyValuePair<Thing, PositionData> thing in structure.Things)
		{
			IntVec3 position = thing.Value.position;
			num = Math.Min(num, position.x);
			num3 = Math.Max(num3, position.x);
			num2 = Math.Min(num2, position.z);
			num4 = Math.Max(num4, position.z);
		}
		foreach (IntVec3 key in structure.Terrains.Keys)
		{
			num = Math.Min(num, key.x);
			num3 = Math.Max(num3, key.x);
			num2 = Math.Min(num2, key.z);
			num4 = Math.Max(num4, key.z);
		}
		foreach (IntVec3 key2 in structure.Foundations.Keys)
		{
			num = Math.Min(num, key2.x);
			num3 = Math.Max(num3, key2.x);
			num2 = Math.Min(num2, key2.z);
			num4 = Math.Max(num4, key2.z);
		}
		foreach (IntVec3 key3 in structure.Roofs.Keys)
		{
			num = Math.Min(num, key3.x);
			num3 = Math.Max(num3, key3.x);
			num2 = Math.Min(num2, key3.z);
			num4 = Math.Max(num4, key3.z);
		}
		return CellRect.FromLimits(num, num2, num3, num4);
	}

	public static Rot4 GetRotationFromRelative(RotationDirection relativeRotation)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Expected I4, but got Unknown
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		return (relativeRotation - 1) switch
		{
			RotationDirection.None => Rot4.East, 
			RotationDirection.Clockwise => Rot4.South, 
			RotationDirection.Opposite => Rot4.West, 
			_ => Rot4.North, 
		};
	}

	public static void Refog(Map map, CellRect cellRect)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		map.fogGrid.SetAllFogged();
		foreach (IntVec3 allCell in map.AllCells)
		{
			map.mapDrawer.MapMeshDirty(allCell, ((ulong)(MapMeshFlagDefOf.FogOfWar)));
		}
		CellRect val = cellRect.ExpandedBy(1);
		FloodFillerFog.FloodUnfog(GenCollection.RandomElement<IntVec3>(val.EdgeCells), map);
	}

	public static void ExposeData(LandingStructure_EnemyStructure __instance)
	{
		((LandingStructureBase)__instance).ExposeData();
		Scribe_Deep.Look<EnemyStructure>(ref __instance.structure, "structure", Array.Empty<object>());
	}
}
