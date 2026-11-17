using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

internal static class DebugActions
{
	private class DesyncSourceSearchWindow : Window
	{
		private HashSet<StuffToSearch> selectedOptions;

		private bool checkedTypes;

		private bool doModded = true;

		private bool doVanilla;

		public override bool IsDebug => true;

		public override Vector2 InitialSize => new Vector2(525f, 680f);

		public override float Margin
		{
			get
			{
				return 32f;
			}
		}

		public DesyncSourceSearchWindow()
		{
			doCloseX = true;
			SetDefault();
		}

		private void SetDefault()
		{
			selectedOptions = ((StuffToSearch[])Enum.GetValues(typeof(StuffToSearch))).Where((StuffToSearch s) => !typeof(StuffToSearch).GetField(s.ToString()).IsDefined(typeof(NonBasicAttribute))).ToHashSet();
		}

		private void SetAll()
		{
			selectedOptions = ((StuffToSearch[])Enum.GetValues(typeof(StuffToSearch))).ToHashSet();
		}

		public override void OnAcceptKeyPressed()
		{
			if (Accept())
			{
				Event.current.Use();
			}
		}

		private bool CanAccept()
		{
			if ((doModded || (doVanilla && (selectedOptions.Count != 1 || selectedOptions.Single() != StuffToSearch.PatchedSyncMethods))) && selectedOptions.Any())
			{
				return !Log.reachedMaxMessagesLimit;
			}
			return false;
		}

		private bool Accept()
		{
			if (!CanAccept())
			{
				return false;
			}
			LogUnpatchedStuff(selectedOptions, checkedTypes, doModded, doVanilla);
			Close();
			Find.WindowStack.Add(new EditWindow_Log());
			return true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			Listing_Standard listing_Standard = new Listing_Standard();
			listing_Standard.Begin(inRect);
			foreach (StuffToSearch value in Enum.GetValues(typeof(StuffToSearch)))
			{
				bool checkOn = selectedOptions.Contains(value);
				bool flag = checkOn;
				listing_Standard.CheckboxLabeled(value.ToString(), ref checkOn);
				if (checkOn != flag)
				{
					if (checkOn)
					{
						selectedOptions.Add(value);
					}
					else
					{
						selectedOptions.Remove(value);
					}
				}
			}
			listing_Standard.GapLine();
			listing_Standard.CheckboxLabeled("Check mods", ref doModded);
			listing_Standard.CheckboxLabeled("Check vanilla (only relevant for MP devs)", ref doVanilla);
			listing_Standard.GapLine();
			listing_Standard.CheckboxLabeled("Log all checked types (do not use unless specifically told to do so)", ref checkedTypes);
			listing_Standard.GapLine();
			if (listing_Standard.ButtonText("Default (basic)"))
			{
				SetDefault();
			}
			if (listing_Standard.ButtonText("Default (all)"))
			{
				SetAll();
			}
			listing_Standard.GapLine();
			bool flag2 = CanAccept();
			bool reachedMaxMessagesLimit = Log.reachedMaxMessagesLimit;
			int messageCount = Log.messageCount;
			string text;
			if (reachedMaxMessagesLimit)
			{
				text = "Cannot run, message log full - clear your message log";
			}
			else if (messageCount >= 950)
			{
				text = "Cannot run, message log almost full - clear your message log";
			}
			else
			{
				text = (flag2 ? "Run" : "Cannot run, incorrect option(s)");
			}
			string label = text;
			if (listing_Standard.ButtonText(label))
			{
				Accept();
			}
			listing_Standard.Gap(listing_Standard.verticalSpacing);
			listing_Standard.End();
		}
	}

	private class NonBasicAttribute : Attribute
	{
	}

	internal enum StuffToSearch
	{
		SystemRng,
		UnityRng,
		GenView,
		Coroutines,
		Multithreading,
		[NonBasic]
		CameraDriver,
		[NonBasic]
		CurrentMap,
		[NonBasic]
		Selector,
		Stopwatch,
		[NonBasic]
		NonTickingUpdate,
		TimeManager,
		[NonBasic]
		GetHashCode,
		[NonBasic]
		PatchedSyncMethods,
		[NonBasic]
		LongEvents,
		MoteSaturation,
		CustomVtrImplementation
	}

