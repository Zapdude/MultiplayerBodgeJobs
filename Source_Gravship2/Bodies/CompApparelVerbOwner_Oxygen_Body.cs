using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using VanillaGravshipExpanded;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class CompApparelVerbOwner_Oxygen_Body
{
	public static CompProperties_ApparelVerbOwnerOxygen get_Props(CompApparelVerbOwner_Oxygen __instance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		return (CompProperties_ApparelVerbOwnerOxygen)((ThingComp)__instance).props;
	}

	public static int get_RemainingCharges(CompApparelVerbOwner_Oxygen __instance)
	{
		if (__instance.compOxygen == null)
		{
			return 0;
		}
		return Mathf.FloorToInt(__instance.compOxygen.RemainingChargesExact / __instance.Props.chargePerUse);
	}

	public static int get_MaxCharges(CompApparelVerbOwner_Oxygen __instance)
	{
		if (__instance.compOxygen == null)
		{
			return 0;
		}
		return Mathf.FloorToInt((float)__instance.compOxygen.MaxCharges / __instance.Props.chargePerUse);
	}

	public static string get_LabelRemaining(CompApparelVerbOwner_Oxygen __instance)
	{
		return $"{__instance.RemainingCharges} / {__instance.MaxCharges}";
	}

	public static string get_GizmoExtraLabel(CompApparelVerbOwner_Oxygen __instance)
	{
		return __instance.LabelRemaining;
	}

	public static string CompInspectStringExtra(CompApparelVerbOwner_Oxygen __instance)
	{
		return null;
	}

	public static string CompTipStringExtra(CompApparelVerbOwner_Oxygen __instance)
	{
		return null;
	}

	public static void CompTickInterval(CompApparelVerbOwner_Oxygen __instance, int delta)
	{
		((ThingComp)__instance).CompTickInterval(delta);
		__instance.remainingCooldownTicks -= delta;
	}

	public static IEnumerable<StatDrawEntry> SpecialDisplayStats(CompApparelVerbOwner_Oxygen __instance)
	{
		yield break;
	}

	public static void UsedOnce(CompApparelVerbOwner_Oxygen __instance)
	{
		__instance.remainingCooldownTicks = __instance.Props.cooldownTicks;
		if (__instance.compOxygen != null)
		{
			CompApparelOxygenProvider compOxygen = __instance.compOxygen;
			compOxygen.RemainingChargesExact -= __instance.Props.chargePerUse;
		}
	}

	public static bool CanBeUsed(CompApparelVerbOwner_Oxygen __instance, out string reason)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		if (!((CompApparelVerbOwner)__instance).CanBeUsed(out reason))
		{
			return false;
		}
		if (__instance.compOxygen == null || __instance.compOxygen.RemainingChargesExact < __instance.Props.chargePerUse)
		{
			reason = ((string)(Translator.Translate("VGE_NotEnoughOxygen")));
			return false;
		}
		if (__instance.remainingCooldownTicks > 0)
		{
			TaggedString val = TranslatorFormattedStringExtensions.Translate("AbilityOnCooldown", ((NamedArgument)(GenDate.ToStringTicksToPeriod(__instance.remainingCooldownTicks, true, false, true, true, false))));
			reason = val.Resolve();
			return false;
		}
		Map mapHeld = ((Thing)((ThingComp)__instance).parent).MapHeld;
		if (mapHeld != null && __instance.LayerPreventsCast(mapHeld.Tile))
		{
			PlanetTile tile = mapHeld.Tile;
			PlanetLayerDef layerDef = tile.LayerDef;
			reason = ((string)(TranslatorFormattedStringExtensions.Translate("CannotPerformPlanetLayer", NamedArgumentUtility.Named((object)layerDef.gerundLabel, "GERUND"), NamedArgumentUtility.Named((object)((Def)layerDef).label, "LAYER"))));
			return false;
		}
		return true;
	}

	public static bool LayerPreventsCast(CompApparelVerbOwner_Oxygen __instance, PlanetTile tile)
	{
		if (!tile.Valid)
		{
			return false;
		}
		foreach (VerbProperties verbProperty in ((CompApparelVerbOwner)__instance).VerbProperties)
		{
			if (!GenList.NullOrEmpty<PlanetLayerDef>((IList<PlanetLayerDef>)verbProperty.layerWhitelist) && !verbProperty.layerWhitelist.Contains(tile.LayerDef))
			{
				return true;
			}
			if (!GenList.NullOrEmpty<PlanetLayerDef>((IList<PlanetLayerDef>)verbProperty.layerBlacklist) && verbProperty.layerBlacklist.Contains(tile.LayerDef))
			{
				return true;
			}
		}
		return false;
	}

	public static IEnumerable<Gizmo> CompGetWornGizmosExtra(CompApparelVerbOwner_Oxygen __instance)
	{
		bool drafted = ((CompApparelVerbOwner)__instance).Wearer.Drafted;
		if ((drafted && !((CompProperties_ApparelVerbOwner)__instance.Props).displayGizmoWhileDrafted) || (!drafted && !((CompProperties_ApparelVerbOwner)__instance.Props).displayGizmoWhileUndrafted))
		{
			yield break;
		}
		foreach (Verb allVerb in ((CompApparelVerbOwner)__instance).VerbTracker.AllVerbs)
		{
			if (allVerb.verbProps.hasStandardCommand)
			{
				yield return (Gizmo)(object)__instance.CreateVerbTargetCommand_Oxygen((Thing)(object)((ThingComp)__instance).parent, allVerb);
			}
		}
		if (DebugSettings.ShowDevGizmos && __instance.remainingCooldownTicks > 0)
		{
			yield return (Gizmo)new Command_Action
			{
				defaultLabel = "DEV: Reset cooldown",
				action = () => _003CCompGetWornGizmosExtra_003Eb__19_0(__instance)
			};
		}
	}

	public static void PostPostMake(CompApparelVerbOwner_Oxygen __instance)
	{
		((CompApparelVerbOwner_Charged)__instance).PostPostMake();
		__instance.InitComps();
	}

	public static void PostExposeData(CompApparelVerbOwner_Oxygen __instance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Invalid comparison between Unknown and I4
		((CompApparelVerbOwner_Charged)__instance).PostExposeData();
		if ((int)Scribe.mode == 2)
		{
			__instance.InitComps();
		}
	}

	public static void InitComps(CompApparelVerbOwner_Oxygen __instance)
	{
		__instance.compOxygen = ((ThingComp)__instance).parent.GetComp<CompApparelOxygenProvider>();
		((CompApparelVerbOwner_Charged)__instance).remainingCharges = 0;
	}

	public static Command_VerbTarget_Oxygen CreateVerbTargetCommand_Oxygen(CompApparelVerbOwner_Oxygen __instance, Thing gear, Verb verb)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Expected Obj, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_012b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0168: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_0178: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		Command_VerbTarget_Oxygen val = new Command_VerbTarget_Oxygen((CompApparelVerbOwner)(object)__instance)
		{
			defaultDesc = ((Def)gear.def).description,
			hotKey = ((CompProperties_ApparelVerbOwner)__instance.Props).hotKey,
			defaultLabel = verb.verbProps.label,
			verb = verb
		};
		if (verb.verbProps.defaultProjectile != null && verb.verbProps.commandIcon == null)
		{
			((Command)val).icon = (Texture)(object)((BuildableDef)verb.verbProps.defaultProjectile).uiIcon;
			((Command)val).iconAngle = ((BuildableDef)verb.verbProps.defaultProjectile).uiIconAngle;
			((Command)val).iconOffset = ((BuildableDef)verb.verbProps.defaultProjectile).uiIconOffset;
			((Command_VerbOwner)val).overrideColor = verb.verbProps.defaultProjectile.graphicData.color;
		}
		else
		{
			((Command)val).icon = (Texture)(object)(((Texture)(object)verb.UIIcon != (Texture)(object)BaseContent.BadTex) ? verb.UIIcon : ((BuildableDef)gear.def).uiIcon);
			((Command)val).iconAngle = ((BuildableDef)gear.def).uiIconAngle;
			((Command)val).iconOffset = ((BuildableDef)gear.def).uiIconOffset;
			((Command)val).defaultIconColor = gear.DrawColor;
		}
		string text = default;
		if (!((CompApparelVerbOwner)__instance).Wearer.IsColonistPlayerControlled)
		{
			((Gizmo)val).Disable(((string)(Translator.Translate("CannotOrderNonControlled"))));
		}
		else if (verb.verbProps.violent && ((CompApparelVerbOwner)__instance).Wearer.WorkTagIsDisabled((WorkTags)8))
		{
			TaggedString val2 = TranslatorFormattedStringExtensions.Translate("IsIncapableOfViolenceLower", ((NamedArgument)(((Entity)((CompApparelVerbOwner)__instance).Wearer).LabelShort)), ((NamedArgument)((Thing)(object)((CompApparelVerbOwner)__instance).Wearer)));
			((Gizmo)val).Disable(((string)(val2.CapitalizeFirst() + ".")));
		}
		else if (!((CompApparelVerbOwner)__instance).CanBeUsed(out text))
		{
			((Gizmo)val).Disable(text);
		}
		return val;
	}

	public static void _003CCompGetWornGizmosExtra_003Eb__19_0(CompApparelVerbOwner_Oxygen __instance)
	{
		__instance.remainingCooldownTicks = 0;
	}
}
