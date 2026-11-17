using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("Dubwise.Rimatomics")]
public class Rimatomics
{
	private struct ConsoleCleanupState
	{
		internal bool Active;

		internal Pawn Previous;
	}

	internal sealed class SessionSettingsSnapshot : IExposable
	{
		internal int[] Indices = new int[4] { 4, 4, 4, 4 };

		internal bool MannedReactor = true;

		internal bool EnableGiblets = true;

		public void ExposeData()
		{
			for (int i = 0; i < Indices.Length; i++)
			{
				Scribe_Values.Look(ref Indices[i], SettingsIndices[i], 4);
			}
			Scribe_Values.Look(ref MannedReactor, "MannedReactor", defaultValue: true);
			Scribe_Values.Look(ref EnableGiblets, "EnableGiblets", defaultValue: true);
		}

		internal bool SameAs(SessionSettingsSnapshot other)
		{
			if (other != null && Indices.SequenceEqual(other.Indices) && MannedReactor == other.MannedReactor)
			{
				return EnableGiblets == other.EnableGiblets;
			}
			return false;
		}
	}

	private sealed class FuelTabState : IDisposable
	{
		private readonly bool previousDrawing;

		private readonly object previousContext;

		private bool disposed;

		internal FuelTabState(Thing owner)
		{
			previousDrawing = (bool)thingFilterDrawing.GetValue(null);
			previousContext = thingFilterContext.GetValue(null);
			try
			{
				thingFilterContext.SetValue(null, null);
				MP.SetThingFilterContext(new RimatomicsFuelStorageContext(owner));
				thingFilterDrawing.SetValue(null, true);
			}
			catch
			{
				thingFilterDrawing.SetValue(null, previousDrawing);
				thingFilterContext.SetValue(null, previousContext);
				throw;
			}
		}

		public void Dispose()
		{
			if (!disposed)
			{
				disposed = true;
				thingFilterDrawing.SetValue(null, previousDrawing);
				thingFilterContext.SetValue(null, previousContext);
			}
		}
	}

	private sealed record RimatomicsFuelStorageContext(Thing Owner) : ThingFilterContext
	{
		public override ThingFilter Filter => (Owner as IStoreSettingsParent)?.GetStoreSettings()?.filter;

		public override ThingFilter ParentFilter => (Owner as IStoreSettingsParent)?.GetParentStoreSettings()?.filter;
	}

	private const string Ns = "Rimatomics.";

	[ThreadStatic]
	private static Pawn consoleJobBeingCleaned;

	private static readonly string[] SettingsIndices = new string[4] { "FuelBurnRateInt", "PulseSizeScalingInt", "RadiationStrengthInt", "RimatomicsTraderCooldownInt" };

	private static readonly string[] SettingsDefinitions = new string[4] { "FuelBurnRate", "PulseSizeScaling", "RadiationStrength", "RimatomicsTraderCooldown" };

	private static AccessTools.FieldRef<object, int> researchCheckTicks;

	private static AccessTools.FieldRef<object, int> projectPrice;

	private static AccessTools.FieldRef<object, bool> researchNotify;

	private static AccessTools.FieldRef<object, object> researchPurchased;

	private static AccessTools.FieldRef<object, object> researchCompleted;

	private static AccessTools.FieldRef<object, object> researchProgress;

	private static AccessTools.FieldRef<object, GlobalTargetInfo> launchTarget;

	private static Action<object> checkResearchBenches;

	private static Func<object> simulationGame;

	private static Func<object, int> simulationOwnerTicks;

	private static AccessTools.FieldRef<object, object> simulationWorld;

	private static FieldInfo simulationTickingMap;

	private static MethodInfo simulationMapGetter;

	private static MethodInfo simulationMapPre;

	private static MethodInfo simulationMapPost;

	private static MethodInfo simulationWorldPre;

	private static MethodInfo simulationWorldPost;

	private static FieldInfo removePipeMode;

	private static FieldInfo thingFilterDrawing;

	private static FieldInfo thingFilterContext;

	private static MethodInfo allCamoCallback;

	private static FieldInfo allCamoOwner;

	private static FieldInfo allCamoMode;

	private static bool Intercept
	{
		get
		{
			if (MP.IsInMultiplayer && MP.InInterface)
			{
				return !MP.IsExecutingSyncCommand;
			}
			return false;
		}
	}

	private static object SessionSettings
	{
		get
		{
			Type type = AccessTools.TypeByName("Rimatomics.RimatomicsMod");
			if (!(type == null))
			{
				return PatchingUtilities.Get(type, "Settings");
			}
			return null;
		}
	}

	internal static bool HasSessionSettings => SessionSettings != null;