	private const string CategoryName = "Multiplayer Compatibility";

	private const string REFERENCES_FOLDER = "References";

	internal static ModContentPack content;

	private static readonly MethodInfo FindCurrentMap = typeof(Find).DeclaredPropertyGetter("CurrentMap");

	private static readonly MethodInfo GameCurrentMap = typeof(Game).DeclaredPropertyGetter("CurrentMap");

	private static readonly MethodInfo GetHashCodeMethod = typeof(object).DeclaredMethod("GetHashCode");

	private static readonly HashSet<MethodInfo> NonTickingUpdateMethodsOverrides = new MethodInfo[9]
	{
		typeof(MapComponent).DeclaredMethod("MapComponentUpdate"),
		typeof(GameComponent).DeclaredMethod("GameComponentUpdate"),
		typeof(WorldComponent).DeclaredMethod("WorldComponentUpdate"),
		AccessTools.DeclaredMethod("HugsLib.ModBase:Update"),
		AccessTools.DeclaredMethod("HugsLib.ModBase:FixedUpdate"),
		AccessTools.DeclaredMethod("HugsLib.ModBase:OnGUI"),
		AccessTools.DeclaredMethod("VFECore.TerrainInstance:Update"),
		AccessTools.DeclaredMethod("VFECore.TerrainComp:CompUpdate"),
		AccessTools.DeclaredMethod("VFECore.DefExtensionActive:DoWork", new Type[1] { AccessTools.TypeByName("Verse.TerrainDef") })
	}.Where((MethodInfo x) => x != null).ToHashSet();

	private static readonly HashSet<MethodInfo> NonTickingUpdateMethodCalls = new MethodInfo[2]
	{
		AccessTools.DeclaredMethod("HugsLib.Utils.DoLaterScheduler:DoNextUpdate"),
		AccessTools.DeclaredMethod("HugsLib.Utils.DoLaterScheduler:DoNextOnGUI")
	}.Where((MethodInfo x) => x != null).ToHashSet();

	private static readonly HashSet<MethodInfo> LongEventMethods = new MethodInfo[3]
	{
		typeof(LongEventHandler).DeclaredMethod("QueueLongEvent", new Type[6]
		{
			typeof(Action),
			typeof(string),
			typeof(bool),
			typeof(Action<Exception>),
			typeof(bool),
			typeof(Action)
		}),
		typeof(LongEventHandler).DeclaredMethod("QueueLongEvent", new Type[4]
		{
			typeof(IEnumerable),
			typeof(string),
			typeof(Action<Exception>),
			typeof(bool)
		}),
		typeof(LongEventHandler).DeclaredMethod("QueueLongEvent", new Type[6]
		{
			typeof(Action),
			typeof(string),
			typeof(string),
			typeof(bool),
			typeof(Action<Exception>),
			typeof(bool)
		})
	}.Where((MethodInfo x) => x != null).ToHashSet();

	private static readonly HashSet<MethodInfo> MoteSaturationMethods = new MethodInfo[4]
	{
		typeof(MoteCounter).DeclaredPropertyGetter("MoteCount"),
		typeof(MoteCounter).DeclaredPropertyGetter("Saturation"),
		typeof(MoteCounter).DeclaredPropertyGetter("Saturated"),
		typeof(MoteCounter).DeclaredPropertyGetter("SaturatedLowPriority")
	}.Where((MethodInfo x) => x != null).ToHashSet();

	private static readonly HashSet<MethodInfo> VtrMethodOverrides = new MethodInfo[4]
	{
		typeof(Thing).DeclaredPropertyGetter("UpdateRateTicks"),
		typeof(Thing).DeclaredPropertyGetter("MinTickIntervalRate"),
		typeof(Thing).DeclaredPropertyGetter("MaxTickIntervalRate"),
		typeof(WorldObject).DeclaredPropertyGetter("UpdateRateTicks")
	}.Where((MethodInfo x) => x != null).ToHashSet();

