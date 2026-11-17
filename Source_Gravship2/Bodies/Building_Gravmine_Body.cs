using System.Collections.Generic;
using System.Linq;
using RimWorld;
using VanillaGravshipExpanded2;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class Building_Gravmine_Body
{
	public static void Tick(Building_Gravmine __instance)
	{
		((Building_Trap)__instance).Tick();
		if (((Thing)__instance).Spawned && Gen.IsHashIntervalTick((Thing)(object)__instance, 15))
		{
			__instance.CheckSpringRadius();
		}
	}

	public static void CheckSpringRadius(Building_Gravmine __instance)
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		HashSet<Thing> hashSet = new HashSet<Thing>();
		foreach (IAttackTarget item in from t in ((Thing)__instance).Map.attackTargetsCache.TargetsHostileToFaction(((Thing)__instance).Faction)
			orderby t.Thing?.thingIDNumber ?? int.MaxValue
			select t)
		{
			Thing thing = item.Thing;
			if (thing == null || !thing.Spawned)
			{
				continue;
			}
			IntVec3 position = thing.Position;
			if (!position.InHorDistOf(((Thing)__instance).Position, 5.9f))
			{
				continue;
			}
			hashSet.Add(thing);
			if (__instance.thingsInRadius.Contains(thing))
			{
				continue;
			}
			float num = StatExtension.GetStatValue((Thing)(object)__instance, StatDefOf.TrapSpringChance, true, -1);
			Pawn val = (Pawn)(object)((thing is Pawn) ? thing : null);
			if (val != null)
			{
				num = ((Building_Trap)__instance).SpringChance(val);
			}
			if (Rand.Chance(num))
			{
				((Building_Trap)__instance).Spring((Pawn)(object)((thing is Pawn) ? thing : null));
				if (((Thing)__instance).Destroyed)
				{
					return;
				}
			}
		}
		__instance.thingsInRadius = hashSet;
	}

	public static void ExposeData(Building_Gravmine __instance)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Invalid comparison between Unknown and I4
		((Building_Trap)__instance).ExposeData();
		Scribe_Collections.Look<Thing>(ref __instance.thingsInRadius, "thingsInRadius", (LookMode)3);
		if ((int)Scribe.mode == 4)
		{
			__instance.thingsInRadius?.RemoveWhere((Thing x) => x == null);
			if (__instance.thingsInRadius == null)
			{
				__instance.thingsInRadius = new HashSet<Thing>();
			}
		}
	}
}
