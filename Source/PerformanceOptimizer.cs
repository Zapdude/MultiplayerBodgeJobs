using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Taranchuk.PerformanceOptimizer")]
public class PerformanceOptimizer
{
	private static FastInvokeHandler refreshCache;

	private static bool isAutoSaving = false;

	private static readonly Dictionary<IDictionary, IDictionary> SimulationToInterfaceCaches = new Dictionary<IDictionary, IDictionary>();

	private static FastInvokeHandler doTimeControlsHotkeysMethod;

	public PerformanceOptimizer(ModContentPack mod)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod("Multiplayer.Client.AsyncTime.TimeControlPatch:DoTimeControlsHotkeys");
		if (methodInfo != null)
		{
			doTimeControlsHotkeysMethod = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod("PerformanceOptimizer.Optimization_DoPlaySettings_DoTimespeedControls:DoTimeControlsGUI"), new HarmonyMethod(typeof(PerformanceOptimizer), "PreDoTimeControlsGUI"));
		}
		else
		{
			Log.Error("Could not find TimeControlPatch:DoTimeControlsHotkeys, speed control hot keys won't work with disabled/hidden speed control UI.");
		}
		MethodInfo methodInfo2 = AccessTools.DeclaredMethod("PerformanceOptimizer.PerformanceOptimizerMod:ResetStaticData");
		MpCompat.harmony.Patch(methodInfo2, new HarmonyMethod(typeof(PerformanceOptimizer), "CancelIfAutosaving"));
		refreshCache = HarmonyLib.MethodInvoker.GetHandler(methodInfo2);
		MethodInfo methodInfo3 = AccessTools.DeclaredMethod("Multiplayer.Client.Autosaving:SaveGameToFile_Overwrite");
		if ((object)methodInfo3 == null)
		{
			methodInfo3 = AccessTools.DeclaredMethod("Multiplayer.Client.MultiplayerSession:SaveGameToFile_Overwrite");
		}
		if ((object)methodInfo3 == null)
		{
			methodInfo3 = AccessTools.DeclaredMethod("Multiplayer.Client.MultiplayerSession:SaveGameToFile");
		}
		if (methodInfo3 != null)
		{
			MpCompat.harmony.Patch(methodInfo3, new HarmonyMethod(typeof(PerformanceOptimizer), "PreSaveToFile"), new HarmonyMethod(typeof(PerformanceOptimizer), "PostSaveToFile"));
		}
		else
		{
			Log.Error("Couldn't find MP SaveGameToFile method, PerformanceOptimizer will now likely cause desyncs because the patch failed.");
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(PerformanceOptimizer), "RefreshCachePrefix"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("PerformanceOptimizer.PerformanceOptimizerSettings:Initialize"), null, new HarmonyMethod(typeof(PerformanceOptimizer), "PostPerformanceOptimizerInitialize"));
	}

	private static void PostPerformanceOptimizerInitialize()
	{
		HarmonyMethod transpiler = new HarmonyMethod(typeof(PerformanceOptimizer), "SeparateCachesTranspiler");
		if (AccessTools.DeclaredField("PerformanceOptimizer.PerformanceOptimizerSettings:throttles").GetValue(null) is IList list)
		{
			{
				foreach (object item in list)
				{
					Type type = item.GetType();
					var (text, text2) = type.Name switch
					{
						"Optimization_InspectGizmoGrid_DrawInspectGizmoGridFor" => ("GetGizmosFast", null), 
						"Optimization_Precept_RoleMulti_RecacheActivity" => ("Prefix", null), 
						"Optimization_Precept_RoleSingle_RecacheActivity" => ("Prefix", null), 
						"Optimization_Plant_TickLong" => ("Prefix", null), 
						"Optimization_JobGiver_ConfigurableHostilityResponse" => ("Prefix", null), 
						"Optimization_Building_Door_DoorRotationAt" => ("DoorRotationAtPrefix", "DoorRotationAtPostfix"), 
						_ => ("Prefix", "Postfix"), 
					};
					if (text != null)
					{
						MethodInfo methodInfo = AccessTools.DeclaredMethod(type, text);
						if (methodInfo != null)
						{
							MpCompat.harmony.Patch(methodInfo, null, null, transpiler);
						}
						else
						{
							Log.Error($"Type {type.FullName} is missing {text} method, patching failed.");
						}
					}
					if (text2 != null)
					{
						MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type, text2);
						if (methodInfo2 != null)
						{
							MpCompat.harmony.Patch(methodInfo2, null, null, transpiler);
							continue;
						}
						Log.Error($"Type {type.FullName} is missing {text2} method, patching failed.");
					}
				}
				return;
			}
		}
		Log.Error("PerformanceOptimizer.PerformanceOptimizerSettings:throttles was null or empty, the patch was likely called too early.");
	}

	private static bool CancelIfAutosaving()
	{
		if (MP.IsInMultiplayer && MP.IsHosting)
		{
			if (isAutoSaving)
			{
				return false;
			}
			ClearInterfaceCaches();
			return true;
		}
		isAutoSaving = false;
		ClearInterfaceCaches();
		return true;
	}

	private static void RefreshCachePrefix()
	{
		refreshCache(null);
	}

	private static void PreSaveToFile()
	{
		isAutoSaving = true;
	}

	private static void PostSaveToFile()
	{
		isAutoSaving = false;
	}

	private static void ClearInterfaceCaches()
	{
		foreach (IDictionary value in SimulationToInterfaceCaches.Values)
		{
			value.Clear();
		}
	}

	private static IDictionary ReturnCorrectCache(IDictionary simulationCache)
	{
		if (simulationCache == null)
		{
			return null;
		}
		if (!MP.InInterface)
		{
			return simulationCache;
		}
		if (SimulationToInterfaceCaches.TryGetValue(simulationCache, out var value))
		{
			return value;
		}
		Log.Warning($"Trying to get interface cache for dictionary of type: {simulationCache.GetType()}");
		value = Activator.CreateInstance(simulationCache.GetType()) as IDictionary;
		SimulationToInterfaceCaches[simulationCache] = value;
		return value;
	}

	private static IEnumerable<CodeInstruction> SeparateCachesTranspiler(IEnumerable<CodeInstruction> instr)
	{
		MethodInfo replacementCall = AccessTools.DeclaredMethod(typeof(PerformanceOptimizer), "ReturnCorrectCache");
		foreach (CodeInstruction ci in instr)
		{
			yield return ci;
			if (ci.opcode == OpCodes.Ldsfld && ci.operand is FieldInfo { IsStatic: not false, Name: "cachedResults" } fieldInfo && fieldInfo.GetValue(null) is IDictionary dictionary)
			{
				if (!SimulationToInterfaceCaches.ContainsKey(dictionary))
				{
					SimulationToInterfaceCaches[dictionary] = Activator.CreateInstance(dictionary.GetType()) as IDictionary;
				}
				yield return new CodeInstruction(OpCodes.Call, replacementCall);
			}
		}
	}

	private static bool PreDoTimeControlsGUI()
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		doTimeControlsHotkeysMethod(null);
		return false;
	}
}
