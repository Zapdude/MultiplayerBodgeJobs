using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Andromeda.NicePlantsMenu")]
internal sealed class NicePlantsMenu
{
	private sealed class EstimateStatus
	{
		internal Game Game;

		internal Map Map;

		internal IPlantToGrowSettable[] Zones;

		internal bool Unknown;

		internal bool Running;

		internal bool Retrying;
	}

	private sealed class RecordRead
	{
		internal RecordRead Previous;

		internal EstimateStatus Status;

		internal List<IPlantToGrowSettable> SourceZones;

		internal bool Unavailable;

		internal bool Ran;
	}

	private sealed class BrowserRead
	{
		internal BrowserRead Previous;

		internal object Browser;

		internal bool PensUnknown;

		internal bool PenReading;
	}

	private sealed class CalculationRead
	{
		internal CalculationRead Previous;

		internal long Clock;

		internal Game Game;

		internal Map Map;

		internal IPlantToGrowSettable Settable;

		internal bool Unavailable;
	}

	private static readonly ConditionalWeakTable<object, EstimateStatus> estimateStatus = new ConditionalWeakTable<object, EstimateStatus>();

	[ThreadStatic]
	private static RecordRead readingRecord;

	[ThreadStatic]
	private static BrowserRead readingBrowser;

	[ThreadStatic]
	private static CalculationRead readingCalculation;

	private static readonly AccessTools.FieldRef<RegionAndRoomUpdater, bool> RegionWorking = AccessTools.FieldRefAccess<RegionAndRoomUpdater, bool>("working");

	private static readonly AccessTools.FieldRef<RegionAndRoomUpdater, Map> RegionOwner = AccessTools.FieldRefAccess<RegionAndRoomUpdater, Map>("map");

	private static readonly AccessTools.FieldRef<RegionGrid, Map> GridOwner = AccessTools.FieldRefAccess<RegionGrid, Map>("map");

	private static FieldInfo browserZones;

	private static FieldInfo browserRecord;

	private static FieldInfo browserPens;

	private static FieldInfo browserInfoX;

	private static FieldInfo recordRecipes;

	private static FieldInfo recordProduction;

	private static MethodInfo checkProblems;

	private static MethodInfo checkPens;

	public NicePlantsMenu(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("NicePlantsMenu.Dialog_PlantBrowser") ?? throw new TypeLoadException("NicePlantsMenu.Dialog_PlantBrowser");
		MethodInfo methodInfo = NiceTabSync.Required(type, "SetPlantDefToGrow", new Type[2]
		{
			typeof(List<IPlantToGrowSettable>),
			typeof(ThingDef)
		});
		MP.RegisterSyncMethod(methodInfo).SetVersion(32);
		NiceTabSync.Patch(methodInfo, typeof(NicePlantsMenu), "Validate");
		NiceTabSync.InstallClock();
		Type type2 = AccessTools.TypeByName("NicePlantsMenu.GrowUtility") ?? throw new TypeLoadException("NicePlantsMenu.GrowUtility");
		InstallEstimateProtection(type, type2);
		NiceTabSync.Patch(NiceTabSync.Required(type2, "Cache", new Type[2]
		{
			typeof(IPlantToGrowSettable),
			typeof(ThingDef)
		}), typeof(NicePlantsMenu), "BeforeEstimate", "AfterEstimate");
	}

