using System;
using System.Linq;
using RimWorld;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class VGE2_MapComponent_Body
{
	public static bool get_EvacuationActive(VGE2_MapComponent __instance)
	{
		return __instance.evacuationActive;
	}

	public static void set_EvacuationActive(VGE2_MapComponent __instance, bool value)
	{
		if (__instance.evacuationActive != value)
		{
			__instance.evacuationActive = value;
			if (value)
			{
				__instance.StartEvacuation(true);
			}
			else
			{
				__instance.StopEvacuation();
			}
		}
	}

	public static VGE2_MapComponent GetCompFast(Map map)
	{
		if (map != VGE2_MapComponent.cachedMap)
		{
			return VGE2_MapComponent.cachedComp = (VGE2_MapComponent.cachedMap = map).GetComponent<VGE2_MapComponent>();
		}
		return VGE2_MapComponent.cachedComp;
	}

	public static void MapComponentTick(VGE2_MapComponent __instance)
	{
		((MapComponent)__instance).MapComponentTick();
		if (__instance.EvacuationActive)
		{
			if (__instance.activeEscapePods.Count == 0 || __instance.activeEscapePods.All((CompEscapePod pod) =>
			{
				Faction faction = ((Thing)((ThingComp)pod).parent).Faction;
				return faction != null && !faction.IsPlayer;
			}))
			{
				__instance.EvacuationActive = false;
			}
			else if (Gen.IsHashIntervalTick(((MapComponent)__instance).map, 2000))
			{
				__instance.StartEvacuation(false);
			}
		}
	}

	public static void ExposeData(VGE2_MapComponent __instance)
	{
		((MapComponent)__instance).ExposeData();
		Scribe_Values.Look<bool>(ref __instance.evacuationActive, "evacuationActive", false, false);
	}

	public static void StartEvacuation(VGE2_MapComponent __instance, bool evacuateDrafted)
	{
		foreach (Pawn item in ((MapComponent)__instance).map.mapPawns.AllHumanlike.OrderBy((Pawn p) => ((Thing)p).thingIDNumber))
		{
			if (((Thing)item).Faction == Faction.OfPlayer && !item.IsSlaveOfColony && !__instance.TryEvacuate(item, evacuateDrafted))
			{
				return;
			}
		}
		if (!ModsConfig.IdeologyActive)
		{
			return;
		}
		foreach (Pawn item2 in ((MapComponent)__instance).map.mapPawns.AllHumanlike.OrderBy((Pawn p) => ((Thing)p).thingIDNumber))
		{
			if (item2.IsSlaveOfColony && !__instance.TryEvacuate(item2, evacuateDrafted))
			{
				break;
			}
		}
	}

	public static void StopEvacuation(VGE2_MapComponent __instance)
	{
		foreach (Pawn item in ((MapComponent)__instance).map.mapPawns.AllHumanlike.OrderBy((Pawn p) => ((Thing)p).thingIDNumber))
		{
			if (item.jobs != null && item.RaceProps.Humanlike && ((Thing)item).Faction == Faction.OfPlayer)
			{
				item.jobs.jobQueue.RemoveAll(item, (Predicate<Job>)((Job job) => job?.def == InternalDefOf.VGE_EscapePod_Enter));
				if (item.jobs.curJob?.def == InternalDefOf.VGE_EscapePod_Enter)
				{
					item.jobs.EndCurrentJob((JobCondition)4, true, true);
				}
			}
		}
	}

	public static bool TryEvacuate(VGE2_MapComponent __instance, Pawn pawn, bool evacuateDrafted)
	{
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Unknown result type (might be due to invalid IL or missing references)
		if (!evacuateDrafted && pawn.Drafted)
		{
			return true;
		}
		if (pawn.jobs == null || !pawn.RaceProps.Humanlike || pawn.Downed)
		{
			return true;
		}
		if (pawn.CurJob?.def != null && VGE2_MapComponent.JobDefsWithPriorityOverEvacuation.Contains(pawn.CurJobDef))
		{
			return true;
		}
		if (GenCollection.Any<QueuedJob>(pawn.jobs.jobQueue.jobs, (Predicate<QueuedJob>)((QueuedJob x) => x.job?.def != null && VGE2_MapComponent.JobDefsWithPriorityOverEvacuation.Contains(x.job.def))))
		{
			return true;
		}
		bool result = false;
		foreach (CompEscapePod item in __instance.activeEscapePods.Where((CompEscapePod pod) => ((Thing)((ThingComp)pod).parent).Faction == null || ((Thing)((ThingComp)pod).parent).Faction.IsPlayer).OrderBy((CompEscapePod pod) =>
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			return IntVec3Utility.DistanceToSquared(((Thing)((ThingComp)pod).parent).Position, ((Thing)pawn).Position);
		}).ThenBy((CompEscapePod pod) => ((Thing)((ThingComp)pod).parent).thingIDNumber))
		{
			result = true;
			if (((MapComponent)__instance).map.reservationManager.CanReserve(pawn, ((LocalTargetInfo)((Thing)(object)((ThingComp)item).parent)), 1, -1, (ReservationLayerDef)null, false) && ReachabilityUtility.CanReach(pawn, ((LocalTargetInfo)((Thing)(object)((ThingComp)item).parent)), (PathEndMode)1, (Danger)3, false, false, (TraverseMode)0))
			{
				item.ClaimIfNeeded();
				if (pawn.Drafted)
				{
					pawn.drafter.Drafted = false;
				}
				pawn.jobs.StartJob(JobMaker.MakeJob(InternalDefOf.VGE_EscapePod_Enter, ((LocalTargetInfo)((Thing)(object)((ThingComp)item).parent))), (JobCondition)16, (ThinkNode)null, true, true, (ThinkTreeDef)null, (JobTag?)null, false, false, (bool?)null, false, true, false);
				break;
			}
		}
		return result;
	}
}
