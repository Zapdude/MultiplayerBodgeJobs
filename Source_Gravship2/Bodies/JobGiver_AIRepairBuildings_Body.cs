using System.Linq;
using RimWorld;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class JobGiver_AIRepairBuildings_Body
{
	public static Job TryGiveJob(JobGiver_AIRepairBuildings __instance, Pawn pawn)
	{
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		if (pawn.skills == null || pawn.skills.GetSkill(SkillDefOf.Construction).TotallyDisabled)
		{
			return null;
		}
		Thing val = ((Thing)pawn).Map.listerBuildingsRepairable.HashSetFor(((Thing)pawn).Faction).OrderBy((Thing x) =>
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			return IntVec3Utility.DistanceToSquared(((Thing)pawn).Position, x.Position);
		}).ThenBy((Thing x) => x.thingIDNumber)
			.FirstOrDefault((Thing x) =>
			{
				//IL_0019: Unknown result type (might be due to invalid IL or missing references)
				//IL_001f: Unknown result type (might be due to invalid IL or missing references)
				//IL_0065: Unknown result type (might be due to invalid IL or missing references)
				//IL_007c: Unknown result type (might be due to invalid IL or missing references)
				return x.Faction == ((Thing)pawn).Faction && (float)IntVec3Utility.DistanceToSquared(((Thing)pawn).Position, x.Position) < __instance.maxDist * __instance.maxDist && x.def.useHitPoints && x.HitPoints < x.MaxHitPoints && ReservationUtility.CanReserve(pawn, ((LocalTargetInfo)(x)), 1, -1, (ReservationLayerDef)null, false) && ReachabilityUtility.CanReach(pawn, ((LocalTargetInfo)(x)), (PathEndMode)2, (Danger)3, false, false, (TraverseMode)0);
			});
		if (val != null)
		{
			return JobMaker.MakeJob(JobDefOf.Repair, ((LocalTargetInfo)(val)));
		}
		return null;
	}

	public static ThinkNode DeepCopy(JobGiver_AIRepairBuildings __instance, bool resolve = true)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected Obj, but got Unknown
		JobGiver_AIRepairBuildings val = (JobGiver_AIRepairBuildings)((ThinkNode_JobGiver)__instance).DeepCopy(resolve);
		val.maxDist = __instance.maxDist;
		return (ThinkNode)val;
	}
}
