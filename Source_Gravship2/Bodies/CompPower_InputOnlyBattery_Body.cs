using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using VEF.Buildings;
using VanillaGravshipExpanded2;
using Verse;

namespace Multiplayer.Compat.Workshop17.Bodies;

public class CompPower_InputOnlyBattery_Body
{
	public static float get_AmountCanAccept(CompPower_InputOnlyBattery __instance)
	{
		if (!BreakdownableUtility.IsBrokenDown((Thing)(object)((ThingComp)__instance).parent) && !__instance.StunnedByEMP)
		{
			return __instance.Props.storedEnergyMax - __instance.storedEnergy;
		}
		return 0f;
	}

	public static bool get_IsFull(CompPower_InputOnlyBattery __instance)
	{
		return __instance.storedEnergy >= __instance.Props.storedEnergyMax;
	}

	public static float get_StoredEnergy(CompPower_InputOnlyBattery __instance)
	{
		return __instance.storedEnergy;
	}

	public static float get_StoredEnergyPct(CompPower_InputOnlyBattery __instance)
	{
		return __instance.storedEnergy / __instance.Props.storedEnergyMax;
	}

	public static bool get_StunnedByEMP(CompPower_InputOnlyBattery __instance)
	{
		if (__instance.stunnableComp != null && __instance.stunnableComp.StunHandler.Stunned)
		{
			return __instance.stunnableComp.StunHandler.StunFromEMP;
		}
		return false;
	}

	public static CompProperties_InputOnlyBattery get_Props(CompPower_InputOnlyBattery __instance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected Obj, but got Unknown
		return (CompProperties_InputOnlyBattery)((ThingComp)__instance).props;
	}

	public static void AddEnergy(CompPower_InputOnlyBattery __instance, float amount)
	{
		if (amount < 0f)
		{
			Log.Error($"Cannot add negative energy {amount}");
		}
		else if (!__instance.StunnedByEMP)
		{
			if (amount > __instance.AmountCanAccept)
			{
				amount = __instance.AmountCanAccept;
			}
			__instance.storedEnergy += amount;
			if (__instance.IsFull)
			{
				__instance.DirtyNeedsCharging();
			}
		}
	}

	public static void DrawPower(CompPower_InputOnlyBattery __instance, float amount)
	{
		bool isFull = __instance.IsFull;
		__instance.storedEnergy -= amount;
		if (isFull != __instance.IsFull)
		{
			__instance.DirtyNeedsCharging();
		}
		if (__instance.storedEnergy < 0f)
		{
			Log.Error($"Drawing power we don't have from {((ThingComp)__instance).parent}");
			__instance.storedEnergy = 0f;
		}
	}

	public static void SetStoredEnergyPct(CompPower_InputOnlyBattery __instance, float pct)
	{
		bool isFull = __instance.IsFull;
		__instance.storedEnergy = __instance.Props.storedEnergyMax * Mathf.Clamp01(pct);
		if (isFull != __instance.IsFull)
		{
			__instance.DirtyNeedsCharging();
		}
	}

	public static void ReceiveCompSignal(CompPower_InputOnlyBattery __instance, string signal)
	{
		if (signal == "Breakdown")
		{
			__instance.DrawPower(__instance.StoredEnergy);
		}
	}

