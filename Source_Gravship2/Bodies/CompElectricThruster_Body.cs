using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using VanillaGravshipExpanded;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class CompElectricThruster_Body
{
	public static Thing get_ParentThing(CompElectricThruster __instance)
	{
		return (Thing)(object)((ThingComp)__instance).parent;
	}

	public static CompProperties_ElectricThruster get_Props(CompElectricThruster __instance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		return (CompProperties_ElectricThruster)((ThingComp)__instance).props;
	}

	public static void PostPostMake(CompElectricThruster __instance)
	{
		((ThingComp)__instance).PostPostMake();
		__instance.InitializeComps();
	}

	public static void PostExposeData(CompElectricThruster __instance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Invalid comparison between Unknown and I4
		((ThingComp)__instance).PostExposeData();
		if ((int)Scribe.mode == 2)
		{
			__instance.InitializeComps();
		}
	}

	public static void InitializeComps(CompElectricThruster __instance)
	{
		__instance.battery = ((ThingComp)__instance).parent.GetComp<CompPower_InputOnlyBattery>();
	}

	public static bool IsActive(CompElectricThruster __instance, Building_GravEngine engine, List<CompGravshipThruster> activeThrusters, List<IGravshipFuelProvider> otherProviders)
	{
		otherProviders?.RemoveAll((IGravshipFuelProvider x) =>
		{
			CompElectricThruster val = (CompElectricThruster)(object)((x is CompElectricThruster) ? x : null);
			return val != null && val.battery.StoredEnergyPct < val.Props.chargePercentRequiredToUse;
		});
		return __instance.battery.StoredEnergyPct >= __instance.Props.chargePercentRequiredToUse;
	}

	public static float CurrentFuel(CompElectricThruster __instance, Building_GravEngine engine)
	{
		return __instance.battery.StoredEnergy;
	}

	public static float MaxFuel(CompElectricThruster __instance, Building_GravEngine engine)
	{
		return __instance.battery.Props.storedEnergyMax;
	}

	public static float CurrentRangeProvidedByFuel(CompElectricThruster __instance, Building_GravEngine engine, List<CompGravshipThruster> activeThrusters, List<IGravshipFuelProvider> otherProviders)
	{
		return __instance.RangeProvidedByThrusters(otherProviders, true);
	}

	public static float MaxRangeProvidedByFuel(CompElectricThruster __instance, Building_GravEngine engine, List<CompGravshipThruster> activeThrusters, List<IGravshipFuelProvider> otherProviders)
	{
		return __instance.RangeProvidedByThrusters(otherProviders, false);
	}

	public static float RangeProvidedByThrusters(CompElectricThruster __instance, List<IGravshipFuelProvider> otherProviders, bool scaled)
	{
		float range = GetRange(__instance);
		otherProviders?.RemoveAll((IGravshipFuelProvider x) =>
		{
			CompElectricThruster val = (CompElectricThruster)(object)((x is CompElectricThruster) ? x : null);
			if (val == null)
			{
				return false;
			}
			range += GetRange(val);
			return true;
		});
		return range;
		float GetRange(CompElectricThruster thruster)
		{
			float num;
			if (thruster.Props.rangeCapacityOverride > 0f)
			{
				num = thruster.Props.rangeCapacityOverride;
			}
			else
			{
				num = StatUtility.GetStatValueFromList(((CompProperties_Facility)thruster.Props).statOffsets, StatDefOf.GravshipRange, thruster.Props.rangeCapacityOverride);
				if (num < 0f)
				{
					num = 0f;
				}
			}
			if (scaled)
			{
				num *= thruster.battery.StoredEnergyPct;
			}
			return num;
		}
	}

	public static float ConsumeFuelAmount(CompElectricThruster __instance, Building_GravEngine engine, float fuelAmount)
	{
		float storedEnergy = __instance.battery.StoredEnergy;
		if (storedEnergy >= fuelAmount)
		{
			__instance.battery.DrawPower(fuelAmount);
			return fuelAmount;
		}
		__instance.battery.SetStoredEnergyPct(0f);
		return storedEnergy;
	}

	public static FuelUsageData ConsumeFuelRatio(CompElectricThruster __instance, Building_GravEngine engine, float fuelConsumedRatio, List<CompGravshipThruster> activeThrusters, List<IGravshipFuelProvider> otherProviders, bool consumeFuel)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected Obj, but got Unknown
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		if (!__instance.IsActive(engine, activeThrusters, otherProviders))
		{
			return null;
		}
		FuelUsageData report = new FuelUsageData();
		float maxCharge = __instance.battery.Props.storedEnergyMax;
		float range = __instance.RangeProvidedByThrusters((List<IGravshipFuelProvider>)null, false);
		float charge = __instance.battery.StoredEnergy * fuelConsumedRatio;
		report.fuelData[(IGravshipFuelProvider)(object)__instance] = (report.totalAmount = charge);
		if (consumeFuel)
		{
			__instance.battery.DrawPower(charge);
		}
		otherProviders?.RemoveAll((IGravshipFuelProvider x) =>
		{
			CompElectricThruster val = (CompElectricThruster)(object)((x is CompElectricThruster) ? x : null);
			if (val == null)
			{
				return false;
			}
			charge = val.battery.StoredEnergy * fuelConsumedRatio;
			maxCharge += val.battery.Props.storedEnergyMax;
			range += val.RangeProvidedByThrusters((List<IGravshipFuelProvider>)null, false);
			report.fuelData[x] = charge;
			FuelUsageData val2 = report;
			val2.totalAmount += charge;
			if (consumeFuel)
			{
				val.battery.DrawPower(charge);
			}
			return true;
		});
		report.sortingOrder = range * fuelConsumedRatio;
		if (report.totalAmount > 0f)
		{
			report.reportString = $"{GenText.ToStringPercent(report.totalAmount / maxCharge)} {Translator.Translate("VGE_UsedCharge")}";
		}
		return report;
	}

	public static float AddFuelAmount(CompElectricThruster __instance, Building_GravEngine engine, float amount)
	{
		float amountCanAccept = __instance.battery.AmountCanAccept;
		if (amountCanAccept >= amount)
		{
			__instance.battery.AddEnergy(amount);
			return amount;
		}
		__instance.battery.AddEnergy(amountCanAccept);
		return amountCanAccept;
	}

	public static FuelTabEntry GetFuelTabEntry(CompElectricThruster __instance, Building_GravEngine engine, List<CompGravshipThruster> activeThrusters, List<IGravshipFuelProvider> otherProviders)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Expected Obj, but got Unknown
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_012a: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_017f: Unknown result type (might be due to invalid IL or missing references)
		SimpleMultiLineTextEntry val = new SimpleMultiLineTextEntry(engine);
		TaggedString val2 = Translator.Translate("VGE_FuelTab_ElectricThrusters");
		val.title = ((string)(val2.CapitalizeFirst()));
		SimpleMultiLineTextEntry entry = val;
		((FuelTabEntry)entry).fuelProviders.Add(__instance.ParentThing);
		((FuelTabEntry)entry).thrusters.Add(__instance.ParentThing);
		float maxRange = __instance.MaxRangeProvidedByFuel(engine, activeThrusters, (List<IGravshipFuelProvider>)null);
		float currentRange = 0f;
		int chargedThrusters = 0;
		if (__instance.battery.StoredEnergyPct >= __instance.Props.chargePercentRequiredToUse)
		{
			chargedThrusters++;
		}
		if (__instance.IsActive(engine, activeThrusters, (List<IGravshipFuelProvider>)null))
		{
			currentRange += __instance.CurrentRangeProvidedByFuel(engine, activeThrusters, (List<IGravshipFuelProvider>)null);
		}
		otherProviders?.RemoveAll((IGravshipFuelProvider x) =>
		{
			CompElectricThruster val3 = (CompElectricThruster)(object)((x is CompElectricThruster) ? x : null);
			if (val3 == null)
			{
				return false;
			}
			((FuelTabEntry)entry).fuelProviders.Add(val3.ParentThing);
			((FuelTabEntry)entry).thrusters.Add(val3.ParentThing);
			maxRange += val3.MaxRangeProvidedByFuel(engine, activeThrusters, (List<IGravshipFuelProvider>)null);
			if (val3.IsActive(engine, activeThrusters, (List<IGravshipFuelProvider>)null))
			{
				currentRange += val3.CurrentRangeProvidedByFuel(engine, activeThrusters, (List<IGravshipFuelProvider>)null);
			}
			if (val3.battery.StoredEnergyPct >= val3.Props.chargePercentRequiredToUse)
			{
				chargedThrusters++;
			}
			return true;
		});
		List<string> text = entry.text;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(2, 2);
		val2 = Translator.Translate("VGE_FuelTab_ChargedThrusters");
		defaultInterpolatedStringHandler.AppendFormatted<TaggedString>(val2.CapitalizeFirst());
		defaultInterpolatedStringHandler.AppendLiteral(": ");
		defaultInterpolatedStringHandler.AppendFormatted(chargedThrusters);
		text.Add(defaultInterpolatedStringHandler.ToStringAndClear());
		List<string> text2 = entry.text;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler2 = new DefaultInterpolatedStringHandler(5, 3);
		val2 = Translator.Translate("VGE_FuelTab_Range");
		defaultInterpolatedStringHandler2.AppendFormatted<TaggedString>(val2.CapitalizeFirst());
		defaultInterpolatedStringHandler2.AppendLiteral(": ");
		defaultInterpolatedStringHandler2.AppendFormatted(currentRange);
		defaultInterpolatedStringHandler2.AppendLiteral(" / ");
		defaultInterpolatedStringHandler2.AppendFormatted(maxRange);
		text2.Add(defaultInterpolatedStringHandler2.ToStringAndClear());
		return (FuelTabEntry)(object)entry;
	}
}