	[DebugAction("Multiplayer Compatibility", "Generate reference DLL", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.Entry)]
	private static void BuildDllReferences()
	{
		Log.Warning("MPCompat :: Put any reference building requests under References as {DLLNAME}.txt");
		var (value, value2, value3) = ReferenceBuilder.Restore(Path.Combine(content.RootDir, "References"));
		Log.Warning($"MPCompat :: Finished building references. Built: {value}. Skipped: {value2}. Failed: {value3}.");
	}

	[DebugAction("Multiplayer Compatibility", "Unsafe stuff logger", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.Entry)]
	public static void OpenDesyncSourceSearchWindow()
	{
		Find.WindowStack.Add(new DesyncSourceSearchWindow());
	}

	public static void LogUnpatchedStuff(HashSet<StuffToSearch> selectedStuff, bool logAllCheckedClasses, bool doModded = true, bool doVanilla = false)
	{
		if (!doModded && !doVanilla)
		{
			Log.Error("Trying to search for potential desync sources with both vanilla and modded disabled.");
			return;
		}
		if (!selectedStuff.Any())
		{
			Log.Error("Trying to search for potential desync sources but nothing was selected for searching.");
			return;
		}
		if (!doVanilla && selectedStuff.Count == 1 && selectedStuff.Single() == StuffToSearch.PatchedSyncMethods)
		{
			Log.Error("Trying to search for potential desync sources for vanilla only but the only option was patched sync methods.");
			return;
		}
		string[] unsupportedNamespaces = new string[32]
		{
			"System", "Unity", "UnityEditor", "UnityEngine", "UnityEngineInternal", "Multiplayer", "Microsoft", "HarmonyLib", "Mono", "MonoMod",
			"Ionic", "NVorbis", "RuntimeAudioClipLoader", "JetBrains", "AOT", "Steamworks", "UnityStandardAssets", "ObjCRuntimeInternal", "TMPro", "NAudio",
			"ICSharpCode", "MS", "DynDelegate", "I18N", "LiteNetLib", "RestSharp", "YamlDotNet", "SemVer", "Prepatcher", "Prestarter",
			"FisheryLib", "PerformanceFish"
		};
		IEnumerable<Type> enumerable = Array.Empty<Type>();
		if (doModded)
		{
			enumerable = enumerable.Concat(LoadedModManager.RunningMods.Where((ModContentPack x) => x.PackageId.ToLower() != "rwmt.multiplayer" && x.PackageId.ToLower() != "rwmt.multiplayercompatibility").SelectMany((ModContentPack x) => x.assemblies.loadedAssemblies).SelectMany((Assembly x) => x.GetTypes()));
		}
		if (doVanilla)
		{
			enumerable = enumerable.Concat(typeof(Def).Assembly.GetTypes());
		}
		enumerable = enumerable.Distinct();
		Dictionary<StuffToSearch, List<string>> log = selectedStuff.ToDictionary((StuffToSearch x) => x, (StuffToSearch _) => new List<string>());
		List<string> logAllClasses = null;
		if (logAllCheckedClasses)
		{
			logAllClasses = new List<string>();
		}
		if (selectedStuff.Any((StuffToSearch x) => x != StuffToSearch.PatchedSyncMethods))
		{
			Parallel.ForEach(enumerable, (Type t) =>
			{
				FindUnpatchedInType(t, unsupportedNamespaces, log, logAllClasses);
			});
		}
		if (doModded && selectedStuff.Contains(StuffToSearch.PatchedSyncMethods))
		{
			FindPatchedSyncMethods(log);
		}
		if (log.Any((KeyValuePair<StuffToSearch, List<string>> x) => x.Value.Any()))
		{
			Log.Warning("== Potentially unpatched RNG or unsafe methods found. ==");
			Log.Warning("Please note, it doesn't always need syncing, or might even break if synced, depending on how the mod uses it. It could also be patched in an alternative way.");
			Log.Warning("Things that are already patched or don't need patching are not listed here, if possible to (easily) check for that.");
			if (log.TryGetValue(StuffToSearch.SystemRng, out var value) && value.Any())
			{
				Log.Warning("== Unpatched System RNG: ==");
				Log.Warning("== Review constructors, Shared, instance calls, fields and factories. A finding is not proof of a desync; seeded or presentation-only use may be valid. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.UnityRng, out value) && value.Any())
			{
				Log.Warning("== Unpatched Unity RNG: ==");
				Log.Warning("== Entries identify APIs outside the existing Unity RNG transpiler. Review their simulation/presentation ownership before patching. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.GenView, out value) && value.Any())
			{
				Log.Warning("== GenView usage found: ==");
				Log.Warning("== Usage of GenView means the mod is doing something based on if something is (not) visible for the user. Can cause issues as players tend to have different camera positions, or be on different maps. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.Coroutines, out value) && value.Any())
			{
				Log.Warning("== Coroutine usage found: ==");
				Log.Warning("== Coroutine are not supported by MP as they are not deterministic. Unless they were patched, or are used on game startup, expect issues. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.Multithreading, out value) && value.Any())
			{
				Log.Warning("== Multithreading usage found: ==");
				Log.Warning("== Please note, the detection may not be perfect and miss some some multithreading usage! ==");
				Log.Warning("== Multithreading is not supported by MP as they are not deterministic. Unless they were patched, or are used on game startup, expect issues. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.CameraDriver, out value) && value.Any())
			{
				Log.Warning("== CameraDriver usage found: ==");
				Log.Warning("== Usage of CameraDriver may cause issues if used for things like checking if something is (not) visible on the screen. Can cause issues as players tend to have different camera positions, or be on different maps. Mods moving camera around, etc. are generally fine. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.CurrentMap, out value) && value.Any())
			{
				Log.Warning("== Current map usage found: ==");
				Log.Warning("== Mods basing code on current map for things like spawning events may cause issues, as players can be on different maps. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.Selector, out value) && value.Any())
			{
				Log.Warning("== Selector usage found: ==");
				Log.Warning("== Usage of selector could cause issues when mod needs to check what the player has selected, but for obvious reason in MP there's more than 1 player. Using it for displaying overlays, etc. is fine. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.Stopwatch, out value) && value.Any())
			{
				Log.Warning("== Stopwatch usage found: ==");
				Log.Warning("== Potential issues from it arise when mod try to make a mod more performant by limiting how long some code can run. Using it to measure performance is safe. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.NonTickingUpdate, out value) && value.Any())
			{
				Log.Warning("== Non-ticking update call usage found: ==");
				Log.Warning("== Those can be called while the game is paused, and are not called once per tick (instead can be called once per frame, etc.). Depending on what it's used for, it may cause issues. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.TimeManager, out value) && value.Any())
			{
				Log.Warning("== TimeManager usage found: ==");
				Log.Warning("== TimeManager uses timing functions that won't be synced across players. Unless used for UI, sounds, etc. then it will cause desyncs. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.GetHashCode, out value) && value.Any())
			{
				Log.Warning("== GetHashCode usage found: ==");
				Log.Warning("== A lot of those will likely be false positives. However, depending on what the mod does with it - it can cause issues. Especially if the object has not implemented, or has non-deterministic .GetHashCode() implementation. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.PatchedSyncMethods, out value) && value.Any())
			{
				Log.Warning("== Harmony patched SyncMethods found: ==");
				Log.Warning("== SyncMethod normally is synchronized to all players and called only then - however, if it contains any Harmony patches (skipping transpilers, as those are likely least disruptive) then they still run before the method is synchronized, which may cause issues. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.LongEvents, out value) && value.Any())
			{
				Log.Warning("== Long events found: ==");
				Log.Warning("== In majority of cases, long events are completely safe. However, there are 2 potential situations that can cause issues with them. The first is asynchronous long events, and the second is long events queued during game startup. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.MoteSaturation, out value) && value.Any())
			{
				Log.Warning("== Mote saturation checks found: ==");
				Log.Warning("== Code that runs based on how saturated the motes are may cause desyncs as mote saturation may differ between players. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
			if (log.TryGetValue(StuffToSearch.CustomVtrImplementation, out value) && value.Any())
			{
				Log.Warning("== Custom Variable Tick Rate found: ==");
				Log.Warning("== Code that uses variable tick rate may cause desyncs if the tick rate depends on some features such as, current player's map, player's camera location, or what the player has selected. ==");
				Log.Message(value.Append("\n").Join(null, "\n"));
			}
		}
		else
		{
			Log.Warning("== No unpatched RNG or potentially unsafe methods found ==");
		}
		if (logAllClasses != null && logAllClasses.Any())
		{
			Log.Warning("== All checked classes: ==");
			Log.Message(logAllClasses.OrderBy((string x) => x).Join(null, "\n"));
		}
	}

	internal static void FindUnpatchedInType(Type type, string[] unsupportedNamespaces, Dictionary<StuffToSearch, List<string>> log, List<string> logAllClasses = null)
	{
		try
		{
			if (unsupportedNamespaces.Any((string t) => type.Namespace != null && (type.Namespace == t || type.Namespace.StartsWith(t + "."))))
			{
				return;
			}
		}
		catch (Exception)
		{
		}
		if (logAllClasses != null)
		{
			lock (logAllClasses)
			{
				logAllClasses.Add(type.FullName + " (" + type.Assembly.GetName().Name + ")");
			}
		}
		try
		{
			foreach (MethodBase item in from m in (from m in AccessTools.GetDeclaredMethods(type)
					where m.ReturnType.FullName != "System.MonoFNPtrFakeClass"
					select m).Cast<MethodBase>().Concat(AccessTools.GetDeclaredConstructors(type)).Concat(from p in AccessTools.GetDeclaredProperties(type).SelectMany((PropertyInfo p) => new MethodInfo[2]
					{
						p.GetGetMethod(nonPublic: true),
						p.GetSetMethod(nonPublic: true)
					})
					where p != null && p.ReturnType.FullName != "System.MonoFNPtrFakeClass"
					select p)
					.Distinct()
				where m.HasMethodBody()
				select m)
			{
				try
				{
					List<string> list = new List<string>();
					foreach (StuffToSearch item2 in ScanMethod(item, list))
					{
						if (log.TryGetValue(item2, out var value))
						{
							bool flag = (uint)item2 <= 1u;
							string value2 = ((flag && list.Count > 0) ? (" [" + string.Join("; ", list.Distinct()) + "]") : "");
							lock (value)
							{
								log[item2].Add($"{type.FullName}:{item.Name} ({type.Assembly.GetName().Name}){value2}");
							}
						}
					}
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception)
		{
		}
	}

	internal static HashSet<StuffToSearch> FindRng(MethodBase baseMethod)
	{
		return ScanMethod(baseMethod, null);
	}

	private static HashSet<StuffToSearch> ScanMethod(MethodBase baseMethod, List<string> rngDetails)
	{
		List<CodeInstruction> currentInstructions = PatchProcessor.GetCurrentInstructions(baseMethod);
		HashSet<StuffToSearch> foundStuff = new HashSet<StuffToSearch>();
		MethodInfo method = baseMethod as MethodInfo;
		if (currentInstructions.Any((CodeInstruction ci) => (ci.operand is Type type && IsSystemRandomType(type)) || (ci.operand is string text && (text == "System.Random" || text.StartsWith("System.Random,")))) && currentInstructions.Any((CodeInstruction ci) => ci.operand is MethodInfo methodInfo10 && ((methodInfo10.DeclaringType == typeof(Activator) && methodInfo10.Name == "CreateInstance") || (typeof(ConstructorInfo).IsAssignableFrom(methodInfo10.DeclaringType) && methodInfo10.Name == "Invoke"))))
		{
			Rng(StuffToSearch.SystemRng, "reflection construction with Random type/name token (review candidate)");
		}
		if (IsOverrideOfAny(method, NonTickingUpdateMethodsOverrides))
		{
			foundStuff.Add(StuffToSearch.NonTickingUpdate);
		}
		if (IsOverrideOfAny(method, VtrMethodOverrides))
		{
			foundStuff.Add(StuffToSearch.CustomVtrImplementation);
		}
		foreach (CodeInstruction item3 in currentInstructions)
		{
			object operand = item3.operand;
			if (operand is ConstructorInfo constructorInfo)
			{
				if ((object)constructorInfo.DeclaringType == null)
				{
					continue;
				}
				if (constructorInfo.DeclaringType != typeof(PatchingUtilities.RandRedirector) && typeof(System.Random).IsAssignableFrom(constructorInfo.DeclaringType))
				{
					Rng(StuffToSearch.SystemRng, constructorInfo.DeclaringType.FullName + " constructor (" + ((constructorInfo.GetParameters().Length == 0) ? "implicit seed" : "seed provenance requires review") + ")");
					continue;
				}
				ConstructorInfo constructorInfo2 = constructorInfo;
				if (typeof(Thread).IsAssignableFrom(constructorInfo2.DeclaringType) || typeof(ThreadStart).IsAssignableFrom(constructorInfo2.DeclaringType))
				{
					foundStuff.Add(StuffToSearch.Multithreading);
					continue;
				}
				ConstructorInfo constructorInfo3 = constructorInfo;
				if (typeof(Stopwatch).IsAssignableFrom(constructorInfo3.DeclaringType))
				{
					foundStuff.Add(StuffToSearch.Stopwatch);
				}
				continue;
			}
			if (operand is MethodInfo methodInfo)
			{
				int num;
				if ((object)methodInfo.DeclaringType != null)
				{
					MethodInfo methodInfo2 = methodInfo;
					if (IsSystemRandomType(methodInfo2.DeclaringType))
					{
						Rng(StuffToSearch.SystemRng, (methodInfo2.Name == "get_Shared") ? "System.Random.Shared" : (methodInfo2.DeclaringType.FullName + "." + methodInfo2.Name + " instance/static call (review source)"));
						continue;
					}
					num = 5;
				}
				else
				{
					num = 3;
				}
				if (!IsSystemRandomType(methodInfo.ReturnType))
				{
					if (num != 3)
					{
						if (num != 5)
						{
							goto IL_0284;
						}
						MethodInfo methodInfo3 = methodInfo;
						if (typeof(UnityEngine.Random).IsAssignableFrom(methodInfo3.DeclaringType))
						{
							Rng(StuffToSearch.UnityRng, $"UnityEngine.Random.{methodInfo3.Name} ({(PatchingUtilities.IsSupportedUnityRng(methodInfo3) ? "supported redirect target, still present" : "not covered by FixUnityRNG")})");
							continue;
						}
						MethodInfo methodInfo4 = methodInfo;
						if (typeof(GenView).IsAssignableFrom(methodInfo4.DeclaringType))
						{
							foundStuff.Add(StuffToSearch.GenView);
							continue;
						}
						MethodInfo methodInfo5 = methodInfo;
						if (typeof(Coroutine).IsAssignableFrom(methodInfo5.ReturnType))
						{
							foundStuff.Add(StuffToSearch.Coroutines);
							continue;
						}
						MethodInfo methodInfo6 = methodInfo;
						if (typeof(CameraDriver).IsAssignableFrom(methodInfo6.ReturnType))
						{
							foundStuff.Add(StuffToSearch.CameraDriver);
							continue;
						}
					}
					MethodInfo methodInfo7 = methodInfo;
					if (methodInfo7 == FindCurrentMap || methodInfo7 == GameCurrentMap)
					{
						foundStuff.Add(StuffToSearch.CurrentMap);
						continue;
					}
					MethodInfo methodInfo8 = methodInfo;
					if (typeof(Selector).IsAssignableFrom(methodInfo8.DeclaringType))
					{
						foundStuff.Add(StuffToSearch.Selector);
						continue;
					}
					MethodInfo methodInfo9 = methodInfo;
					if (typeof(Time).IsAssignableFrom(methodInfo9.DeclaringType))
					{
						foundStuff.Add(StuffToSearch.TimeManager);
						continue;
					}
					if (methodInfo == GetHashCodeMethod && !IsOverrideOf(method, GetHashCodeMethod))
					{
						foundStuff.Add(StuffToSearch.GetHashCode);
						continue;
					}
					if (IsOverrideOfAny(methodInfo, NonTickingUpdateMethodCalls))
					{
						foundStuff.Add(StuffToSearch.NonTickingUpdate);
						continue;
					}
					MethodInfo item = methodInfo;
					if (LongEventMethods.Contains(item))
					{
						foundStuff.Add(StuffToSearch.LongEvents);
						continue;
					}
					MethodInfo item2 = methodInfo;
					if (MoteSaturationMethods.Contains(item2))
					{
						foundStuff.Add(StuffToSearch.MoteSaturation);
					}
					continue;
				}
				goto IL_0284;
			}
			if (operand is FieldInfo fieldInfo && IsSystemRandomType(fieldInfo.FieldType))
			{
				Rng(StuffToSearch.SystemRng, "Random field " + fieldInfo.DeclaringType?.FullName + "." + fieldInfo.Name);
			}
			continue;
			IL_0284:
			Rng(StuffToSearch.SystemRng, "Random-returning factory/property " + methodInfo.DeclaringType?.FullName + "." + methodInfo.Name);
		}
		return foundStuff;
		void Rng(StuffToSearch kind, string detail)
		{
			foundStuff.Add(kind);
			rngDetails?.Add(detail);
		}
	}

	private static bool IsSystemRandomType(Type type)
	{
		if (type != null && typeof(System.Random).IsAssignableFrom(type))
		{
			return !typeof(PatchingUtilities.RandRedirector).IsAssignableFrom(type);
		}
		return false;
	}

	internal static void FindPatchedSyncMethods(Dictionary<StuffToSearch, List<string>> log)
	{
		FieldInfo fieldInfo = AccessTools.DeclaredField("Multiplayer.Client.Sync:methodBaseToInternalId");
		if (fieldInfo == null)
		{
			Log.Error("Failed to search for patched sync methods: could not find sync handlers.");
			return;
		}
		if (!fieldInfo.IsStatic)
		{
			Log.Error("Failed to search for patched sync methods: sync methods are stored in a non-static fields");
			return;
		}
		if (!(fieldInfo.GetValue(null) is Dictionary<MethodBase, int> dictionary))
		{
			Log.Error("Failed to search for patched sync methods: sync methods field does not implement IDictionary interface or is null");
			return;
		}
		if (!dictionary.Any())
		{
			Log.Error("Failed to search for patched sync methods: no registered sync methods found");
			return;
		}
		Parallel.ForEach(dictionary.Keys, (MethodBase method) =>
		{
			Patches patchInfo = Harmony.GetPatchInfo(method);
			List<Patch> list = (from patch in patchInfo.Prefixes.Concat(patchInfo.Postfixes).Concat(patchInfo.Finalizers).Distinct()
				where !(patch.owner == "multiplayer")
				select patch).ToList();
			if (list.Any())
			{
				Type declaringType = method.DeclaringType;
				lock (log[StuffToSearch.PatchedSyncMethods])
				{
					log[StuffToSearch.PatchedSyncMethods].Add($"{declaringType?.FullName}:{method.Name} ({declaringType?.Assembly.GetName().Name}) (patched by: {list.Select((Patch p) => p.owner).ToStringSafeEnumerable()})");
				}
			}
		});
	}

	private static bool IsOverrideOf(MethodInfo method, MethodInfo target)
	{
		if (method == null)
		{
			return false;
		}
		MethodInfo baseDefinition = method.GetBaseDefinition();
		if (baseDefinition == method)
		{
			return false;
		}
		return baseDefinition == target;
	}

	private static bool IsOverrideOfAny(MethodInfo method, HashSet<MethodInfo> possibleTargets)
	{
		if (method == null)
		{
			return false;
		}
		if (!possibleTargets.Any())
		{
			return false;
		}
		MethodInfo baseDefinition = method.GetBaseDefinition();
		if (baseDefinition == method)
		{
			return false;
		}
		return possibleTargets.Contains(baseDefinition);
	}

	[DebugAction("Multiplayer Compatibility", "Search outdated mods", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.Entry)]
	private static void SearchOutdatedMods()
	{
		Type type = AccessTools.TypeByName("UseThisInstead.UseThisInstead");
		if (type == null)
		{
			Log.Error("Use This Instead mod is required to perform this action. It's either not present or not recognized due to an update.");
			return;
		}
		if ((bool)AccessTools.DeclaredField(type, "Scanning").GetValue(null))
		{
			Log.Error("Use This Instead mod is currently scanning files, please try again later.");
			return;
		}
		IDictionary dictionary = (IDictionary)AccessTools.DeclaredField(type, "ModReplacements").GetValue(null);
		if (dictionary == null || dictionary.Count <= 0)
		{
			AccessTools.DeclaredMethod(type, "CheckForReplacements").Invoke(null, new object[1] { true });
			dictionary = (IDictionary)AccessTools.DeclaredField(type, "ModReplacements").GetValue(null);
			if (dictionary == null || dictionary.Count <= 0)
			{
				Log.Error("Use This Instead either failed to initialize or has no replacements listed.");
				return;
			}
		}
		Type type2 = AccessTools.TypeByName("UseThisInstead.ModReplacement");
		MethodInfo modIdField = AccessTools.DeclaredPropertyGetter(type2, "ModId");
		MethodInfo replacementModId = AccessTools.DeclaredPropertyGetter(type2, "ReplacementModId");
		MethodInfo replacementSteamId = AccessTools.DeclaredPropertyGetter(type2, "ReplacementSteamId");
		MethodInfo replacementVersions = AccessTools.DeclaredPropertyGetter(type2, "ReplacementVersions");
		List<(string, string, ulong)> list = (from object x in dictionary.Values
			where ((replacementVersions.Invoke(x, Array.Empty<object>()) as string) ?? string.Empty).Split(',').Contains(VersionControl.CurrentVersionStringWithoutBuild)
			select (id: ((string)modIdField.Invoke(x, Array.Empty<object>())).ToLower().NoModIdSuffix(), replacement: ((string)replacementModId.Invoke(x, Array.Empty<object>())).ToLower().NoModIdSuffix(), workshopId: (ulong)replacementSteamId.Invoke(x, Array.Empty<object>()))).ToList();
		if (dictionary.Count <= 0)
		{
			Log.Error("No replacement entries for " + VersionControl.CurrentVersionStringWithoutBuild + " found.");
			return;
		}
		bool flag = false;
		foreach (Assembly loadedAssembly in LoadedModManager.GetMod<MpCompat>().Content.assemblies.loadedAssemblies)
		{
			foreach (List<string> item in from t in loadedAssembly.GetTypes()
				where t.HasAttribute<MpCompatForAttribute>()
				select ((MpCompatForAttribute[])t.GetCustomAttributes(typeof(MpCompatForAttribute), inherit: false)).Select((MpCompatForAttribute a) => a.PackageId.ToLower()).ToList())
			{
				foreach (string id in item)
				{
					(string, string, ulong) tuple = list.FirstOrDefault(((string id, string replacement, ulong workshopId) x) => x.id == id);
					(string, string, ulong) tuple2 = tuple;
					if ((tuple2.Item1 != null || tuple2.Item2 != null || tuple2.Item3 != 0L) && !item.Contains(tuple.Item2))
					{
						Log.Error($"Found a mod replacement for mod {id} - {tuple.Item2} - https://steamcommunity.com/sharedfiles/filedetails/?id={tuple.Item3}");
						flag = true;
					}
				}
			}
		}
		if (!flag)
		{
			Log.Error("No mod replacements found.");
		}
	}
}
