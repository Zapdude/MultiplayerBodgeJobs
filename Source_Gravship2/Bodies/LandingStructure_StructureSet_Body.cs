using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PipeSystem;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using VEF.Storyteller;
using VanillaGravshipExpanded;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI.Group;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class LandingStructure_StructureSet_Body
{
	public static IEnumerator CaptureGravshipCoroutine(LandingStructure_StructureSet __instance)
	{
		((LandingStructureBase)__instance).coroutineStarted = true;
		MapParent mapParent = default;
		Map tempMap = default;
		LandingStructureBase.CreateTempMap(new IntVec3(250, 1, 250), ((Thing)__instance).Map, out mapParent, out tempMap);
		Map originalMap = Current.Game.CurrentMap;
		Camera mainCamera = Find.Camera;
		CameraDriver cameraDriver = ((Component)mainCamera).GetComponent<CameraDriver>();
		bool wasCamDriverEnabled = ((Behaviour)cameraDriver).enabled;
		bool wasCamEnabled = ((Behaviour)mainCamera).enabled;
		((Behaviour)cameraDriver).enabled = false;
		((Behaviour)mainCamera).enabled = false;
		Current.Game.CurrentMap = tempMap;
		yield return (object)new WaitForEndOfFrame();
		yield return (object)new WaitForEndOfFrame();
		try
		{
			Rand.PushState(((LandingStructureBase)__instance).randomSeed);
			List<(StructurePatternOffset, Def)> list = StructureSetGenerator.SelectStandardLayouts(__instance.structureSetDef, 0f, __instance.selectedDefs);
			HashSet<Thing> hashSet = new HashSet<Thing>(tempMap.listerThings.AllThings);
			List<CellRect> source = StructureSetGenerator.Generate(tempMap, __instance.structureSetDef, __instance.shipFaction, tempMap.Center, list, 0f, __instance.shipRotation);
			HashSet<IntVec3> vacuumCells = tempMap.AllCells.Where((IntVec3 c) =>
			{
				//IL_000b: Unknown result type (might be due to invalid IL or missing references)
				return tempMap.terrainGrid.TerrainAt(c) == TerrainDefOf.Space;
			}).ToHashSet();
			foreach (IntVec3 allCell in tempMap.AllCells)
			{
				if (tempMap.terrainGrid.TerrainAt(allCell) == TerrainDefOf.Space)
				{
					tempMap.terrainGrid.SetTerrain(allCell, VGEDefOf.VGE_FakeTerrain);
				}
			}
			int num = source.Min((CellRect r) =>
			{
				//IL_0000: Unknown result type (might be due to invalid IL or missing references)
				return r.minX;
			});
			int num2 = source.Min((CellRect r) =>
			{
				//IL_0000: Unknown result type (might be due to invalid IL or missing references)
				return r.minZ;
			});
			int num3 = source.Max((CellRect r) =>
			{
				//IL_0000: Unknown result type (might be due to invalid IL or missing references)
				return r.maxX;
			});
			int num4 = source.Max((CellRect r) =>
			{
				//IL_0000: Unknown result type (might be due to invalid IL or missing references)
				return r.maxZ;
			});
			CellRect val = CellRect.FromLimits(num, num2, num3, num4);
			LandingStructure_StructureSet.Unfog(tempMap, val);
			__instance.PostProcessMap(tempMap, __instance.shipFaction, hashSet);
			GeneratePawns(__instance, tempMap, val, vacuumCells);
			Rand.PopState();
			Building_GravEngine val2 = default;
			((LandingStructureBase)__instance).ScanGeneratedLayout(tempMap, val, out val2);
			((LandingStructureBase)__instance).enginePos = IntVec3.Invalid;
			((LandingStructureBase)__instance).landingRotation = Rot4.North;
			GravshipCapturer.IsGravshipRenderInProgress = true;
			GravshipCapturer.GravshipCaptureBounds = val.ExpandedBy(1);
			Find.ScreenshotModeHandler.Active = true;
			tempMap.mapDrawer.RegenerateEverythingNow();
			((LandingStructureBase)__instance).RenderAndSaveTexture(tempMap, mainCamera, val, val2);
			__instance.DespawnPawns();
		}
		catch (Exception ex)
		{
			Log.Error("Failed to capture " + ((object)__instance.structureSetDef)?.ToString() + ": " + ex.ToString());
		}
		GravshipCapturer.IsGravshipRenderInProgress = false;
		Find.ScreenshotModeHandler.Active = false;
		Current.Game.CurrentMap = originalMap;
		((Behaviour)mainCamera).enabled = wasCamEnabled;
		((Behaviour)cameraDriver).enabled = wasCamDriverEnabled;
		Find.WorldObjects.Remove((WorldObject)(object)mapParent);
		Find.Maps.Remove(tempMap);
		((LandingStructureBase)__instance).coroutineStarted = false;
	}

	public static void Impact(LandingStructure_StructureSet __instance)
	{
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_0195: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_019b: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d0: Unknown result type (might be due to invalid IL or missing references)
		Map map = ((Thing)__instance).Map;
		using (new PrefabTerrainScope(DefDatabase<PrefabDef>.AllDefsListForReading, TerrainDefOf.Space))
		{
			bool flag = false;
			try
			{
				Rand.PushState(((LandingStructureBase)__instance).randomSeed);
				flag = true;
				List<(StructurePatternOffset, Def)> list = StructureSetGenerator.SelectStandardLayouts(__instance.structureSetDef, 0f, __instance.selectedDefs);
				HashSet<Thing> hashSet = new HashSet<Thing>(map.listerThings.AllThings);
				List<CellRect> source = StructureSetGenerator.Generate(map, __instance.structureSetDef, __instance.shipFaction, ((Thing)__instance).Position, list, 0f, __instance.shipRotation);
				int num = source.Min((CellRect r) =>
				{
					//IL_0000: Unknown result type (might be due to invalid IL or missing references)
					return r.minX;
				});
				int num2 = source.Min((CellRect r) =>
				{
					//IL_0000: Unknown result type (might be due to invalid IL or missing references)
					return r.minZ;
				});
				int num3 = source.Max((CellRect r) =>
				{
					//IL_0000: Unknown result type (might be due to invalid IL or missing references)
					return r.maxX;
				});
				int num4 = source.Max((CellRect r) =>
				{
					//IL_0000: Unknown result type (might be due to invalid IL or missing references)
					return r.maxZ;
				});
				CellRect val = CellRect.FromLimits(num, num2, num3, num4);
				__instance.PostProcessMap(map, __instance.shipFaction, hashSet);
				if (__instance.pawnPositions.Count == 0)
				{
					HashSet<IntVec3> vacuumCells = map.AllCells.Where((IntVec3 c) =>
					{
						//IL_000b: Unknown result type (might be due to invalid IL or missing references)
						return map.terrainGrid.TerrainAt(c) == TerrainDefOf.Space;
					}).ToHashSet();
					GeneratePawns(__instance, map, val, vacuumCells, spawn: false);
				}
				Rand.PopState();
				flag = false;
				CellRect.Enumerator enumerator = val.GetEnumerator();
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
				__instance.OnImpact(map, val, hashSet);
				((Thing)__instance).Destroy((DestroyMode)0);
			}
			finally
			{
				if (flag)
				{
					Rand.PopState();
				}
			}
		}
	}

	public static void Unfog(Map map, CellRect cellRect)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		CellRect val = cellRect.ExpandedBy(1);
		val = val.ClipInsideMap(map);
		CellRect.Enumerator enumerator = val.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				IntVec3 current = enumerator.Current;
				if (map.fogGrid.IsFogged(current))
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

	public static void DespawnPawns(LandingStructure_StructureSet __instance)
	{
		foreach (Pawn key in __instance.pawnPositions.Keys)
		{
			if (((Thing)key).Spawned)
			{
				((Entity)key).DeSpawn((DestroyMode)0);
			}
		}
	}

	public static void OnImpact(LandingStructure_StructureSet __instance, Map map, CellRect cellRect, HashSet<Thing> preExisting)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected Obj, but got Unknown
		IntVec3 centerCell = cellRect.CenterCell;
		foreach (KeyValuePair<Pawn, IntVec3> item in __instance.pawnPositions.OrderBy((KeyValuePair<Pawn, IntVec3> pair) => ((Thing)pair.Key).thingIDNumber))
		{
			GenSpawn.Spawn((Thing)(object)item.Key, centerCell + item.Value, map, (WipeMode)0);
		}
		if (__instance.pawnPositions.Count > 0)
		{
			LordMaker.MakeNewLord(__instance.shipFaction, (LordJob)new LordJob_DefendBase_SpaceCombat(__instance.shipFaction, map.Center, 25000, false), map, (IEnumerable<Pawn>)__instance.pawnPositions.Keys.OrderBy((Pawn p) => ((Thing)p).thingIDNumber).ToList());
		}
	}

	public static void PostProcessMap(LandingStructure_StructureSet __instance, Map map, Faction faction, HashSet<Thing> preExisting)
	{
		GenStep_Warplatform.MakeAllCratesANew(map, preExisting);
		foreach (Thing allThing in map.listerThings.AllThings)
		{
			if (!preExisting.Contains(allThing) && (faction == null || allThing.Faction == faction))
			{
				CompRefuelable val = ThingCompUtility.TryGetComp<CompRefuelable>(allThing);
				if (val != null)
				{
					val.Refuel(val.Props.fuelCapacity);
				}
				CompPowerBattery val2 = ThingCompUtility.TryGetComp<CompPowerBattery>(allThing);
				if (val2 != null)
				{
					val2.SetStoredEnergyPct(1f);
				}
				CompPower_InputOnlyBattery val3 = ThingCompUtility.TryGetComp<CompPower_InputOnlyBattery>(allThing);
				if (val3 != null)
				{
					val3.SetStoredEnergyPct(1f);
				}
				CompResourceStorage val4 = ThingCompUtility.TryGetComp<CompResourceStorage>(allThing);
				if (val4 != null)
				{
					val4.AddResource(val4.Props.storageCapacity);
				}
			}
		}
	}

	public static void GeneratePawns(LandingStructure_StructureSet __instance, Map map, CellRect cellRect, HashSet<IntVec3> vacuumCells, bool spawn = true)
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		if (__instance.shipFaction == null || __instance.pawnCountRange.max <= 0)
		{
			return;
		}
		List<IntVec3> list = cellRect.Cells.Where((IntVec3 c) =>
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0030: Unknown result type (might be due to invalid IL or missing references)
			return GenGrid.InBounds(c, map) && GenGrid.Standable(c, map) && GridsUtility.Roofed(c, map) && !vacuumCells.Contains(c);
		}).ToList();
		if (list.Count == 0)
		{
			return;
		}
		IntVec3 centerCell = cellRect.CenterCell;
		int randomInRange = __instance.pawnCountRange.RandomInRange;
		for (int num = 0; num < randomInRange; num++)
		{
			Pawn val = PawnGenerator.GeneratePawn(GenCollection.RandomElementByWeight<PawnGenOption>(__instance.shipFaction.def.pawnGroupMakers.SelectMany((PawnGroupMaker gm) => gm.options), (Func<PawnGenOption, float>)((PawnGenOption opt) => opt.selectionWeight)).kind, __instance.shipFaction, (PlanetTile?)null);
			IntVec3 val2 = GenCollection.RandomElement<IntVec3>((IEnumerable<IntVec3>)list);
			__instance.pawnPositions[val] = val2 - centerCell;
			if (spawn)
			{
				GenSpawn.Spawn((Thing)(object)val, val2, map, (WipeMode)0);
			}
		}
	}

	public static void ExposeData(LandingStructure_StructureSet __instance)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Invalid comparison between Unknown and I4
		((LandingStructureBase)__instance).ExposeData();
		Scribe_Defs.Look<StructureSetDef>(ref __instance.structureSetDef, "structureSetDef");
		Scribe_Values.Look<Rot4>(ref __instance.shipRotation, "shipRotation", default(Rot4), false);
		Scribe_Values.Look<int>(ref ((LandingStructureBase)__instance).ticksToImpact, "ticksToImpact", 0, false);
		Scribe_Values.Look<int>(ref ((LandingStructureBase)__instance).ticksToImpactMax, "ticksToImpactMax", 0, false);
		Scribe_References.Look<Faction>(ref __instance.shipFaction, "shipFaction", false);
		Scribe_Values.Look<IntRange>(ref __instance.pawnCountRange, "pawnCountRange", default(IntRange), false);
		Scribe_Collections.Look<Pawn, IntVec3>(ref __instance.pawnPositions, "pawnPositions", (LookMode)2, (LookMode)1);
		LandingLayoutSelection.Expose(ref __instance.selectedDefs);
		if ((int)Scribe.mode == 4 && __instance.pawnPositions == null)
		{
			__instance.pawnPositions = new Dictionary<Pawn, IntVec3>();
		}
	}
}
