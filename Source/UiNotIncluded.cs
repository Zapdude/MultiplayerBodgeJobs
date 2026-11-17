using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("GonDragon.UINotIncluded")]
public class UiNotIncluded
{
	private static bool needToListenToHotkeys;

	private static FastInvokeHandler doTimeControlsHotkeysMethod;

	private static Type timespeedConfigType;

	private static AccessTools.FieldRef<bool> vanillaControlSpeedField;

	private static AccessTools.FieldRef<IList> topBarField;

	private static AccessTools.FieldRef<IList> bottomBarField;

	public UiNotIncluded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		string errorType = "time control hotkeys";
		MethodInfo methodInfo = AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.AsyncTime.TimeControlPatch"), "DoTimeControlsHotkeys");
		Type type = AccessTools.TypeByName("UINotIncluded.Widget.Configs.TimespeedConfig");
		Type type2 = AccessTools.TypeByName("UINotIncluded.Settings");
		FieldInfo fieldInfo = AccessTools.DeclaredField(type2, "vanillaControlSpeed");
		FieldInfo fieldInfo2 = AccessTools.DeclaredField(type2, "topBar");
		FieldInfo fieldInfo3 = AccessTools.DeclaredField(type2, "bottomBar");
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type2, "ExposeData");
		MethodInfo methodInfo3 = AccessTools.DeclaredMethod(AccessTools.TypeByName("UINotIncluded.UINI_Mod"), "DoSettingsWindowContents");
		if (methodInfo == null)
		{
			LogError("MP's DoTimeControlsHotkeys method doesn't exist.");
		}
		else if (!methodInfo.IsStatic)
		{
			LogError("MP's DoTimeControlsHotkeys method is not static.");
		}
		else if (methodInfo.ReturnType != typeof(void))
		{
			LogError("MP's DoTimeControlsHotkeys method does not have a void return type.");
		}
		else
		{
			ParameterInfo[] parameters = methodInfo.GetParameters();
			if (parameters == null || parameters.Length != 0)
			{
				LogError("MP's DoTimeControlsHotkeys method has incorrect number of arguments.");
			}
			else if (type == null)
			{
				LogError("TimespeedConfig type is null.");
			}
			else if (fieldInfo == null)
			{
				LogError("vanillaControlSpeed field is null");
			}
			else if (!fieldInfo.IsStatic)
			{
				LogError("vanillaControlSpeed field is not static.");
			}
			else if (fieldInfo.FieldType != typeof(bool))
			{
				LogError("vanillaControlSpeed field is not of type bool.");
			}
			else if (fieldInfo2 == null)
			{
				LogError("topBar field is null.");
			}
			else if (!fieldInfo2.IsStatic)
			{
				LogError("topBar field is not static.");
			}
			else if (!typeof(IList).IsAssignableFrom(fieldInfo2.FieldType))
			{
				LogError("topBar field is not of type IList.");
			}
			else if (fieldInfo3 == null)
			{
				LogError("bottomBar field is null.");
			}
			else if (!fieldInfo3.IsStatic)
			{
				LogError("bottomBar field is not static.");
			}
			else if (!typeof(IList).IsAssignableFrom(fieldInfo3.FieldType))
			{
				LogError("bo$ttomBar field is not of type IList.");
			}
			else if (methodInfo2 == null)
			{
				LogError("ExposeData method is null.");
			}
			else if (methodInfo3 == null)
			{
				LogError("DoSettingsWindowContents method is null.");
			}
			else
			{
				doTimeControlsHotkeysMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
				timespeedConfigType = type;
				vanillaControlSpeedField = AccessTools.StaticFieldRefAccess<bool>(fieldInfo);
				topBarField = AccessTools.StaticFieldRefAccess<IList>(fieldInfo2);
				bottomBarField = AccessTools.StaticFieldRefAccess<IList>(fieldInfo3);
				MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GlobalControlsUtility), "DoTimespeedControls"), new HarmonyMethod(new Action(PreDoTimespeedControls)));
				MpCompat.harmony.Patch(methodInfo3, new HarmonyMethod(new Action(PostPotentialSettingsChange)));
				MpCompat.harmony.Patch(methodInfo2, new HarmonyMethod(new Action(PostPotentialSettingsChange)));
			}
		}
		errorType = "time control widget";
		FieldInfo fieldInfo4 = AccessTools.DeclaredField(AccessTools.TypeByName("UINotIncluded.Widget.Workers.Timespeed_Worker"), "cached_DoTimeControlsGUI");
		MethodInfo methodInfo4 = AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.AsyncTime.TimeControlPatch"), "DoTimeControlsGUI");
		if (fieldInfo4 == null)
		{
			LogError("cached time control field doesn't exist.");
			return;
		}
		if (!fieldInfo4.IsStatic)
		{
			LogError("cached time control field is not static.");
			return;
		}
		if (fieldInfo4.FieldType != typeof(Action<Rect>))
		{
			LogError("cached time control field type is not Action<Rect>.");
			return;
		}
		if (methodInfo4 == null)
		{
			LogError("MP's DoTimeControlsGUI method doesn't exist.");
			return;
		}
		if (!methodInfo4.IsStatic)
		{
			LogError("MP's DoTimeControlsGUI method is not static.");
			return;
		}
		if (methodInfo4.ReturnType != typeof(void))
		{
			LogError("MP's DoTimeControlsGUI method does not have a void return type.");
			return;
		}
		ParameterInfo[] parameters2 = methodInfo4.GetParameters();
		if (parameters2 == null || parameters2.Length != 1)
		{
			LogError("MP's DoTimeControlsGUI method has incorrect number of arguments.");
		}
		else if (parameters2[0].ParameterType != typeof(Rect))
		{
			LogError("MP's DoTimeControlsGUI method argument is not of type Rect.");
		}
		else
		{
			fieldInfo4.SetValue(null, Delegate.CreateDelegate(typeof(Action<Rect>), methodInfo4));
		}
		void LogError(string error)
		{
			Log.Error("Patching UI Not Included failed (" + errorType + "): " + error);
		}
	}

	private static void PreDoTimespeedControls()
	{
		if (needToListenToHotkeys)
		{
			doTimeControlsHotkeysMethod(null);
		}
	}

	private static void PostPotentialSettingsChange()
	{
		if (vanillaControlSpeedField())
		{
			needToListenToHotkeys = false;
			return;
		}
		IList[] array = new IList[2]
		{
			topBarField(),
			bottomBarField()
		};
		foreach (IList list in array)
		{
			if (list == null)
			{
				continue;
			}
			foreach (object item in list)
			{
				if (timespeedConfigType.IsInstanceOfType(item))
				{
					needToListenToHotkeys = false;
					return;
				}
			}
		}
		needToListenToHotkeys = true;
	}
}