	private static bool Validate(List<IPlantToGrowSettable> zones, ThingDef p)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (zones == null || zones.Count == 0 || zones.Count > 256 || p?.plant == null || zones.Any((IPlantToGrowSettable z) => z?.Map == null))
		{
			return false;
		}
		Map map = zones[0].Map;
		if (map.Disposed || !Find.Maps.Contains(map) || !Command_SetPlantToGrow.IsPlantAvailable(p, map))
		{
			return false;
		}
		if (zones.Distinct().Count() == zones.Count)
		{
			return zones.All((IPlantToGrowSettable z) => z.Map == map && ((z is Zone item && map.zoneManager.AllZones.Contains(item) && map.ParentFaction == Faction.OfPlayer) || (z is Building_PlantGrower { Spawned: not false, Destroyed: false } building_PlantGrower && building_PlantGrower.Faction == Faction.OfPlayer)));
		}
		return false;
	}

	private static void BeforeEstimate(IPlantToGrowSettable __0, out long __state)
	{
		__state = 0L;
		if (MP.IsInMultiplayer && __0?.Map != null)
		{
			NiceTabSync.BeginRead(__0.Map, out __state);
		}
	}

	private static Exception AfterEstimate(Exception __exception, long __state)
	{
		if (__state != 0L)
		{
			NiceTabSync.EndRead(__state);
		}
		return __exception;
	}

	private static void InstallEstimateProtection(Type browser, Type grow)
	{
		Type type = AccessTools.TypeByName("NicePlantsMenu.PlantRecord") ?? throw new TypeLoadException("NicePlantsMenu.PlantRecord");
		browserZones = AccessTools.DeclaredField(browser, "plantZones") ?? throw new MissingFieldException(browser.FullName, "plantZones");
		browserRecord = AccessTools.DeclaredField(browser, "drawInfoFor") ?? throw new MissingFieldException(browser.FullName, "drawInfoFor");
		browserPens = AccessTools.DeclaredField(browser, "CheckingInPens") ?? throw new MissingFieldException(browser.FullName, "CheckingInPens");
		browserInfoX = AccessTools.DeclaredField(browser, "infoBlockMidX") ?? throw new MissingFieldException(browser.FullName, "infoBlockMidX");
		recordRecipes = AccessTools.DeclaredField(type, "recipes") ?? throw new MissingFieldException(type.FullName, "recipes");
		recordProduction = AccessTools.DeclaredField(type, "observingProduction") ?? throw new MissingFieldException(type.FullName, "observingProduction");
		checkProblems = NiceTabSync.Required(type, "CheckProblems", new Type[3]
		{
			typeof(Map),
			typeof(List<IPlantToGrowSettable>),
			typeof(List<Pawn>)
		});
		checkPens = NiceTabSync.Required(browser, "CheckAnimalPens", Type.EmptyTypes);
		Patch(NiceTabSync.Required(grow, "Calculate", new Type[3]
		{
			typeof(IPlantToGrowSettable),
			typeof(ThingDef),
			typeof(bool)
		}), "BeforeCalculation", "AfterCalculation");
		MpCompat.harmony.Patch(NiceTabSync.Required(grow, "IsGreenHouse", new Type[1] { typeof(IPlantToGrowSettable) }), null, null, new HarmonyMethod(typeof(NicePlantsMenu), "PreviewRoomCalls"));
		Patch(checkProblems, "BeforeProblems", "AfterProblems");
		Patch(NiceTabSync.Required(browser, "ScanAvailablePlants", Type.EmptyTypes), "BeforeScan", "AfterScan");
		Patch(checkPens, "BeforePens", "AfterPens");
		Patch(NiceTabSync.Required(browser, "DrawInfo", new Type[1] { typeof(Rect) }), "BeforeInfo");
		Patch(NiceTabSync.Required(type, "SolveRecipes", new Type[1] { typeof(Map) }), "BeforeRecipes");
		MpCompat.harmony.Patch(AccessTools.DeclaredPropertyGetter(type, "HasProblems") ?? throw new MissingMethodException(type.FullName, "get_HasProblems"), null, new HarmonyMethod(typeof(NicePlantsMenu), "ProblemsVisible"));
		static void Patch(MethodInfo method, string before, string after = null)
		{
			NiceTabSync.Patch(method, typeof(NicePlantsMenu), before, after);
		}
	}

	private static bool MapReady(Map map)
	{
		if (map != null && !map.Disposed && Current.Game != null && Find.Maps.Contains(map) && map.regionGrid != null && GridOwner(map.regionGrid) == map && map.regionAndRoomUpdater != null && RegionOwner(map.regionAndRoomUpdater) == map && map.regionDirtyer != null && !RegionWorking(map.regionAndRoomUpdater))
		{
			return !map.regionAndRoomUpdater.AnythingToRebuild;
		}
		return false;
	}

	private static Room ExistingRoom(IntVec3 cell, Map map)
	{
		if (!MapReady(map) || !cell.InBounds(map))
		{
			return null;
		}
		Region validRegionAt_NoRebuild = map.regionGrid.GetValidRegionAt_NoRebuild(cell);
		if (validRegionAt_NoRebuild == null || !validRegionAt_NoRebuild.valid || (validRegionAt_NoRebuild.type & RegionType.Set_All) == 0 || validRegionAt_NoRebuild.Map != map)
		{
			return null;
		}
		Room room = validRegionAt_NoRebuild.District?.Room;
		if (room == null || room.Map != map)
		{
			return null;
		}
		return room;
	}

	private static bool SettableReady(IPlantToGrowSettable settable)
	{
		if (settable is Building_PlantGrower building_PlantGrower)
		{
			if (building_PlantGrower.Spawned && !building_PlantGrower.Destroyed)
			{
				return ExistingRoom(building_PlantGrower.Position, building_PlantGrower.Map) != null;
			}
			return false;
		}
		if (settable is Zone zone)
		{
			if (zone.cells != null && zone.cells.Count != 0)
			{
				return ExistingRoom(zone.Position, zone.Map) != null;
			}
			return false;
		}
		return false;
	}

	private static bool SelectionReady(Map map, IList<IPlantToGrowSettable> zones)
	{
		if (MapReady(map) && zones != null && zones.Count != 0)
		{
			return zones.All((IPlantToGrowSettable z) => z != null && z.Map == map && SettableReady(z));
		}
		return false;
	}

	private static bool BeforeCalculation(IPlantToGrowSettable __0, ref (long, int)? __result, out CalculationRead __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__state = new CalculationRead
		{
			Previous = readingCalculation,
			Game = Current.Game,
			Map = __0?.Map,
			Settable = __0,
			Unavailable = !SettableReady(__0)
		};
		readingCalculation = __state;
		if (__state.Unavailable)
		{
			__result = null;
			return false;
		}
		NiceTabSync.BeginRead(__0.Map, out __state.Clock);
		return true;
	}

	private static Exception AfterCalculation(Exception __exception, CalculationRead __state, ref (long, int)? __result)
	{
		if (__state == null)
		{
			return __exception;
		}
		try
		{
			__state.Unavailable |= __state.Game != Current.Game || __state.Settable?.Map != __state.Map || !SettableReady(__state.Settable);
			if (__state.Unavailable)
			{
				__result = null;
				if (readingRecord != null)
				{
					readingRecord.Unavailable = true;
				}
			}
		}
		finally
		{
			readingCalculation = __state.Previous;
			if (__state.Clock != 0L)
			{
				NiceTabSync.EndRead(__state.Clock);
			}
		}
		return __exception;
	}

	private static IEnumerable<CodeInstruction> PreviewRoomCalls(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.DeclaredMethod(typeof(GridsUtility), "GetRoom", new Type[2]
		{
			typeof(IntVec3),
			typeof(Map)
		});
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(NicePlantsMenu), "PreviewRoom");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count != 2)
		{
			throw new InvalidOperationException("Nice Plants greenhouse room calls changed: " + count);
		}
	}

	private static Room PreviewRoom(IntVec3 cell, Map map)
	{
		if (!MP.IsInMultiplayer)
		{
			return cell.GetRoom(map);
		}
		Room room = ExistingRoom(cell, map);
		if (room == null && readingCalculation != null)
		{
			readingCalculation.Unavailable = true;
		}
		return room;
	}

	private static bool BeforeProblems(object __instance, Map map, List<IPlantToGrowSettable> zones, out RecordRead __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		EstimateStatus value = estimateStatus.GetValue(__instance, (object _) => new EstimateStatus());
		__state = new RecordRead
		{
			Previous = readingRecord,
			Status = value,
			SourceZones = zones
		};
		bool running = value.Running;
		value.Unknown = true;
		if (running)
		{
			if (readingRecord != null)
			{
				readingRecord.Unavailable = true;
			}
			return false;
		}
		value.Game = Current.Game;
		value.Map = map;
		value.Zones = zones?.ToArray();
		value.Running = true;
		readingRecord = __state;
		RecordRead obj = __state;
		BrowserRead browserRead = readingBrowser;
		obj.Unavailable = (browserRead != null && browserRead.PensUnknown) || !SelectionReady(map, zones);
		__state.Ran = !__state.Unavailable;
		return __state.Ran;
	}

	private static Exception AfterProblems(Exception __exception, RecordRead __state)
	{
		if (__state == null || readingRecord != __state)
		{
			return __exception;
		}
		try
		{
			EstimateStatus status = __state.Status;
			status.Unknown = __exception != null || !__state.Ran || __state.Unavailable || status.Game != Current.Game || !SelectionReady(status.Map, status.Zones) || __state.SourceZones == null || !__state.SourceZones.SequenceEqual(status.Zones);
			return __exception;
		}
		finally
		{
			__state.Status.Running = false;
			readingRecord = __state.Previous;
		}
	}

	private static void BeforeScan(object __instance, out BrowserRead __state)
	{
		__state = null;
		if (MP.IsInMultiplayer)
		{
			__state = new BrowserRead
			{
				Previous = readingBrowser,
				Browser = __instance
			};
			readingBrowser = __state;
		}
	}

	private static Exception AfterScan(Exception __exception, BrowserRead __state)
	{
		if (__state != null)
		{
			readingBrowser = __state.Previous;
		}
		return __exception;
	}

	private static bool BeforePens(object __instance, ref List<Pawn> __result, out bool __state)
	{
		__state = false;
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		List<IPlantToGrowSettable> list = browserZones.GetValue(__instance) as List<IPlantToGrowSettable>;
		Map map = ((list == null) ? null : list.FirstOrDefault()?.Map);
		bool flag = readingBrowser != null && readingBrowser.Browser == __instance;
		if (MapReady(map) && list.All((IPlantToGrowSettable z) => z != null && z.Map == map) && (!flag || !readingBrowser.PenReading))
		{
			if (flag)
			{
				readingBrowser.PenReading = true;
				__state = true;
			}
			return true;
		}
		if (flag)
		{
			readingBrowser.PensUnknown = true;
		}
		__result = map?.mapPawns?.ColonyAnimals ?? new List<Pawn>();
		return false;
	}

	private static Exception AfterPens(object __instance, Exception __exception, bool __state)
	{
		if (readingBrowser != null && readingBrowser.Browser == __instance)
		{
			if (__exception != null)
			{
				readingBrowser.PensUnknown = true;
			}
			if (__state)
			{
				readingBrowser.PenReading = false;
			}
		}
		return __exception;
	}

	private static bool BeforeRecipes(object __instance)
	{
		if (!MP.IsInMultiplayer || !estimateStatus.TryGetValue(__instance, out var value))
		{
			return true;
		}
		if (value.Game != Current.Game || !SelectionReady(value.Map, value.Zones))
		{
			value.Unknown = true;
		}
		return !value.Unknown;
	}

	private static void ProblemsVisible(object __instance, ref bool __result)
	{
		if (MP.IsInMultiplayer && estimateStatus.TryGetValue(__instance, out var value) && value.Unknown)
		{
			__result = true;
		}
	}

	private static bool BeforeInfo(object __instance, Rect rect)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		object value = browserRecord.GetValue(__instance);
		if (value == null || !estimateStatus.TryGetValue(value, out var status))
		{
			return true;
		}
		List<IPlantToGrowSettable> list = browserZones.GetValue(__instance) as List<IPlantToGrowSettable>;
		bool flag = status.Game == Current.Game && list != null && status.Zones != null && list.SequenceEqual(status.Zones) && list.All((IPlantToGrowSettable z) => z != null && z.Map == status.Map);
		if (!flag || !SelectionReady(status.Map, list))
		{
			status.Unknown = true;
		}
		if (!status.Unknown)
		{
			return true;
		}
		if (flag && !status.Running && !status.Retrying && SelectionReady(status.Map, list))
		{
			BrowserRead previous = readingBrowser;
			status.Retrying = true;
			readingBrowser = new BrowserRead
			{
				Previous = previous,
				Browser = __instance
			};
			try
			{
				(browserPens.GetValue(__instance) as IList)?.Clear();
				List<Pawn> list2 = (List<Pawn>)InvokeAuthor(checkPens, __instance, Array.Empty<object>());
				InvokeAuthor(checkProblems, value, new object[3] { status.Map, list, list2 });
				if (!status.Unknown)
				{
					recordRecipes.SetValue(value, null);
					(recordProduction.GetValue(value) as IList)?.Clear();
					browserInfoX.SetValue(__instance, -1f);
					return true;
				}
			}
			catch
			{
				status.Unknown = true;
				throw;
			}
			finally
			{
				status.Retrying = false;
				readingBrowser = previous;
			}
		}
		Widgets.Label(rect.ContractedBy(12f), "NPM_ExpectedDateHarvest".Translate() + ": " + "Unknown".Translate());
		return false;
	}

	private static object InvokeAuthor(MethodInfo method, object owner, object[] args)
	{
		try
		{
			return method.Invoke(owner, args);
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			throw;
		}
	}
}
