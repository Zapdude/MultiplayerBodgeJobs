using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Pirates")]
internal class VanillaFactionsPirates
{
	private static Type warcasketDialogType;

	private static MethodInfo notifySettingsChangedMethod;

	private static AccessTools.FieldRef<object, object> warcasketProjectField;

	private static AccessTools.FieldRef<object, Color> helmetColorField;

	private static AccessTools.FieldRef<object, Color> shoulderPadsColorField;

	private static AccessTools.FieldRef<object, Color> armorColorField;

	private static AccessTools.FieldRef<GameComponent> curseManagerInstanceField;

	private static AccessTools.FieldRef<object, IEnumerable> activeCurseDefsField;

	private static MethodInfo removeCurseMethod;

	private static MethodInfo addCurseMethod;

	private static MethodInfo curseDefWorkerGetter;

	private static MethodInfo curseWorkerDisactivateMethod;

	private static MethodInfo curseWorkerStartMethod;

	public VanillaFactionsPirates(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("VFEPirates.Ability_SiegeMode", "GetGizmo", 0, 2);
		MP.RegisterSyncMethod(AccessTools.Method("VFEPirates.Verb_DroneDeployment:ReleaseDrones"));
		MP.RegisterSyncMethod(typeof(VanillaFactionsPirates), "SyncedShieldDetonation");
		MpCompat.harmony.Patch(AccessTools.Method("VFEPirates.Verb_ShieldDetonation:TryCastShot"), new HarmonyMethod(typeof(VanillaFactionsPirates), "PreShieldDetonation"));
		warcasketDialogType = AccessTools.TypeByName("VFEPirates.Buildings.Dialog_WarcasketCustomization");
		notifySettingsChangedMethod = AccessTools.Method(warcasketDialogType, "Notify_SettingsChanged");
		warcasketProjectField = AccessTools.FieldRefAccess<object>(warcasketDialogType, "project");
		MP.RegisterSyncWorker<Window>(SyncWarcasketCustomizationDialog, warcasketDialogType);
		MP.RegisterSyncMethod(warcasketDialogType, "OnAcceptKeyPressed");
		MP.RegisterSyncMethod(warcasketDialogType, "OnCancelKeyPressed");
		MpCompat.RegisterLambdaMethod(warcasketDialogType, "DoWindowContents", 0, 1, 2);
		MpCompat.harmony.Patch(AccessTools.Method(warcasketDialogType, "DoWindowContents"), new HarmonyMethod(typeof(VanillaFactionsPirates), "PreDoWindowContents"), new HarmonyMethod(typeof(VanillaFactionsPirates), "PostDoWindowContents"));
		Type type = AccessTools.TypeByName("VFEPirates.WarcasketProject");
		helmetColorField = AccessTools.FieldRefAccess<Color>(type, "colorHelmet");
		shoulderPadsColorField = AccessTools.FieldRefAccess<Color>(type, "colorShoulderPads");
		armorColorField = AccessTools.FieldRefAccess<Color>(type, "colorArmor");
		MP.RegisterSyncMethod(typeof(VanillaFactionsPirates), "SyncedSetColors");
		DialogUtilities.RegisterDialogCloseSync(warcasketDialogType, addPauseLock: true);
		MP.RegisterSyncMethod(typeof(VanillaFactionsPirates), "SyncedSetCurse");
		MpCompat.harmony.Patch(AccessTools.Method("VFEPirates.Page_ChooseCurses:DoCurse"), null, null, new HarmonyMethod(typeof(VanillaFactionsPirates), "ReplaceButton"));
		Type type2 = AccessTools.TypeByName("VFEPirates.GameComponent_CurseManager");
		curseManagerInstanceField = AccessTools.StaticFieldRefAccess<GameComponent>(AccessTools.Field(type2, "Instance"));
		activeCurseDefsField = AccessTools.FieldRefAccess<IEnumerable>(type2, "activeCurseDefs");
		removeCurseMethod = AccessTools.Method(type2, "Remove");
		addCurseMethod = AccessTools.Method(type2, "Add");
		curseDefWorkerGetter = AccessTools.PropertyGetter(AccessTools.TypeByName("VFEPirates.CurseDef"), "Worker");
		Type type3 = AccessTools.TypeByName("VFEPirates.CurseWorker");
		curseWorkerDisactivateMethod = AccessTools.Method(type3, "Disactivate");
		curseWorkerStartMethod = AccessTools.Method(type3, "Start");
		PatchingUtilities.PatchPushPopRand("VFEPirates.IncomingSmoker:ThrowBlackSmoke");
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			PatchingUtilities.SetupAsyncTime();
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VFEPirates.CommandAbilityToggle:GizmoOnGUIInt"), new HarmonyMethod(MpMethodUtil.MethodOf(VanillaExpandedFramework.PreAbilityGizmoGui)), null, null, new HarmonyMethod(MpMethodUtil.MethodOf(new Action<PatchingUtilities.TimeSnapshot?>(VanillaExpandedFramework.RestoreProperTimeSnapshot))));
		});
	}

	private static void PreDoWindowContents(Window __instance, ref Color[] __state)
	{
		if (MP.IsInMultiplayer)
		{
			object instance = warcasketProjectField(__instance);
			__state = new Color[3]
			{
				helmetColorField(instance),
				shoulderPadsColorField(instance),
				armorColorField(instance)
			};
		}
	}

	private static void PostDoWindowContents(Window __instance, Color[] __state)
	{
		if (MP.IsInMultiplayer)
		{
			object instance = warcasketProjectField(__instance);
			Color color = helmetColorField(instance);
			Color color2 = shoulderPadsColorField(instance);
			Color color3 = armorColorField(instance);
			if (__state[0] != color || __state[1] != color2 || __state[2] != color3)
			{
				helmetColorField(instance) = __state[0];
				shoulderPadsColorField(instance) = __state[1];
				armorColorField(instance) = __state[2];
				SyncedSetColors(color, color2, color3);
			}
		}
	}

	private static void SyncedSetColors(Color helmetColor, Color shoulderPadsColor, Color armorColor)
	{
		Window window = Find.WindowStack.Windows.FirstOrDefault((Window w) => w.GetType() == warcasketDialogType);
		if (window == null)
		{
			Log.Error("Couldn't find the warcasket customization dialog while trying to sync it");
			return;
		}
		object instance = warcasketProjectField(window);
		helmetColorField(instance) = helmetColor;
		shoulderPadsColorField(instance) = shoulderPadsColor;
		armorColorField(instance) = armorColor;
		notifySettingsChangedMethod.Invoke(window, Array.Empty<object>());
	}

	private static void SyncWarcasketCustomizationDialog(SyncWorker sync, ref Window dialog)
	{
		if (!sync.isWriting)
		{
			dialog = Find.WindowStack.Windows.FirstOrDefault((Window w) => w.GetType() == warcasketDialogType);
		}
	}

	private static void SyncedSetCurse(Def curseDef)
	{
		GameComponent gameComponent = curseManagerInstanceField();
		IEnumerable obj = activeCurseDefsField(gameComponent);
		object obj2 = curseDefWorkerGetter.Invoke(curseDef, Array.Empty<object>());
		bool flag = false;
		foreach (object item in obj)
		{
			if (item == curseDef)
			{
				flag = true;
				break;
			}
		}
		if (flag)
		{
			removeCurseMethod.Invoke(gameComponent, new object[1] { curseDef });
			curseWorkerDisactivateMethod.Invoke(obj2, Array.Empty<object>());
		}
		else
		{
			addCurseMethod.Invoke(gameComponent, new object[1] { curseDef });
			curseWorkerStartMethod.Invoke(obj2, Array.Empty<object>());
		}
	}

	private static bool InjectedButton(Rect rect, bool doMouseoverSound, Def curseDef)
	{
		if (Widgets.ButtonInvisible(rect, doMouseoverSound))
		{
			if (!MP.IsInMultiplayer)
			{
				return true;
			}
			SyncedSetCurse(curseDef);
		}
		return false;
	}

	private static IEnumerable<CodeInstruction> ReplaceButton(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo method = AccessTools.Method(typeof(Widgets), "ButtonInvisible");
		bool anythingPatched = false;
		foreach (CodeInstruction ci in instr)
		{
			if (ci.opcode == OpCodes.Call && ci.operand is MethodInfo methodInfo && methodInfo == method)
			{
				yield return new CodeInstruction(OpCodes.Ldarg_2);
				ci.operand = AccessTools.Method(typeof(VanillaFactionsPirates), "InjectedButton");
				anythingPatched = true;
			}
			yield return ci;
		}
		if (!anythingPatched)
		{
			Log.Warning("Failed to patch Vanilla Factions Pirates - curse page");
		}
	}

	private static bool PreShieldDetonation(Verb __instance, ref bool __result)
	{
		if (!MP.InInterface)
		{
			return true;
		}
		SyncedShieldDetonation((ThingComp)__instance.DirectOwner, __instance.loadID);
		__result = true;
		return false;
	}

	private static void SyncedShieldDetonation(ThingComp verbGiverComp, string loadId)
	{
		((IVerbOwner)verbGiverComp).VerbTracker.AllVerbs.Find((Verb ve) => ve.loadID == loadId).TryCastShot();
	}
}