	public Rimatomics(ModContentPack mod)
	{
		PatchingUtilities.SetupAsyncTime();
		InstallSimulation();
		InstallSettings();
		InstallUI();
		InstallConsoleLifecycle();
		InstallTranslation();
	}

	private static Type TypeOf(string name)
	{
		return AccessTools.TypeByName("Rimatomics." + name) ?? throw new TypeLoadException("Rimatomics." + name);
	}

	private static void Patch(string type, string method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		MethodInfo original = AccessTools.DeclaredMethod(TypeOf(type), method) ?? throw new MissingMethodException("Rimatomics." + type, method);
		MpCompat.harmony.Patch(original, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(Rimatomics), name);
			}
			return null;
		}
	}

	private static void InstallConsoleLifecycle()
	{
		Patch("ReactorControl", "TryCloseTerminal", "BeforeConsoleJobCleanup", null, null, "AfterConsoleJobCleanup");
		Patch("ReactorControl", "EndJob", "EndReactorJobPrefix");
	}

	private static void BeforeConsoleJobCleanup(object __instance, out ConsoleCleanupState __state)
	{
		__state = default;
		if (MP.IsInMultiplayer)
		{
			object obj = PatchingUtilities.Get(__instance, "console");
			if (obj != null && PatchingUtilities.Get(obj, "neg") is Pawn pawn)
			{
				__state.Active = true;
				__state.Previous = consoleJobBeingCleaned;
				consoleJobBeingCleaned = pawn;
			}
		}
	}

	private static Exception AfterConsoleJobCleanup(Exception __exception, ConsoleCleanupState __state)
	{
		if (__state.Active)
		{
			consoleJobBeingCleaned = __state.Previous;
		}
		return __exception;
	}

	private static bool EndReactorJobPrefix(Pawn neg)
	{
		if (MP.IsInMultiplayer && neg != null)
		{
			return neg != consoleJobBeingCleaned;
		}
		return true;
	}

	private static void InstallSettings()
	{
		MP.RegisterSyncMethod(typeof(Rimatomics), "SyncRimatomicsSettings");
		Patch("RimatomicsMod", "DoSettingsWindowContents", "BeforeSettings", null, null, "AfterSettings");
	}

	internal static SessionSettingsSnapshot CaptureSessionSettings()
	{
		object settings = SessionSettings;
		if (settings == null)
		{
			return null;
		}
		return new SessionSettingsSnapshot
		{
			Indices = SettingsIndices.Select((string name) => (int)PatchingUtilities.Get(settings, name)).ToArray(),
			MannedReactor = (bool)PatchingUtilities.Get(settings, "MannedReactor"),
			EnableGiblets = (bool)PatchingUtilities.Get(settings, "EnableGiblets")
		};
	}

	internal static void ApplySessionSettings(SessionSettingsSnapshot value)
	{
		object sessionSettings = SessionSettings;
		if (sessionSettings != null && value != null)
		{
			for (int i = 0; i < SettingsIndices.Length; i++)
			{
				PatchingUtilities.Set(sessionSettings, SettingsIndices[i], value.Indices[i]);
			}
			PatchingUtilities.Set(sessionSettings, "MannedReactor", value.MannedReactor);
			PatchingUtilities.Set(sessionSettings, "EnableGiblets", value.EnableGiblets);
		}
	}

	private static void BeforeSettings(out SessionSettingsSnapshot __state)
	{
		__state = (MP.IsInMultiplayer ? CaptureSessionSettings() : null);
	}

	private static Exception AfterSettings(Exception __exception, SessionSettingsSnapshot __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		SessionSettingsSnapshot sessionSettingsSnapshot = CaptureSessionSettings();
		ApplySessionSettings(__state);
		if (__exception == null && !sessionSettingsSnapshot.SameAs(__state))
		{
			SyncRimatomicsSettings(sessionSettingsSnapshot.Indices, sessionSettingsSnapshot.MannedReactor, sessionSettingsSnapshot.EnableGiblets);
		}
		return __exception;
	}

	private static void SyncRimatomicsSettings(int[] indices, bool mannedReactor, bool enableGiblets)
	{
		if (indices?.Length != SettingsIndices.Length || SessionSettings == null)
		{
			return;
		}
		for (int i = 0; i < indices.Length; i++)
		{
			object obj = PatchingUtilities.Get(TypeOf("DubDef"), SettingsDefinitions[i]);
			if (obj == null || !(PatchingUtilities.Get(obj, "options") is IList list) || indices[i] < 0 || indices[i] >= list.Count)
			{
				return;
			}
		}
		ApplySessionSettings(new SessionSettingsSnapshot
		{
			Indices = (int[])indices.Clone(),
			MannedReactor = mannedReactor,
			EnableGiblets = enableGiblets
		});
	}

	private static void InstallSimulation()
	{
		Type type = TypeOf("GameComp_RimatomicsResearch");
		researchCheckTicks = AccessTools.FieldRefAccess<int>(type, "tickTillCheck");
		researchNotify = AccessTools.FieldRefAccess<bool>(type, "notify");
		researchPurchased = AccessTools.FieldRefAccess<object>(type, "Purchased");
		researchCompleted = AccessTools.FieldRefAccess<object>(type, "StepCompleted");
		researchProgress = AccessTools.FieldRefAccess<object>(type, "StepProgress");
		projectPrice = AccessTools.FieldRefAccess<int>(TypeOf("RimatomicResearchDef"), "price");
		launchTarget = AccessTools.FieldRefAccess<GlobalTargetInfo>(TypeOf("Building_LaunchPad"), "<Target>k__BackingField");
		MethodInfo meth = AccessTools.DeclaredMethod(type, "CheckAllStepsAllBenches") ?? throw new MissingMethodException(type.FullName, "CheckAllStepsAllBenches");
		DynamicMethod dynamicMethod = new DynamicMethod("Rimatomics_CheckResearchBenches", typeof(void), new Type[1] { typeof(object) }, typeof(Rimatomics), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Castclass, type);
		iLGenerator.Emit(OpCodes.Call, meth);
		iLGenerator.Emit(OpCodes.Ret);
		checkResearchBenches = (Action<object>)dynamicMethod.CreateDelegate(typeof(Action<object>));
		InitializeSimulationScopes();
		Patch("GameComp_RimatomicsResearch", "WorldComponentUpdate", "ResearchFramePrefix");
		Patch("RimatomicsResearch", "WorldComponentTick", null, "ResearchTickPostfix");
		Patch("GameComp_RimatomicsResearch", "ExposeData", null, "ResearchSavePostfix");
		Patch("GameComp_RimatomicsResearch", "IsPurchased", "PurchasedReadPrefix");
		Patch("GameComp_RimatomicsResearch", "IsCompleted", "CompletedReadPrefix");
		Patch("GameComp_RimatomicsResearch", "GetProgress", "ProgressReadPrefix");
		Patch("Building_LaunchPad", "ExposeData", null, "LaunchTargetSavePostfix");
		Patch("Building_Marauder", "DrawAt", "DrawRandPrefix", null, null, "DrawRandFinalizer");
		Patch("RimatomicsResearch", "DonkeyRubarb", null, null, "WorldTicksTranspiler");
		Patch("RimatomicsResearch", "ForceRaids", null, null, "WorldTicksTranspiler");
		Patch("RimatomicsResearch", "IncidentQueueTick", null, null, "WorldTicksTranspiler");
		Patch("CreudMinxident", "ReportThreat", null, null, "WorldTicksTranspiler");
		Patch("RimatomicsResearch", "SpawnTradeShip", "TradeShipMapPrefix", null, null, "SimulationScopeFinalizer");
		Patch("WorldObject_Sabot", "Arrived", "SabotMapPrefix", null, null, "SimulationScopeFinalizer");
		Patch("WorldObject_ICBMfission", "DropWarheadInMap", "WarheadMapPrefix", null, null, "SimulationScopeFinalizer");
		Patch("NuclearStrike", "StartStrike", null, null, "NuclearLongEventsTranspiler");
		Patch("WorldObject_Fallout", "Arrived", null, null, "FalloutConditionTranspiler");
	}

	private static bool ResearchFramePrefix()
	{
		return !MP.IsInMultiplayer;
	}

	private static void ResearchTickPostfix(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			researchCheckTicks(__instance)++;
			if (researchCheckTicks(__instance) >= 2000 || researchNotify(__instance))
			{
				researchCheckTicks(__instance) = 0;
				researchNotify(__instance) = false;
				checkResearchBenches(__instance);
			}
		}
	}

	private static void ResearchSavePostfix(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Scribe_Values.Look(ref researchCheckTicks(__instance), "mpResearchCheckTicks", 0);
			Scribe_Values.Look(ref researchNotify(__instance), "mpResearchNotify", defaultValue: false);
		}
	}

	private static void LaunchTargetSavePostfix(object __instance)
	{
		if (MP.IsInMultiplayer)
		{
			Scribe_TargetInfo.Look(ref launchTarget(__instance), "mpLaunchTarget");
		}
	}

	private static bool PurchasedReadPrefix(object __instance, object proj, ref bool __result)
	{
		if (!Intercept)
		{
			return true;
		}
		IDictionary dictionary = researchPurchased(__instance) as IDictionary;
		__result = projectPrice(proj) == 0 || (dictionary != null && dictionary.Contains(proj) && (bool)dictionary[proj]);
		return false;
	}

	private static bool CompletedReadPrefix(object __instance, object proj, ref bool __result)
	{
		if (!Intercept)
		{
			return true;
		}
		IDictionary dictionary = researchCompleted(__instance) as IDictionary;
		__result = dictionary != null && dictionary.Contains(proj) && (bool)dictionary[proj];
		return false;
	}

	private static bool ProgressReadPrefix(object __instance, object step, ref float __result)
	{
		if (!Intercept)
		{
			return true;
		}
		IDictionary dictionary = researchProgress(__instance) as IDictionary;
		__result = ((dictionary != null && dictionary.Contains(step)) ? ((float)dictionary[step]) : 0f);
		return false;
	}

	private static void DrawRandPrefix(out bool __state)
	{
		__state = MP.IsInMultiplayer;
		if (__state)
		{
			Rand.PushState();
		}
	}

	private static Exception DrawRandFinalizer(Exception __exception, bool __state)
	{
		if (__state)
		{
			Rand.PopState();
		}
		return __exception;
	}

	private static int ReadWorldTicks(TickManager manager)
	{
		if (!MP.IsInMultiplayer)
		{
			return manager.TicksGame;
		}
		return simulationOwnerTicks(WorldSimulationOwner());
	}

	private static IEnumerable<CodeInstruction> WorldTicksTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		int expected = __originalMethod.Name switch
		{
			"DonkeyRubarb" => 2, 
			"ForceRaids" => 1, 
			"IncidentQueueTick" => 3, 
			"ReportThreat" => 2, 
			_ => throw new InvalidOperationException("Unknown Rimatomics world clock target"), 
		};
		MethodInfo original = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(Rimatomics), "ReadWorldTicks");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count != expected)
		{
			throw new InvalidOperationException($"Rimatomics world clock changed in {__originalMethod.Name}: {count}, expected {expected}");
		}
	}

	private static Map MapAt(PlanetTile tile)
	{
		return Find.Maps.FirstOrDefault((Map map) => map.Tile == tile);
	}

	private static void TradeShipMapPrefix(out IDisposable __state)
	{
		object obj;
		if (MP.IsInMultiplayer)
		{
			Map anyPlayerHomeMap = Find.AnyPlayerHomeMap;
			if (anyPlayerHomeMap != null)
			{
				obj = SimulationMapScope(anyPlayerHomeMap);
				goto IL_001a;
			}
		}
		obj = null;
		goto IL_001a;
		IL_001a:
		__state = (IDisposable)obj;
	}

	private static void SabotMapPrefix(object __instance, out IDisposable __state)
	{
		object obj;
		if (MP.IsInMultiplayer)
		{
			Map map = MapAt((PlanetTile)PatchingUtilities.Get(__instance, "destinationTile"));
			if (map != null)
			{
				obj = SimulationMapScope(map);
				goto IL_002a;
			}
		}
		obj = null;
		goto IL_002a;
		IL_002a:
		__state = (IDisposable)obj;
	}

	private static void WarheadMapPrefix(object __instance, out IDisposable __state)
	{
		object obj;
		if (MP.IsInMultiplayer)
		{
			Map map = (PatchingUtilities.Get(__instance, "mapParent") as MapParent)?.Map;
			if (map != null)
			{
				obj = SimulationMapScope(map);
				goto IL_0031;
			}
		}
		obj = null;
		goto IL_0031;
		IL_0031:
		__state = (IDisposable)obj;
	}

	private static Exception SimulationScopeFinalizer(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static IEnumerable<CodeInstruction> NuclearLongEventsTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo queue = AccessTools.DeclaredMethod(typeof(LongEventHandler), "QueueLongEvent", new Type[7]
		{
			typeof(Action),
			typeof(string),
			typeof(bool),
			typeof(Action<Exception>),
			typeof(bool),
			typeof(bool),
			typeof(Action)
		});
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(Rimatomics), "QueueNuclearLongEvent");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(queue))
			{
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction.labels.AddRange(instruction.labels);
				instruction.labels.Clear();
				codeInstruction.blocks.AddRange(instruction.blocks);
				instruction.blocks.Clear();
				yield return codeInstruction;
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count != 11)
		{
			throw new InvalidOperationException("Rimatomics nuclear long-event contract changed: " + count);
		}
	}

	private static void QueueNuclearLongEvent(Action action, string textKey, bool doAsynchronously, Action<Exception> exceptionHandler, bool showExtraUIInfo, bool forceHideUI, Action callback, Thing strike)
	{
		Map map = strike.Map;
		if (MP.IsInMultiplayer && map != null)
		{
			Action original = action;
			action = () =>
			{
				using (SimulationMapScope(map))
				{
					original();
				}
			};
		}
		LongEventHandler.QueueLongEvent(action, textKey, doAsynchronously, exceptionHandler, showExtraUIInfo, forceHideUI, callback);
	}

	private static IEnumerable<CodeInstruction> FalloutConditionTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo maker = AccessTools.DeclaredMethod(typeof(GameConditionMaker), "MakeCondition", new Type[2]
		{
			typeof(GameConditionDef),
			typeof(int)
		});
		MethodInfo register = AccessTools.DeclaredMethod(typeof(GameConditionManager), "RegisterCondition");
		int made = 0;
		int registered = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			string replacement = null;
			if (instruction.Calls(maker))
			{
				replacement = "MakeFalloutCondition";
				made++;
			}
			else if (instruction.Calls(register))
			{
				replacement = "RegisterFalloutCondition";
				registered++;
			}
			if (replacement != null)
			{
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction.labels.AddRange(instruction.labels);
				instruction.labels.Clear();
				codeInstruction.blocks.AddRange(instruction.blocks);
				instruction.blocks.Clear();
				yield return codeInstruction;
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(Rimatomics), replacement);
			}
			yield return instruction;
		}
		if (made != 1 || registered != 1)
		{
			throw new InvalidOperationException($"Rimatomics fallout condition changed: {made}/{registered}");
		}
	}

	private static GameCondition MakeFalloutCondition(GameConditionDef def, int duration, WorldObject fallout)
	{
		if (MP.IsInMultiplayer)
		{
			Map map = MapAt(fallout.Tile);
			if (map != null)
			{
				using (SimulationMapScope(map))
				{
					return GameConditionMaker.MakeCondition(def, duration);
				}
			}
		}
		return GameConditionMaker.MakeCondition(def, duration);
	}

	private static void RegisterFalloutCondition(GameConditionManager manager, GameCondition condition, WorldObject fallout)
	{
		if (MP.IsInMultiplayer)
		{
			Map map = MapAt(fallout.Tile);
			if (map != null)
			{
				using (SimulationMapScope(map))
				{
					manager.RegisterCondition(condition);
					return;
				}
			}
		}
		manager.RegisterCondition(condition);
	}

	private static void InitializeSimulationScopes()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp") ?? throw new TypeLoadException("Multiplayer.Client.AsyncTimeComp");
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp") ?? throw new TypeLoadException("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
		FieldInfo field = AccessTools.DeclaredField("Multiplayer.Client.Multiplayer:game") ?? throw new MissingFieldException("Multiplayer:game");
		FieldInfo fieldInfo = AccessTools.DeclaredField("Multiplayer.Client.MultiplayerGame:asyncWorldTimeComp") ?? throw new MissingFieldException("MultiplayerGame:asyncWorldTimeComp");
		simulationWorld = AccessTools.FieldRefAccess<object>(fieldInfo.DeclaringType, fieldInfo.Name);
		simulationTickingMap = AccessTools.DeclaredField(type, "tickingMap") ?? throw new MissingFieldException(type.FullName, "tickingMap");
		simulationMapGetter = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime") ?? throw new MissingMethodException("Extensions:AsyncTime");
		simulationMapPre = AccessTools.DeclaredMethod(type, "PreContext") ?? throw new MissingMethodException(type.FullName, "PreContext");
		simulationMapPost = AccessTools.DeclaredMethod(type, "PostContext") ?? throw new MissingMethodException(type.FullName, "PostContext");
		simulationWorldPre = AccessTools.DeclaredMethod(type2, "PreContextForCallback") ?? throw new MissingMethodException(type2.FullName, "PreContextForCallback");
		simulationWorldPost = AccessTools.DeclaredMethod(type2, "PostContext") ?? throw new MissingMethodException(type2.FullName, "PostContext");
		DynamicMethod dynamicMethod = new DynamicMethod("Rimatomics_GetMultiplayerGame", typeof(object), Type.EmptyTypes, typeof(Rimatomics), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldsfld, field);
		iLGenerator.Emit(OpCodes.Ret);
		simulationGame = (Func<object>)dynamicMethod.CreateDelegate(typeof(Func<object>));
		MethodInfo methodInfo = AccessTools.DeclaredMethod("Multiplayer.Client.SimulationTimeContext:ReadTicks") ?? throw new MissingMethodException("SimulationTimeContext:ReadTicks");
		DynamicMethod dynamicMethod2 = new DynamicMethod("Rimatomics_ReadOwnerTicks", typeof(int), new Type[1] { typeof(object) }, typeof(Rimatomics), skipVisibility: true);
		ILGenerator iLGenerator2 = dynamicMethod2.GetILGenerator();
		iLGenerator2.Emit(OpCodes.Ldarg_0);
		iLGenerator2.Emit(OpCodes.Castclass, methodInfo.GetParameters()[0].ParameterType);
		iLGenerator2.Emit(OpCodes.Call, methodInfo);
		iLGenerator2.Emit(OpCodes.Ret);
		simulationOwnerTicks = (Func<object, int>)dynamicMethod2.CreateDelegate(typeof(Func<object, int>));
	}

	private static object WorldSimulationOwner()
	{
		object obj = simulationGame();
		return ((obj == null) ? null : simulationWorld(obj)) ?? throw new InvalidOperationException("Rimatomics requires an initialized Multiplayer world owner");
	}

	private static IDisposable SimulationMapScope(Map map)
	{
		if (map == null)
		{
			throw new ArgumentNullException("map");
		}
		if (MP.IsInMultiplayer)
		{
			return new PatchingUtilities.SimulationScope(simulationMapGetter.Invoke(null, new object[1] { map }), simulationMapPre, simulationMapPost, map, simulationTickingMap, "Rimatomics requires an initialized Multiplayer simulation owner");
		}
		return PatchingUtilities.EmptySimulationScope.Instance;
	}

	private static IDisposable SimulationWorldScope()
	{
		if (MP.IsInMultiplayer)
		{
			return new PatchingUtilities.SimulationScope(WorldSimulationOwner(), simulationWorldPre, simulationWorldPost, null, simulationTickingMap, "Rimatomics requires an initialized Multiplayer simulation owner");
		}
		return PatchingUtilities.EmptySimulationScope.Instance;
	}

	private static void InstallTranslation()
	{
		Patch("Dialog_ReactorConsole", "drawSlot", null, null, "FuelSlotTranslationTranspiler");
	}

	private static IEnumerable<CodeInstruction> FuelSlotTranslationTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo translate = AccessTools.DeclaredMethod(typeof(TranslatorFormattedStringExtensions), "Translate", new Type[6]
		{
			typeof(string),
			typeof(NamedArgument),
			typeof(NamedArgument),
			typeof(NamedArgument),
			typeof(NamedArgument),
			typeof(NamedArgument)
		});
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(Rimatomics), "TranslateFuelSlot");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(translate))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Rimatomics fuel-slot translation contract changed: " + count);
		}
	}

	private static string FuelSlotTemplate(string text)
	{
		if (text == null || !text.TrimEnd().EndsWith("{5}", StringComparison.Ordinal))
		{
			return text;
		}
		int num = text.LastIndexOf('\n');
		if (num < 0)
		{
			num = text.LastIndexOf("\\n", StringComparison.Ordinal);
		}
		if (num < 0)
		{
			return text;
		}
		string text2 = text.Substring(num);
		for (int i = 0; i < 5; i++)
		{
			if (text2.Contains("{" + i + "}", StringComparison.Ordinal))
			{
				return text;
			}
		}
		return text.Substring(0, num);
	}

	private static TaggedString TranslateFuelSlot(string key, NamedArgument estimatedLife, NamedArgument maxPower, NamedArgument currentPower, NamedArgument fuelLife, NamedArgument plutoniumYield)
	{
		TaggedString str = key.Translate();
		if (key == "SlotBreakdown")
		{
			str = FuelSlotTemplate(str.RawText);
		}
		return str.Formatted(estimatedLife, maxPower, currentPower, fuelLife, plutoniumYield);
	}

	private static void InstallUI()
	{
		string[] array = new string[4] { "SyncSiloControl", "SyncAllCamo", "SyncShieldRadius", "SyncFuelLife" };
		foreach (string methodOrPropertyName in array)
		{
			MP.RegisterSyncMethod(typeof(Rimatomics), methodOrPropertyName);
		}
		for (int j = 0; j < 3; j++)
		{
			MethodInfo lambda = MpMethodUtil.GetLambda(TypeOf("MissileSilo"), "GetGizmos", MethodType.Normal, null, j);
			MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(Rimatomics), j switch
			{
				1 => "LowerYieldPrefix", 
				0 => "CancelSiloPrefix", 
				_ => "HigherYieldPrefix", 
			}));
		}
		for (int k = 0; k < 2; k++)
		{
			MethodInfo lambda2 = MpMethodUtil.GetLambda(TypeOf("CompRimatomicsShield"), "CompGetGizmosExtra", MethodType.Normal, null, k);
			MpCompat.harmony.Patch(lambda2, new HarmonyMethod(typeof(Rimatomics), (k == 0) ? "IncreaseShieldPrefix" : "DecreaseShieldPrefix"));
		}
		allCamoCallback = MpMethodUtil.GetLambda(TypeOf("Command_SetCamoMode"), "ProcessInput", MethodType.Normal, null, 1);
		allCamoOwner = AccessTools.DeclaredField(allCamoCallback.DeclaringType, "<>4__this") ?? throw new MissingFieldException(allCamoCallback.DeclaringType?.FullName, "<>4__this");
		allCamoMode = AccessTools.DeclaredField(allCamoCallback.DeclaringType, "item") ?? throw new MissingFieldException(allCamoCallback.DeclaringType?.FullName, "item");
		MpCompat.harmony.Patch(allCamoCallback, new HarmonyMethod(typeof(Rimatomics), "AllCamoPrefix"));
		removePipeMode = AccessTools.DeclaredField(TypeOf("Designator_RemovePipe"), "RemovalMode") ?? throw new MissingFieldException("Rimatomics.Designator_RemovePipe", "RemovalMode");
		MP.RegisterSyncWorker<Designator>(SyncPipeRemovalDesignator, TypeOf("Designator_RemovePipe"), isImplicit: false, shouldConstruct: true);
		array = new string[3] { "reactorCore", "Building_storagePool", "Building_PlutoniumProc" };
		foreach (string text in array)
		{
			MethodInfo original = AccessTools.PropertySetter(TypeOf(text), "FuelLifeFilter") ?? throw new MissingMethodException("Rimatomics." + text, "set_FuelLifeFilter");
			MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(Rimatomics), "FuelLifePrefix"));
		}
		Type type = AccessTools.TypeByName("Multiplayer.Client.ThingFilterMarkers") ?? throw new TypeLoadException("Multiplayer.Client.ThingFilterMarkers");
		thingFilterDrawing = AccessTools.DeclaredField(type, "drawingThingFilter");
		thingFilterContext = AccessTools.DeclaredField(type, "thingFilterContext");
		if (thingFilterDrawing == null || thingFilterContext == null)
		{
			throw new MissingFieldException(type.FullName, "thing-filter context");
		}
		Patch("ITab_FuelStorage", "FillTab", "FuelTabPrefix", null, null, "FuelTabFinalizer");
		Patch("Building_RimatomicsResearchBench", "BuyProject", "BuyProjectPrefix", null, null, "BuyProjectFinalizer");
		MethodInfo original2 = AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(TypeOf("CompUpgradable"), "CompGetGizmosExtra"));
		MpCompat.harmony.Patch(original2, null, null, new HarmonyMethod(typeof(Rimatomics), "UpgradeReadTranspiler"));
	}

	private static bool CancelSiloPrefix(Thing __instance)
	{
		return SiloControlPrefix(__instance, 0);
	}

	private static bool LowerYieldPrefix(Thing __instance)
	{
		return SiloControlPrefix(__instance, 1);
	}

	private static bool HigherYieldPrefix(Thing __instance)
	{
		return SiloControlPrefix(__instance, 2);
	}

	private static bool SiloControlPrefix(Thing silo, int action)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncSiloControl(silo, action);
		return false;
	}

	private static void SyncSiloControl(Thing silo, int action)
	{
		if (silo != null && silo.Spawned && TypeOf("MissileSilo").IsInstanceOfType(silo))
		{
			switch (action)
			{
			case 0:
				PatchingUtilities.Set(silo, "launchPhase", Enum.ToObject(PatchingUtilities.Get(silo, "launchPhase").GetType(), 0));
				PatchingUtilities.Set(silo, "TickCounter", 0);
				Messages.Message("ICBMLaunchCancelled".Translate(), MessageTypeDefOf.RejectInput);
				break;
			case 1:
			case 2:
				PatchingUtilities.Set(silo, "WarheadYield", (action == 1) ? 55f : 250f);
				break;
			}
		}
	}

	private static bool AllCamoPrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncAllCamo(PatchingUtilities.Get(allCamoOwner.GetValue(__instance), "parent") as Thing, allCamoMode.GetValue(__instance) as string);
		return false;
	}

	private static void SyncAllCamo(Thing parent, string mode)
	{
		bool flag = parent == null || !parent.Spawned;
		if (!flag)
		{
			bool flag2;
			switch (mode)
			{
			case "base":
			case "woodland":
			case "desert":
			case "snow":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = !flag2;
		}
		if (flag)
		{
			return;
		}
		Type type = TypeOf("ICamoSelect");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "SetMode");
		foreach (Building item in parent.Map.listerBuildings.allBuildingsColonist)
		{
			if (type.IsInstanceOfType(item))
			{
				methodInfo.Invoke(item, new object[1] { mode });
			}
		}
		object obj = parent.Map.components.FirstOrDefault((MapComponent c) => c.GetType() == TypeOf("MapComponent_Rimatomics"));
		if (obj != null)
		{
			PatchingUtilities.Set(obj, "MapCamoMode", mode);
		}
		SoundDefOf.Click.PlayOneShotOnCamera();
	}

	private static bool IncreaseShieldPrefix(ThingComp __instance)
	{
		return ShieldRadiusPrefix(__instance, 1);
	}

	private static bool DecreaseShieldPrefix(ThingComp __instance)
	{
		return ShieldRadiusPrefix(__instance, -1);
	}

	private static bool ShieldRadiusPrefix(ThingComp shield, int delta)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncShieldRadius(shield.parent, delta);
		return false;
	}

	private static void SyncShieldRadius(Thing parent, int delta)
	{
		bool flag = parent == null || !parent.Spawned;
		if (!flag)
		{
			bool flag2 = ((delta == -1 || delta == 1) ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			return;
		}
		object obj = (parent as ThingWithComps)?.AllComps.FirstOrDefault((ThingComp c) => c.GetType() == TypeOf("CompRimatomicsShield"));
		if (obj != null && (bool)PatchingUtilities.Get(obj, "MEPS"))
		{
			float num = (float)PatchingUtilities.Get(obj, "targetRadius");
			if (float.IsFinite(num))
			{
				PatchingUtilities.Set(obj, "targetRadius", (delta > 0) ? Mathf.Min(13f, num + 1f) : Mathf.Max(3f, num - 1f));
			}
		}
	}

	private static void SyncPipeRemovalDesignator(SyncWorker sync, ref Designator designator)
	{
		int obj = (sync.isWriting ? Convert.ToInt32(removePipeMode.GetValue(designator)) : 0);
		sync.Bind(ref obj);
		if (!sync.isWriting)
		{
			if (!Enum.IsDefined(removePipeMode.FieldType, obj))
			{
				throw new ArgumentOutOfRangeException("mode");
			}
			removePipeMode.SetValue(designator, Enum.ToObject(removePipeMode.FieldType, obj));
		}
	}

	private static bool FuelLifePrefix(Thing __instance, FloatRange value)
	{
		if (!Intercept)
		{
			return true;
		}
		FloatRange floatRange = (FloatRange)PatchingUtilities.Get(__instance, "_FuelLifeFilter");
		if (floatRange.min != value.min || floatRange.max != value.max)
		{
			SyncFuelLife(__instance, value.min, value.max);
		}
		return false;
	}

	private static void SyncFuelLife(Thing owner, float min, float max)
	{
		if (owner != null && owner.Spawned && TypeOf("IFuelFilter").IsInstanceOfType(owner) && float.IsFinite(min) && float.IsFinite(max) && !(min < 0f) && !(max > 1f) && !(min > max))
		{
			PatchingUtilities.Set(owner, "_FuelLifeFilter", new FloatRange(min, max));
		}
	}

	private static void FuelTabPrefix(ITab __instance, out FuelTabState __state)
	{
		__state = ((Intercept && __instance.SelThing is IStoreSettingsParent) ? new FuelTabState(__instance.SelThing) : null);
	}

	private static Exception FuelTabFinalizer(Exception __exception, FuelTabState __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static bool BuyProjectPrefix(int mapID, object project, out IDisposable __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer || Intercept)
		{
			return true;
		}
		Map map = Find.Maps.FirstOrDefault((Map m) => m.uniqueID == mapID);
		if (map == null || project == null || !TypeOf("RimatomicResearchDef").IsInstanceOfType(project))
		{
			return false;
		}
		object obj = PatchingUtilities.Call(TypeOf("DubUtils"), "GetResearch");
		if (obj != null)
		{
			object obj2 = (PatchingUtilities.Get(obj, "Purchased") as IDictionary)?[project];
			if ((!(obj2 is bool) || !(bool)obj2) && (bool)PatchingUtilities.Get(project, "PrerequisitesCompleted") && !(bool)PatchingUtilities.Get(project, "IsFinished"))
			{
				int num = (int)PatchingUtilities.Get(project, "price");
				if (num < 0 || !TradeUtility.ColonyHasEnoughSilver(map, num))
				{
					return false;
				}
				__state = SimulationMapScope(map);
				return true;
			}
		}
		return false;
	}

	private static Exception BuyProjectFinalizer(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static bool TryReadUpgrade<T>(Dictionary<ThingDef, T> dictionary, ThingDef part, out T value)
	{
		if (dictionary.TryGetValue(part, out value))
		{
			return true;
		}
		return Intercept;
	}

	private static IEnumerable<CodeInstruction> UpgradeReadTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		int found = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.operand is MethodInfo { Name: "TryGetValue", DeclaringType: var declaringType } methodInfo && (declaringType?.IsGenericType ?? false) && methodInfo.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<, >) && methodInfo.DeclaringType.GetGenericArguments()[0] == typeof(ThingDef) && methodInfo.DeclaringType.GetGenericArguments()[1] == TypeOf("UpgradeState"))
			{
				MethodInfo operand = AccessTools.DeclaredMethod(typeof(Rimatomics), "TryReadUpgrade").MakeGenericMethod(TypeOf("UpgradeState"));
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = operand
				};
				found++;
			}
			else
			{
				yield return instruction;
			}
		}
		if (found != 1)
		{
			throw new InvalidOperationException($"Rimatomics upgrade UI lookup changed: {found}");
		}
	}
}
