using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class CompExowormJoiner_Body
{
	public static void PostExposeData(CompExowormJoiner __instance)
	{
		((ThingComp)__instance).PostExposeData();
		Scribe_Collections.Look<Pawn>(ref __instance.candidates, "candidates", (LookMode)3, Array.Empty<object>());
	}

	public static void CompTick(CompExowormJoiner __instance)
	{
		((ThingComp)__instance).CompTick();
		if (Gen.IsHashIntervalTick((Thing)(object)((ThingComp)__instance).parent, 120) && ((Thing)((ThingComp)__instance).parent).Spawned)
		{
			__instance.TryInitiateKnotForming();
		}
	}

	public static void TryInitiateKnotForming(CompExowormJoiner __instance)
	{
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_026f: Unknown result type (might be due to invalid IL or missing references)
		ThingWithComps parent = ((ThingComp)__instance).parent;
		Pawn val = (Pawn)(object)((parent is Pawn) ? parent : null);
		if (val.Dead || val.Downed || ((Thing)val).Destroyed || val.InMentalState || ((Thing)val).Faction == Faction.OfPlayerSilentFail || val.CurJob?.def == InternalDefOf.VGE_FormKnot)
		{
			return;
		}
		Map map = ((Thing)val).Map;
		float num = 8f;
		__instance.candidates = new List<Pawn>();
		foreach (Thing item in map.listerThings.ThingsOfDef(((Thing)val).def))
		{
			Pawn val2 = (Pawn)(object)((item is Pawn) ? item : null);
			if (val2 != null && !val2.Dead && !val2.Downed && !((Thing)val2).Destroyed && ((Thing)val2).Faction == ((Thing)val).Faction)
			{
				IntVec3 position = ((Thing)val2).Position;
				if (position.InHorDistOf(((Thing)val).Position, num) && val2.CurJob?.def != InternalDefOf.VGE_FormKnot)
				{
					__instance.candidates.Add(val2);
				}
			}
		}
		if (__instance.candidates.Count < 10)
		{
			return;
		}
		__instance.candidates.Sort((Pawn a, Pawn b) => ((Thing)a).thingIDNumber.CompareTo(((Thing)b).thingIDNumber));
		IntVec3 avgPos = new IntVec3(Mathf.RoundToInt((float)__instance.candidates.Average((Pawn p) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return ((Thing)p).Position.x;
		})), 0, Mathf.RoundToInt((float)__instance.candidates.Average((Pawn p) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			return ((Thing)p).Position.z;
		})));
		Pawn anchor = __instance.candidates.MinBy((Pawn p) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			return IntVec3Utility.DistanceToSquared(((Thing)p).Position, avgPos);
		});
		__instance.candidates = __instance.candidates.OrderBy((Pawn p) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			return IntVec3Utility.DistanceToSquared(((Thing)p).Position, ((Thing)anchor).Position);
		}).ThenBy((Pawn p) => ((Thing)p).thingIDNumber).Take(10)
			.ToList();
		ThingCompUtility.TryGetComp<CompExowormJoiner>((Thing)(object)anchor).candidates = __instance.candidates;
		foreach (Pawn candidate in __instance.candidates)
		{
			candidate.pather.StopDead();
			Job val3 = JobMaker.MakeJob(InternalDefOf.VGE_FormKnot, ((LocalTargetInfo)((Thing)(object)anchor)));
			candidate.jobs.StartJob(val3, (JobCondition)16, (ThinkNode)null, false, true, (ThinkTreeDef)null, (JobTag?)null, false, false, (bool?)null, false, true, false);
		}
	}
}
