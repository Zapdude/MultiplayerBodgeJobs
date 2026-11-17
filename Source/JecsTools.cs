using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("jecrell.jecstools")]
[MpCompatFor("zal.jecslitels")]
[MpCompatFor("zal.jecsliterwom")]
public class JecsTools
{
	private static bool patched;

	private static AccessTools.FieldRef<object, ThingComp> pawnAbilityUserField;

	private static AccessTools.FieldRef<object, object> compAbilityUserAbilityDataField;

	private static FastInvokeHandler abilityDataAllPowersGetter;

	private static FastInvokeHandler abilityUserUtilityGetCompsMethod;

	public JecsTools(ModContentPack mod)
	{
		if (!patched)
		{
			patched = true;
			int num = 0;
			if (PatchAbilities())
			{
				num++;
			}
			Type type = AccessTools.TypeByName("CompActivatableEffect.CompActivatableEffect");
			if (type != null)
			{
				MpCompat.RegisterLambdaMethod(type, "EquippedGizmos", 0, 1);
				num++;
			}
			Type type2 = AccessTools.TypeByName("CompInstalledPart.InstalledPartFloatMenuPatch");
			if (type2 != null)
			{
				MP.RegisterSyncMethodLambda(type2, "GetFloatMenus", 3);
				num++;
			}
			Type type3 = AccessTools.TypeByName("CompSlotLoadable.CompSlotLoadable");
			if (type3 != null)
			{
				MP.RegisterSyncMethod(type3, "TryCancel");
				MP.RegisterSyncMethod(type3, "TryGiveLoadSlotJob");
				MP.RegisterSyncMethod(type3, "TryEmptySlot");
				MpCompat.RegisterLambdaMethod("CompSlotLoadable.SloatLoadbleFloatMenuPatch", "GetFloatMenus", 1);
				num++;
			}
			Type type4 = AccessTools.TypeByName("CompToggleDef.ToggleDefCardUtility");
			if (type4 != null)
			{
				MP.RegisterSyncMethod(type4, "SwapThing").SetContext(SyncContext.MapSelected);
				num++;
			}
			if (Prefs.DevMode)
			{
				Log.Message($"Patched {num} out of {5} elements of JecsTools.");
			}
		}
	}

	private static bool PatchAbilities()
	{
		Type type = AccessTools.TypeByName("AbilityUser.PawnAbility");
		if (type == null)
		{
			return false;
		}
		pawnAbilityUserField = AccessTools.FieldRefAccess<ThingComp>(type, "abilityUser");
		MP.RegisterSyncMethod(type, "UseAbility").SetPostInvoke(StopTargeting);
		MP.RegisterSyncWorker<object>(SyncPawnAbility, type);
		compAbilityUserAbilityDataField = AccessTools.FieldRefAccess<object>("AbilityUser.CompAbilityUser:abilityData");
		abilityDataAllPowersGetter = MethodInvoker.GetHandler(AccessTools.PropertyGetter("AbilityUser.AbilityData:AllPowers"));
		abilityUserUtilityGetCompsMethod = MethodInvoker.GetHandler(AccessTools.Method("AbilityUser.AbilityUserUtility:GetCompAbilityUsers"));
		return true;
	}

	private static void StopTargeting(object instance, object[] args)
	{
		if (MP.IsExecutingSyncCommandIssuedBySelf)
		{
			Find.Targeter.StopTargeting();
		}
	}

	private static void SyncPawnAbility(SyncWorker sync, ref object ability)
	{
		if (sync.isWriting)
		{
			ThingComp thingComp = pawnAbilityUserField(ability);
			ThingComp[] array = ((IEnumerable)abilityUserUtilityGetCompsMethod(null, thingComp.parent)).Cast<ThingComp>().ToArray();
			bool flag = false;
			for (int i = 0; i < array.Length; i++)
			{
				ThingComp instance = array[i];
				object target = compAbilityUserAbilityDataField(instance);
				int num = ((IList)abilityDataAllPowersGetter(target)).IndexOf(ability);
				if (num >= 0)
				{
					flag = true;
					sync.Write(i);
					sync.Write(num);
					sync.Write(thingComp.parent);
					break;
				}
			}
			if (!flag)
			{
				sync.Write(-1);
			}
		}
		else
		{
			int num2 = sync.Read<int>();
			if (num2 >= 0)
			{
				int index = sync.Read<int>();
				Pawn pawn = sync.Read<Pawn>();
				ThingComp instance2 = ((IEnumerable)abilityUserUtilityGetCompsMethod(null, pawn)).Cast<ThingComp>().ToArray()[num2];
				object target2 = compAbilityUserAbilityDataField(instance2);
				IList list = (IList)abilityDataAllPowersGetter(target2);
				ability = list[index];
			}
		}
	}
}
