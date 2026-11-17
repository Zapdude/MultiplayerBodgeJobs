using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("seohyeon.onlyIfToleranceBelow")]
internal class OnlyIfToleranceBelow
{
	private static ISyncField toleranceSyncField;

	public OnlyIfToleranceBelow(ModContentPack mod)
	{
		MethodInfo methodInfo = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Sync"), "Field", new Type[3]
		{
			typeof(Type),
			typeof(string),
			typeof(string)
		});
		FieldInfo fieldInfo = typeof(DrugPolicyEntry).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).First((FieldInfo f) => f.Name.Contains("OnlyIfToleranceBelow"));
		toleranceSyncField = (ISyncField)methodInfo.Invoke(null, new object[3]
		{
			typeof(DrugPolicy),
			"entriesInt/[]",
			fieldInfo.Name
		});
		toleranceSyncField.SetBufferChanges();
		MpCompat.harmony.Patch(AccessTools.Method(typeof(Dialog_ManageDrugPolicies), "DoPolicyConfigArea"), new HarmonyMethod(typeof(OnlyIfToleranceBelow), "PreDoPolicy"), new HarmonyMethod(typeof(OnlyIfToleranceBelow), "PostDoPolicy"));
	}

	private static void PreDoPolicy(Dialog_ManageDrugPolicies __instance)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			DrugPolicy selectedPolicy = ((Dialog_ManagePolicies<DrugPolicy>)__instance).SelectedPolicy;
			for (int i = 0; i < selectedPolicy.Count; i++)
			{
				toleranceSyncField.Watch(selectedPolicy, i);
			}
		}
	}

	private static void PostDoPolicy(Dialog_ManageDrugPolicies __instance)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}
}
