using System.Linq;
using RimWorld;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class JobGiver_AIManTerminal_Body
{
	public static Job TryGiveJob(JobGiver_AIManTerminal __instance, Pawn pawn)
	{
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		if (pawn.skills == null || pawn.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled)
		{
			return null;
		}
		float num = float.MaxValue;
		Thing val = null;
		for (int i = 0; i < JobGiver_AIManTerminal.EnemyTerminals.Count; i++)
		{
			Thing val2 = ((Thing)pawn).Map.listerThings.ThingsOfDef(JobGiver_AIManTerminal.EnemyTerminals[i]).OrderBy((Thing x) =>
			{
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				return IntVec3Utility.DistanceToSquared(((Thing)pawn).Position, x.Position);
			}).ThenBy((Thing x) => x.thingIDNumber)
				.FirstOrDefault((Thing x) =>
				{
					//IL_0019: Unknown result type (might be due to invalid IL or missing references)
					//IL_001f: Unknown result type (might be due to invalid IL or missing references)
					//IL_004a: Unknown result type (might be due to invalid IL or missing references)
					//IL_0073: Unknown result type (might be due to invalid IL or missing references)
					if (x.Faction == ((Thing)pawn).Faction && (float)IntVec3Utility.DistanceToSquared(((Thing)pawn).Position, x.Position) < __instance.maxDist * __instance.maxDist && ReservationUtility.CanReserve(pawn, ((LocalTargetInfo)(x)), 1, -1, (ReservationLayerDef)null, false))
					{
						CompMannable val3 = ThingCompUtility.TryGetComp<CompMannable>(x);
						if (val3 != null && !val3.MannedNow)
						{
							return ReachabilityUtility.CanReach(pawn, ((LocalTargetInfo)(x)), (PathEndMode)4, (Danger)3, false, false, (TraverseMode)0);
						}
					}
					return false;
				});
			if (val2 != null)
			{
				int num2 = IntVec3Utility.DistanceToSquared(((Thing)pawn).Position, val2.Position);
				if ((float)num2 < num)
				{
					val = val2;
					num = num2;
				}
			}
		}
		if (val != null)
		{
			return JobMaker.MakeJob(InternalDefOf.VGE_OperateEnemyTerminal, ((LocalTargetInfo)(val)));
		}
		return null;
	}

	public static ThinkNode DeepCopy(JobGiver_AIManTerminal __instance, bool resolve = true)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected Obj, but got Unknown
		JobGiver_AIManTerminal val = (JobGiver_AIManTerminal)((ThinkNode_JobGiver)__instance).DeepCopy(resolve);
		val.maxDist = __instance.maxDist;
		return (ThinkNode)val;
	}
}
