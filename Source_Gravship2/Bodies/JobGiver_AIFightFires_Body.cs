using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class JobGiver_AIFightFires_Body
{
	public static Job TryGiveJob(JobGiver_AIFightFires __instance, Pawn pawn)
	{
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		if (pawn.WorkTagIsDisabled((WorkTags)4096))
		{
			return null;
		}
		Thing val = null;
		JobDef val2 = null;
		float num = float.MaxValue;
		foreach (var (val5, val6) in GetFireDefs())
		{
			Thing val7 = ((Thing)pawn).Map.listerThings.ThingsOfDef(val5).OrderBy((Thing x) =>
			{
				//IL_0006: Unknown result type (might be due to invalid IL or missing references)
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				return IntVec3Utility.DistanceToSquared(((Thing)pawn).Position, x.Position);
			}).ThenBy((Thing x) => x.thingIDNumber)
				.FirstOrDefault((Thing x) =>
				{
					//IL_0001: Unknown result type (might be due to invalid IL or missing references)
					//IL_0036: Unknown result type (might be due to invalid IL or missing references)
					//IL_004d: Unknown result type (might be due to invalid IL or missing references)
					Thing parent = ((AttachableThing)x).parent;
					Pawn val8 = (Pawn)(object)((parent is Pawn) ? parent : null);
					return val8 == null || (val8.Downed && ((Thing)val8).Faction == ((Thing)pawn).Faction && ReservationUtility.CanReserve(pawn, ((LocalTargetInfo)(x)), 1, -1, (ReservationLayerDef)null, false) && ReachabilityUtility.CanReach(pawn, ((LocalTargetInfo)(x)), (PathEndMode)2, (Danger)3, false, false, (TraverseMode)0));
				});
			if (val7 != null)
			{
				int num2 = IntVec3Utility.DistanceToSquared(((Thing)pawn).Position, val7.Position);
				if ((float)num2 < num && (float)num2 <= __instance.maxDist * __instance.maxDist)
				{
					val = val7;
					val2 = val6;
					num = num2;
				}
			}
		}
		if (val == null || val2 == null)
		{
			return null;
		}
		return JobMaker.MakeJob(val2, ((LocalTargetInfo)(val)));
	}

	public static ThinkNode DeepCopy(JobGiver_AIFightFires __instance, bool resolve = true)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected Obj, but got Unknown
		JobGiver_AIFightFires val = (JobGiver_AIFightFires)((ThinkNode_JobGiver)__instance).DeepCopy(resolve);
		val.maxDist = __instance.maxDist;
		return (ThinkNode)val;
	}

	// The reviewed VGE2 build exposes this dictionary; the 1.7 build does not, and the loader
	// skips this body when the field is absent, so bind it late instead of at compile time.
	private static Dictionary<ThingDef, JobDef> GetFireDefs()
	{
		return (Dictionary<ThingDef, JobDef>)typeof(JobGiver_AIFightFires)
			.GetField("FireDefs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
			.GetValue(null);
	}
}
