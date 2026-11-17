using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class CompVacuumWarningLight_Body
{
	[Serializable]
	[CompilerGenerated]
	private sealed class _003C_003Ec
	{
		public static readonly _003C_003Ec _003C_003E9 = new _003C_003Ec();

		public static RegionProcessor _003C_003E9__28_3;

		public static Func<Pawn, int> _003C_003E9__28_4;

		internal bool _003CScarePawnsAway_003Eb__28_3(Region region)
		{
			foreach (Thing item in region.ListerThings.ThingsInGroup((ThingRequestGroup)12))
			{
				Pawn val = (Pawn)(object)((item is Pawn) ? item : null);
				if (val != null)
				{
					CompVacuumWarningLight.TempPawnsWorkingList.Add(val);
				}
			}
			return false;
		}

		internal int _003CScarePawnsAway_003Eb__28_4(Pawn p)
		{
			return ((Thing)p).thingIDNumber;
		}
	}

	public static float get_VacuumLevel(CompVacuumWarningLight __instance)
	{
		return __instance.vacuumLevel;
	}

	public static void set_VacuumLevel(CompVacuumWarningLight __instance, float value)
	{
		bool isVacuumConcern = __instance.IsVacuumConcern;
		__instance.vacuumLevel = value;
		if (isVacuumConcern != __instance.IsVacuumConcern && ((Thing)((ThingComp)__instance).parent).Spawned)
		{
			CompGlower glower = __instance.glower;
			if (glower != null)
			{
				glower.UpdateLit(((Thing)((ThingComp)__instance).parent).Map);
			}
		}
	}

	public static float get_ConcerningVacuumLevel(CompVacuumWarningLight __instance)
	{
		return __instance.concerningVacuumLevel;
	}

	public static void set_ConcerningVacuumLevel(CompVacuumWarningLight __instance, float value)
	{
		float num = __instance.Props.minMaxVacuumRanges.ClampToRange(value);
		if (__instance.concerningVacuumLevel == num)
		{
			return;
		}
		bool isVacuumConcern = __instance.IsVacuumConcern;
		__instance.concerningVacuumLevel = num;
		bool isVacuumConcern2 = __instance.IsVacuumConcern;
		if (!((Thing)((ThingComp)__instance).parent).Spawned)
		{
			return;
		}
		if (isVacuumConcern2 != isVacuumConcern)
		{
			CompGlower glower = __instance.glower;
			if (glower != null)
			{
				glower.UpdateLit(((Thing)((ThingComp)__instance).parent).Map);
			}
		}
		if (!isVacuumConcern & isVacuumConcern2)
		{
			__instance.ScarePawnsAway();
		}
	}

	public static bool get_EvacuatePawns(CompVacuumWarningLight __instance)
	{
		return __instance.evacuatePawns;
	}

	public static void set_EvacuatePawns(CompVacuumWarningLight __instance, bool value)
	{
		if (value && !__instance.evacuatePawns && __instance.IsVacuumConcern)
		{
			__instance.ScarePawnsAway();
		}
		__instance.evacuatePawns = value;
	}

	public static bool get_IsVacuumConcern(CompVacuumWarningLight __instance)
	{
		return __instance.vacuumLevel >= __instance.ConcerningVacuumLevel;
	}

	public static CompProperties_VacuumWarningLight get_Props(CompVacuumWarningLight __instance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		return (CompProperties_VacuumWarningLight)((ThingComp)__instance).props;
	}

	public static bool ShouldBeLitNow(CompVacuumWarningLight __instance)
	{
		return __instance.IsVacuumConcern;
	}

	public static void PostPostMake(CompVacuumWarningLight __instance)
	{
		((ThingComp)__instance).PostPostMake();
		__instance.concerningVacuumLevel = __instance.Props.baseVacuumLevel;
		__instance.evacuatePawns = __instance.Props.evacuatePawnsByDefault;
		__instance.InitComps();
	}

	public static void CompTickInterval(CompVacuumWarningLight __instance, int delta)
	{
		((ThingComp)__instance).CompTickInterval(delta);
		if (__instance.IsVacuumConcern && Gen.IsHashIntervalTick((Thing)(object)((ThingComp)__instance).parent, 250, delta))
		{
			__instance.ScarePawnsAway();
		}
	}

	public static void CompTickRare(CompVacuumWarningLight __instance)
	{
		((ThingComp)__instance).CompTickRare();
		if (__instance.IsVacuumConcern)
		{
			__instance.ScarePawnsAway();
		}
	}

	public static void CompTickLong(CompVacuumWarningLight __instance)
	{
		((ThingComp)__instance).CompTickLong();
		if (__instance.IsVacuumConcern)
		{
			__instance.ScarePawnsAway();
		}
	}

	public static void PostSpawnSetup(CompVacuumWarningLight __instance, bool respawningAfterLoad)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		((ThingComp)__instance).PostSpawnSetup(respawningAfterLoad);
		__instance.VacuumLevel = VacuumUtility.GetVacuum(((Thing)((ThingComp)__instance).parent).Position, ((Thing)((ThingComp)__instance).parent).Map);
		if (__instance.IsVacuumConcern)
		{
			__instance.ScarePawnsAway();
		}
	}

	public static void PostDeSpawn(CompVacuumWarningLight __instance, Map map, DestroyMode mode = (DestroyMode)0)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		((ThingComp)__instance).PostDeSpawn(map, mode);
		__instance.VacuumLevel = 0f;
	}

	public static void ReceiveCompSignal(CompVacuumWarningLight __instance, string signal)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		((ThingComp)__instance).ReceiveCompSignal(signal);
		if (signal == "Vacuum" && ((Thing)((ThingComp)__instance).parent).Spawned)
		{
			bool isVacuumConcern = __instance.IsVacuumConcern;
			__instance.VacuumLevel = VacuumUtility.GetVacuum(((Thing)((ThingComp)__instance).parent).Position, ((Thing)((ThingComp)__instance).parent).Map);
			if (!isVacuumConcern && __instance.IsVacuumConcern)
			{
				__instance.ScarePawnsAway();
			}
		}
	}

	public static IEnumerable<Gizmo> CompGetGizmosExtra(CompVacuumWarningLight __instance)
	{
		foreach (Gizmo item in _003C_003En__0(__instance))
		{
			yield return item;
		}
		if (((Thing)((ThingComp)__instance).parent).Faction == Faction.OfPlayer)
		{
			yield return (Gizmo)new Command_Action
			{
				defaultLabel = ((string)(Translator.Translate("VGE_VacuumEmergencyLight_UnsafeVacuumLevel"))),
				defaultDesc = ((string)(Translator.Translate("VGE_VacuumEmergencyLight_UnsafeVacuumLevelDesc"))),
				icon = (Texture)(object)CompVacuumWarningLight.VacuumResistanceGizmo,
				action = () => _003CCompGetGizmosExtra_003Eb__27_0(__instance)
			};
		}
	}

	public static void ScarePawnsAway(CompVacuumWarningLight __instance)
	{
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected Obj, but got Unknown
		//IL_00b6: Expected Obj, but got Unknown
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Expected Obj, but got Unknown
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Expected Obj, but got Unknown
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
		CompVacuumWarningLight val = __instance;
		if (__instance.Props.visualOnly || !__instance.EvacuatePawns || ((Thing)((ThingComp)__instance).parent).Faction == null)
		{
			return;
		}
		Room room = RegionAndRoomQuery.GetRoom((Thing)(object)((ThingComp)__instance).parent, (RegionType)15);
		IntVec3 pos = ((Thing)((ThingComp)__instance).parent).Position;
		CompVacuumWarningLight.TempPawnsWorkingList.Clear();
		if (room == null || !__instance.Props.alwaysAffectsWholeRoom || room.PsychologicallyOutdoors)
		{
			RegionTraverser.BreadthFirstTraverse(pos, ((Thing)((ThingComp)__instance).parent).Map, (RegionEntryPredicate)((Region _, Region to) =>
			{
				//IL_000d: Unknown result type (might be due to invalid IL or missing references)
				//IL_0012: Unknown result type (might be due to invalid IL or missing references)
				return pos.InHorDistOf(to.extentsClose.ClosestCellTo(pos), val.Props.radius);
			}), (RegionProcessor)((Region region) =>
			{
				//IL_001e: Unknown result type (might be due to invalid IL or missing references)
				//IL_0023: Unknown result type (might be due to invalid IL or missing references)
				//IL_0027: Unknown result type (might be due to invalid IL or missing references)
				foreach (Thing item in region.ListerThings.ThingsInGroup((ThingRequestGroup)12))
				{
					IntVec3 position = item.Position;
					if (position.InHorDistOf(pos, val.Props.radius))
					{
						Pawn val7 = (Pawn)(object)((item is Pawn) ? item : null);
						if (val7 != null)
						{
							CompVacuumWarningLight.TempPawnsWorkingList.Add(val7);
						}
					}
				}
				return false;
			}), 999999, (RegionType)14);
		}
		else
		{
			IntVec3 val2 = pos;
			Map map = ((Thing)((ThingComp)__instance).parent).Map;
			RegionEntryPredicate val3 = (Region _, Region to) => to.Room == room;
			RegionProcessor val4 = _003C_003Ec._003C_003E9__28_3;
			if (val4 == null)
			{
				RegionProcessor val5 = (Region region) =>
				{
					foreach (Thing item2 in region.ListerThings.ThingsInGroup((ThingRequestGroup)12))
					{
						Pawn val7 = (Pawn)(object)((item2 is Pawn) ? item2 : null);
						if (val7 != null)
						{
							CompVacuumWarningLight.TempPawnsWorkingList.Add(val7);
						}
					}
					return false;
				};
				_003C_003Ec._003C_003E9__28_3 = val5;
				val4 = val5;
			}
			RegionTraverser.BreadthFirstTraverse(val2, map, val3, val4, 999999, (RegionType)14);
		}
		foreach (Pawn item3 in CompVacuumWarningLight.TempPawnsWorkingList.OrderBy((Pawn p) => ((Thing)p).thingIDNumber))
		{
			if (((Thing)item3).Spawned && item3.health.CanCrawlOrMove && __instance.CaresAboutLowVacuum(item3) && item3.CurJob?.def != JobDefOf.GotoOxygenatedArea)
			{
				IntVec3 val6 = CompVacuumWarningLight.ClosestOxygenatedCell(item3, __instance.ConcerningVacuumLevel, TraverseParms.For(item3, (Danger)3, (TraverseMode)0, false, false, false, true), (RegionType)14);
				if (val6.IsValid)
				{
					item3.jobs.StartJob(JobMaker.MakeJob(JobDefOf.GotoOxygenatedArea, ((LocalTargetInfo)(val6))), (JobCondition)16, (ThinkNode)null, true, true, (ThinkTreeDef)null, (JobTag?)null, false, false, (bool?)null, false, true, false);
				}
			}
		}
		CompVacuumWarningLight.TempPawnsWorkingList.Clear();
	}

	public static bool CaresAboutLowVacuum(CompVacuumWarningLight __instance, Pawn pawn)
	{
		if (((Thing)((ThingComp)__instance).parent).Faction != ((Thing)pawn).Faction)
		{
			Faction faction = ((Thing)((ThingComp)__instance).parent).Faction;
			Pawn_GuestTracker guest = pawn.guest;
			if (faction != ((guest != null) ? guest.HostFaction : null))
			{
				return false;
			}
		}
		if (pawn.Drafted)
		{
			return false;
		}
		if (pawn.RaceProps.IsMechanoid || pawn.RaceProps.IsDrone)
		{
			return false;
		}
		if (pawn.IsMutant && !pawn.mutant.Def.breathesAir)
		{
			return false;
		}
		if (StatExtension.GetStatValue((Thing)(object)pawn, StatDefOf.VacuumResistance, true, 60) >= 0.95f)
		{
			return false;
		}
		return true;
	}

	public static IntVec3 ClosestOxygenatedCell(Pawn pawn, float concerningVacuumLevel, TraverseParms traverseParms, RegionType traversableRegionTypes = (RegionType)14)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Expected Obj, but got Unknown
		//IL_0070: Expected Obj, but got Unknown
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		Region region = GridsUtility.GetRegion(((Thing)pawn).Position, ((Thing)pawn).Map, traversableRegionTypes);
		if (region == null)
		{
			return IntVec3.Invalid;
		}
		IntVec3 foundCell = IntVec3.Invalid;
		RegionTraverser.BreadthFirstTraverse(region, (RegionEntryPredicate)((Region _, Region to) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			return to.Allows(traverseParms, false);
		}), (RegionProcessor)((Region reg) =>
		{
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Unknown result type (might be due to invalid IL or missing references)
			if (reg.IsDoorway)
			{
				return false;
			}
			if (reg.Room.Vacuum >= concerningVacuumLevel)
			{
				return false;
			}
			IntVec3 val = default;
			if (!JobGiver_FindOxygen.TryGetAllowedCellInRegion(reg, pawn, out val, -1f))
			{
				return false;
			}
			foundCell = val;
			return true;
		}), 999999, traversableRegionTypes);
		return foundCell;
	}

	public static void PostExposeData(CompVacuumWarningLight __instance)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Invalid comparison between Unknown and I4
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Invalid comparison between Unknown and I4
		((ThingComp)__instance).PostExposeData();
		Scribe_Values.Look<float>(ref __instance.concerningVacuumLevel, "concerningVacuumLevel", 0.25f, false);
		Scribe_Values.Look<bool>(ref __instance.evacuatePawns, "evacuatePawns", true, false);
		if ((int)Scribe.mode == 4)
		{
			__instance.concerningVacuumLevel = __instance.Props.minMaxVacuumRanges.ClampToRange(__instance.concerningVacuumLevel);
		}
		if ((int)Scribe.mode == 2)
		{
			__instance.InitComps();
		}
	}

	public static void InitComps(CompVacuumWarningLight __instance)
	{
		__instance.glower = ((ThingComp)__instance).parent.GetComp<CompGlower>();
	}

	public static void _003CCompGetGizmosExtra_003Eb__27_0(CompVacuumWarningLight __instance)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected Obj, but got Unknown
		Find.WindowStack.Add((Window)new Dialog_VacuumWarningLight(__instance.Props.minMaxVacuumRanges, __instance.EvacuatePawns, !__instance.Props.visualOnly, __instance.ConcerningVacuumLevel, 0.01f));
	}

	public static IEnumerable<Gizmo> _003C_003En__0(CompVacuumWarningLight __instance)
	{
		return ((ThingComp)__instance).CompGetGizmosExtra();
	}
}