	public static void DrawAt(CompPower_InputOnlyBattery __instance, Vector3 drawLoc, bool flip = false)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		((ThingComp)__instance).DrawAt(drawLoc, flip);
		CustomFillableBarGaugeData powerGauge = __instance.Props.powerGauge;
		if (powerGauge != null)
		{
			powerGauge.DrawGauge((Thing)(object)((ThingComp)__instance).parent, __instance.StoredEnergyPct);
		}
	}

	public static void PostSpawnSetup(CompPower_InputOnlyBattery __instance, bool respawningAfterLoad)
	{
		((CompPower)__instance).PostSpawnSetup(respawningAfterLoad);
		__instance.mapComp = VGE2_MapComponent.GetCompFast(((Thing)((ThingComp)__instance).parent).Map);
		__instance.DirtyNeedsCharging();
	}

	public static void PostDeSpawn(CompPower_InputOnlyBattery __instance, Map map, DestroyMode mode = (DestroyMode)0)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		((CompPower)__instance).PostDeSpawn(map, mode);
		__instance.mapComp.unchargedBatteries.Remove(__instance);
	}

	public static void PostPostMake(CompPower_InputOnlyBattery __instance)
	{
		((ThingComp)__instance).PostPostMake();
		__instance.InitializeComps();
	}

	public static void PostExposeData(CompPower_InputOnlyBattery __instance)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Invalid comparison between Unknown and I4
		((CompPower)__instance).PostExposeData();
		Scribe_Values.Look<float>(ref __instance.storedEnergy, "storedEnergy", 0f, false);
		if (__instance.storedEnergy > __instance.Props.storedEnergyMax)
		{
			__instance.storedEnergy = __instance.Props.storedEnergyMax;
		}
		if ((int)Scribe.mode == 2)
		{
			__instance.InitializeComps();
		}
	}

	public static void InitializeComps(CompPower_InputOnlyBattery __instance)
	{
		__instance.stunnableComp = ((ThingComp)__instance).parent.GetComp<CompStunnable>();
	}

	public static string CompInspectStringExtra(CompPower_InputOnlyBattery __instance)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		return $"{Translator.Translate("PowerBatteryStored")}: {__instance.storedEnergy:F0} / {__instance.Props.storedEnergyMax:F0} Wd\n{((CompPower)__instance).CompInspectStringExtra()}";
	}

	public static IEnumerable<Gizmo> CompGetGizmosExtra(CompPower_InputOnlyBattery __instance)
	{
		foreach (Gizmo item in _003C_003En__0(__instance))
		{
			yield return item;
		}
		if (DebugSettings.ShowDevGizmos)
		{
			yield return (Gizmo)new Command_Action
			{
				defaultLabel = "DEV: Empty",
				action = () => _003CCompGetGizmosExtra_003Eb__26_0(__instance)
			};
			yield return (Gizmo)new Command_Action
			{
				defaultLabel = "DEV: -20% charge",
				action = () => _003CCompGetGizmosExtra_003Eb__26_1(__instance)
			};
			yield return (Gizmo)new Command_Action
			{
				defaultLabel = "DEV: Fill",
				action = () => _003CCompGetGizmosExtra_003Eb__26_2(__instance)
			};
		}
	}

	public static void DirtyNeedsCharging(CompPower_InputOnlyBattery __instance)
	{
		List<CompPower_InputOnlyBattery> list = __instance.mapComp?.unchargedBatteries;
		if (list != null)
		{
			if (__instance.IsFull)
			{
				list.Remove(__instance);
			}
			else
			{
				GenCollection.AddDistinct<CompPower_InputOnlyBattery>(list, __instance);
			}
		}
	}

	public static void _003CCompGetGizmosExtra_003Eb__26_0(CompPower_InputOnlyBattery __instance)
	{
		__instance.SetStoredEnergyPct(0f);
	}

	public static void _003CCompGetGizmosExtra_003Eb__26_1(CompPower_InputOnlyBattery __instance)
	{
		__instance.DrawPower(Mathf.Min(__instance.Props.storedEnergyMax * 0.2f, __instance.StoredEnergy));
	}

	public static void _003CCompGetGizmosExtra_003Eb__26_2(CompPower_InputOnlyBattery __instance)
	{
		__instance.SetStoredEnergyPct(1f);
	}

	public static IEnumerable<Gizmo> _003C_003En__0(CompPower_InputOnlyBattery __instance)
	{
		return ((CompPower)__instance).CompGetGizmosExtra();
	}
}
