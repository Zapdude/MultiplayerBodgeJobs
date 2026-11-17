using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using VanillaGravshipExpanded;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class VGE2LaunchInfo_Body
{
	public static void ExposeData(VGE2LaunchInfo __instance)
	{
		((ExtendedLaunchInfoComp)__instance).ExposeData();
		Scribe_Values.Look<float>(ref __instance.launchVisibilityOffset, "launchVisibilityOffset", 0f, false);
		Scribe_Values.Look<float>(ref __instance.launchVisibilityFactor, "launchVisibilityFactor", 1f, false);
		Scribe_Values.Look<float>(ref __instance.launchVisibilityOffsetNoFactor, "launchVisibilityOffsetNoFactor", 1f, false);
	}

	public static void PostLandingEnded(VGE2LaunchInfo __instance, Gravship gravship)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			WorldComponent_GravshipCombat instance = WorldComponent_GravshipCombat.Instance;
			Building_GravEngine engine = gravship.Engine;
			instance.lastKnownShipMap = ((Thing)engine).Map;
			instance.lastKnownShipTile = ((Thing)engine).Tile;
			ScenPart_TheGravship active = ScenPart_TheGravship.Active;
			if (active != null && !active.enemyArrived && active.enemyArrivalTick > 0)
			{
				active.NotifyPlayerEscaped();
				instance.RemoveVisibility(instance.visibility);
			}
			else if (instance.incomingWarplatform)
			{
				__instance.ApplyEarlyEscape(instance, gravship);
			}
			else
			{
				__instance.ApplyVisibilityGain(gravship);
			}
			__instance.ApplySignalJammerEffect(gravship);
		}
		catch (Exception value)
		{
			Log.Error($"[VGE2] Exception in LandingEnded: {value}");
		}
	}

	public static void ApplySignalJammerEffect(VGE2LaunchInfo __instance, Gravship gravship)
	{
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Unknown result type (might be due to invalid IL or missing references)
		CompSignalJammer val = (from c in gravship.Engine.GravshipComponents
			where ((Thing)((ThingComp)c).parent).def == InternalDefOf.SignalJammer
			select ((ThingComp)c).parent.GetComp<CompSignalJammer>()).FirstOrDefault((CompSignalJammer x) => x != null && !x.OnCooldown);
		if (val == null)
		{
			return;
		}
		Map map = ((Thing)gravship.Engine).Map;
		List<Building_GravshipTurret> list = new List<Building_GravshipTurret>();
		foreach (Building item in map.listerBuildings.allBuildingsNonColonist)
		{
			Building_GravshipTurret val2 = (Building_GravshipTurret)(object)((item is Building_GravshipTurret) ? item : null);
			if (val2 != null && GenHostility.HostileTo((Thing)(object)val2, Faction.OfPlayer))
			{
				list.Add(val2);
			}
		}
		if (!GenCollection.Any<Building_GravshipTurret>(list))
		{
			return;
		}
		val.StartCooldown();
		foreach (Building_GravshipTurret item2 in list)
		{
			CompStunnable comp = ((ThingWithComps)item2).GetComp<CompStunnable>();
			if (comp != null)
			{
				StunHandler stunHandler = comp.StunHandler;
				if (stunHandler != null)
				{
					stunHandler.StunFor(30000, (Thing)(object)((ThingComp)val).parent, true, true, false);
				}
			}
			FleckMaker.ThrowMicroSparks(((Thing)item2).DrawPos, map);
			for (int num = 0; num < 3; num++)
			{
				CellRect val3 = GenAdj.OccupiedRect((Thing)(object)item2);
				IntVec3 randomCell = val3.RandomCell;
				FleckMaker.Static(randomCell.ToVector3Shifted(), map, InternalDefOf.BlastEMP, 1f);
			}
		}
		Messages.Message(((string)(Translator.Translate("VGE_SignalJammerStunnedArtillery"))), MessageTypeDefOf.PositiveEvent, false);
	}

	public static void ApplyVisibilityGain(VGE2LaunchInfo __instance, Gravship gravship)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		Building_GravEngine val = ((gravship != null) ? gravship.Engine : null);
		ExtendedLaunchInfo val2 = ((val != null) ? LaunchInfo_ExposeData_Patch.ExtendedInfo(val.launchInfo, false) : null);
		if (val2 != null && val2.launchSourceTile.Valid)
		{
			int distance = GravshipHelper.GetDistance(val2.launchSourceTile, ((Thing)val).Tile);
			int count = val.ValidSubstructure.Count;
			WorldComponent_GravshipCombat.Instance.AddVisibility((float)(count * distance) * __instance.launchVisibilityFactor + __instance.launchVisibilityOffset, true, true);
			if (__instance.launchVisibilityOffsetNoFactor != 0f)
			{
				WorldComponent_GravshipCombat.Instance.AddVisibility(__instance.launchVisibilityOffsetNoFactor, false, false);
			}
		}
	}

	public static void ApplyEarlyEscape(VGE2LaunchInfo __instance, WorldComponent_GravshipCombat comp, Gravship gravship)
	{
		if (comp.incomingWarplatform)
		{
			GravshipThreatDef activeThreatDef = comp.activeThreatDef;
			comp.incomingWarplatform = false;
			activeThreatDef.Worker.OnEarlyEscape(((Thing)gravship.Engine).Map);
		}
	}
}
