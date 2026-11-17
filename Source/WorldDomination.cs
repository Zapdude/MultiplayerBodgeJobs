using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("TSA.WorldDominationExperimental")]
public class WorldDomination
{
	private sealed class ProcessedConquestMenu
	{
	}

	private sealed class FieldEdit
	{
		internal object Target;

		internal object Before;

		internal FieldInfo Field;
	}

	private sealed class FieldEditBatch
	{
		internal readonly List<FieldEdit> Edits = new List<FieldEdit>();

		internal int Depth;

		internal bool Submit;
	}

	private sealed class StateGraph
	{
		private static readonly ConcurrentDictionary<Type, FieldInfo[]> Fields = new ConcurrentDictionary<Type, FieldInfo[]>();

		internal readonly List<ILoadReferenceable> References;

		private readonly Dictionary<ILoadReferenceable, int> referenceIds = new Dictionary<ILoadReferenceable, int>(ReferenceEqualityComparer.Instance);

		private readonly HashSet<WorldObject> liveWorldObjects;

		private readonly Dictionary<object, int> objects = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

		private readonly List<object> restored = new List<object>();

		internal StateGraph(List<ILoadReferenceable> references = null)
		{
			References = references ?? new List<ILoadReferenceable>();
			for (int i = 0; i < References.Count; i++)
			{
				if (References[i] != null)
				{
					referenceIds.TryAdd(References[i], i);
				}
			}
			liveWorldObjects = new HashSet<WorldObject>(Find.WorldObjects?.AllWorldObjects ?? new List<WorldObject>(), ReferenceEqualityComparer.Instance);
		}

		private int Reference(ILoadReferenceable value)
		{
			if (value is WorldObject worldObject && (worldObject.Destroyed || !liveWorldObjects.Contains(worldObject)))
			{
				return -1;
			}
			if (!referenceIds.TryGetValue(value, out var value2))
			{
				value2 = References.Count;
				References.Add(value);
				referenceIds.Add(value, value2);
			}
			return value2;
		}

		internal void Write(BinaryWriter writer, object value)
		{
			if (value == null)
			{
				writer.Write((byte)0);
				return;
			}
			Type type = value.GetType();
			if (value is GlobalTargetInfo globalTargetInfo)
			{
				if (!globalTargetInfo.HasWorldObject && globalTargetInfo.IsValid)
				{
					throw new NotSupportedException("WD threat snapshot has a non-world target");
				}
				writer.Write((byte)8);
				writer.Write(globalTargetInfo.HasWorldObject ? Reference(globalTargetInfo.WorldObject) : (-1));
				return;
			}
			if (value is ILoadReferenceable value2)
			{
				writer.Write((byte)1);
				writer.Write(Reference(value2));
				return;
			}
			if (value is WorldObjectComp worldObjectComp)
			{
				writer.Write((byte)2);
				writer.Write(Reference(worldObjectComp.parent));
				writer.Write(type.FullName);
				return;
			}
			if (value is WorldComponent)
			{
				writer.Write((byte)3);
				writer.Write(type.FullName);
				return;
			}
			if (value == SessionSettings)
			{
				writer.Write((byte)4);
				return;
			}
			if (type.IsPrimitive || type.IsEnum || value is string)
			{
				writer.Write((byte)5);
				writer.Write(type.AssemblyQualifiedName);
				if (type.IsEnum)
				{
					writer.Write(Convert.ToInt64(value));
					return;
				}
				if (value is bool value3)
				{
					writer.Write(value3);
					return;
				}
				if (value is int value4)
				{
					writer.Write(value4);
					return;
				}
				if (value is long value5)
				{
					writer.Write(value5);
					return;
				}
				if (value is float value6)
				{
					writer.Write(value6);
					return;
				}
				if (value is double value7)
				{
					writer.Write(value7);
					return;
				}
				if (value is string value8)
				{
					writer.Write(value8);
					return;
				}
				throw new NotSupportedException("Unreviewed WD state scalar " + type);
			}
			if (objects.TryGetValue(value, out var value9))
			{
				writer.Write((byte)6);
				writer.Write(value9);
				return;
			}
			ValidateType(type);
			writer.Write((byte)7);
			writer.Write(objects.Count);
			objects.Add(value, objects.Count);
			writer.Write(type.AssemblyQualifiedName);
			if (value is IDictionary dictionary)
			{
				writer.Write(dictionary.Count);
				{
					foreach (DictionaryEntry item in dictionary)
					{
						Write(writer, item.Key);
						Write(writer, item.Value);
					}
					return;
				}
			}
			if (value is IEnumerable source && (type.IsArray || type.IsGenericType))
			{
				object[] array = source.Cast<object>().ToArray();
				writer.Write(array.Length);
				object[] array2 = array;
				foreach (object value10 in array2)
				{
					Write(writer, value10);
				}
			}
			else
			{
				FieldInfo[] array3 = StateFields(type);
				foreach (FieldInfo fieldInfo in array3)
				{
					Write(writer, fieldInfo.GetValue(value));
				}
			}
		}

		internal object Read(BinaryReader reader)
		{
			byte b = reader.ReadByte();
			switch (b)
			{
			case 0:
				return null;
			case 1:
			{
				int num3 = reader.ReadInt32();
				if (num3 >= 0)
				{
					return References[num3];
				}
				return null;
			}
			case 2:
			{
				int num2 = reader.ReadInt32();
				WorldObject obj2 = ((num2 < 0) ? null : (References[num2] as WorldObject));
				Type compType = AccessTools.TypeByName(reader.ReadString());
				return obj2?.AllComps.FirstOrDefault((WorldObjectComp c) => c.GetType() == compType);
			}
			case 3:
			{
				Type componentType = AccessTools.TypeByName(reader.ReadString());
				return Find.World?.components.FirstOrDefault((WorldComponent c) => c.GetType() == componentType);
			}
			case 4:
				return SessionSettings;
			case 5:
			{
				Type type2 = Type.GetType(reader.ReadString(), throwOnError: true);
				if (type2.IsEnum)
				{
					return Enum.ToObject(type2, reader.ReadInt64());
				}
				if (type2 == typeof(bool))
				{
					return reader.ReadBoolean();
				}
				if (type2 == typeof(int))
				{
					return reader.ReadInt32();
				}
				if (type2 == typeof(long))
				{
					return reader.ReadInt64();
				}
				if (type2 == typeof(float))
				{
					return reader.ReadSingle();
				}
				if (type2 == typeof(double))
				{
					return reader.ReadDouble();
				}
				if (type2 == typeof(string))
				{
					return reader.ReadString();
				}
				throw new InvalidDataException("Unreviewed WD scalar " + type2);
			}
			case 6:
				return restored[reader.ReadInt32()];
			case 8:
			{
				int num4 = reader.ReadInt32();
				return (num4 < 0 || !(References[num4] is WorldObject worldObject)) ? GlobalTargetInfo.Invalid : new GlobalTargetInfo(worldObject);
			}
			default:
				throw new InvalidDataException("Unknown WD state tag");
			case 7:
			{
				if (reader.ReadInt32() != restored.Count)
				{
					throw new InvalidDataException("Invalid WD state object index");
				}
				Type type = Type.GetType(reader.ReadString(), throwOnError: true);
				ValidateType(type);
				if (type.IsArray || type.IsGenericType)
				{
					int num = reader.ReadInt32();
					if (num < 0 || num > 1000000)
					{
						throw new InvalidDataException("Invalid WD state collection size");
					}
					object obj = (type.IsArray ? Array.CreateInstance(type.GetElementType(), num) : Activator.CreateInstance(type));
					restored.Add(obj);
					if (obj is IDictionary dictionary)
					{
						for (int i = 0; i < num; i++)
						{
							dictionary.Add(Read(reader), Read(reader));
						}
					}
					else if (obj is Array array)
					{
						for (int j = 0; j < num; j++)
						{
							array.SetValue(Read(reader), j);
						}
					}
					else
					{
						MethodInfo method = type.GetMethod((type.GetGenericTypeDefinition() == typeof(Queue<>)) ? "Enqueue" : "Add");
						for (int k = 0; k < num; k++)
						{
							method.Invoke(obj, new object[1] { Read(reader) });
						}
					}
					return obj;
				}
				object uninitializedObject = RuntimeHelpers.GetUninitializedObject(type);
				restored.Add(uninitializedObject);
				FieldInfo[] array2 = StateFields(type);
				for (int l = 0; l < array2.Length; l++)
				{
					array2[l].SetValue(uninitializedObject, Read(reader));
				}
				return uninitializedObject;
			}
			}
		}

		private static FieldInfo[] StateFields(Type type)
		{
			return Fields.GetOrAdd(type, (Type t) => (from f in AccessTools.GetDeclaredFields(t)
				where !f.IsStatic
				select f).OrderBy((FieldInfo f) => f.Name, StringComparer.Ordinal).ToArray());
		}

		private static void ValidateType(Type type)
		{
			if (!type.IsArray && (!type.IsGenericType || !new Type[4]
			{
				typeof(List<>),
				typeof(Queue<>),
				typeof(HashSet<>),
				typeof(Dictionary<, >)
			}.Contains(type.GetGenericTypeDefinition(), null)))
			{
				string? fullName = type.FullName;
				if (fullName == null || !fullName.StartsWith("TSA_WorldDomination.", StringComparison.Ordinal) || !SimulationDataTypes.Contains(type.FullName.Substring("TSA_WorldDomination.".Length)))
				{
					throw new NotSupportedException("Unreviewed WD scheduler record " + type);
				}
			}
		}
	}

	private sealed class LocalPreviewScope : IDisposable
	{
		private readonly bool previousDontSync;

		private bool disposed;

		internal LocalPreviewScope()
		{
			previousDontSync = previewDontSync();
			Rand.PushState();
			previewDontSync() = true;
			localPreviewDepth++;
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				localPreviewDepth--;
				previewDontSync() = previousDontSync;
			}
			finally
			{
				Rand.PopState();
			}
		}
	}

	private sealed class WorldDominationSimulationScope : IDisposable
	{
		private readonly object owner;

		private readonly MethodInfo post;

		private readonly Map map;

		private readonly Map previous;

		private bool disposed;

		internal WorldDominationSimulationScope(object owner, MethodInfo pre, MethodInfo post, Map map)
		{
			this.owner = owner ?? throw new InvalidOperationException("WorldDomination requires an initialized Multiplayer simulation owner");
			this.post = post;
			this.map = map;
			previous = (Map)simulationTickingMap.GetValue(null);
			pre.Invoke(owner, null);
			try
			{
				if (map != null)
				{
					simulationTickingMap.SetValue(null, map);
				}
			}
			catch
			{
				post.Invoke(owner, null);
				throw;
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				post.Invoke(owner, null);
			}
			finally
			{
				if (map != null)
				{
					simulationTickingMap.SetValue(null, previous);
				}
			}
		}
	}

	private sealed class EmptyWorldDominationScope : IDisposable
	{
		internal static readonly EmptyWorldDominationScope Instance = new EmptyWorldDominationScope();

		public void Dispose()
		{
		}
	}

	private const string Ns = "TSA_WorldDomination.";

	private static readonly ConcurrentDictionary<string, Type> Types = new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

	private static readonly ConcurrentDictionary<(Type, string), MemberInfo> Members = new ConcurrentDictionary<(Type, string), MemberInfo>();

	private static readonly List<MethodInfo> RegisteredCommands = new List<MethodInfo>();

	private static readonly ConditionalWeakTable<object, ProcessedConquestMenu> processedConquestMenus = new ConditionalWeakTable<object, ProcessedConquestMenu>();

	[ThreadStatic]
	private static int processedConquestMenuDepth;

	private static readonly Dictionary<string, int> TradeKinds = new Dictionary<string, int>(StringComparer.Ordinal)
	{
		["SettlementBuyUtility.TryLaunchBuy"] = 0,
		["SettlementGiftUtility.TryLaunchGift"] = 1,
		["SettlementBribeUtility.TryLaunchSettlementBribe"] = 2,
		["SettlementBribeUtility.TryLaunchRaidBribe"] = 3,
		["DiplomacyNegotiateUtility.TryLaunch"] = 4
	};

	private static AccessTools.FieldRef<int> goodwillPulseTick;

	private static readonly Dictionary<string, FieldInfo> CommandFields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

	[ThreadStatic]
	private static FieldEditBatch activeFieldEdits;

	private static readonly (string Type, string Field)[] SimulationRoots = new (string, string)[45]
	{
		("WorldComponent_SpreadManager", "dailyActionQueue"),
		("WorldComponent_SpreadManager", "dailySnapshot"),
		("WorldComponent_SpreadManager", "pendingRaid"),
		("WorldComponent_SpreadManager", "pendingTrader"),
		("WorldComponent_SpreadManager", "dirtyPowerStats"),
		("WorldComponent_SpreadManager", "lateGameMetricsBootstrapped"),
		("WorldComponent_SpreadManager", "lateGameMetricsBootstrapScheduled"),
		("WorldComponent_SpreadManager", "lateGameMetricsDirty"),
		("WorldComponent_SpreadManager", "lateGameMetricsDirtyTick"),
		("WorldComponent_SpreadManager", "nextWorldThreatRefreshTick"),
		("WorldComponent_SpreadManager", "lastThreatFingerprint"),
		("WorldComponent_SpreadManager", "nextThreatFingerprintTick"),
		("WorldActions_NpcLaunchStagger", "fortifyQueue"),
		("WorldActions_NpcLaunchStagger", "roadQueue"),
		("NpcT4GlobalFireStagger", "nextMortarFireTick"),
		("NpcT4GlobalFireStagger", "nextAaFireTick"),
		("NpcT4GlobalFireStagger", "pendingNpcAaEngages"),
		("WorldActions_DesperationRaid", "suppressLossNotifyIds"),
		("WorldActions_DesperationRaid", "notifiedLossIds"),
		("AntiAirFireUtils", "pendingFlak"),
		("AntiAirFireUtils", "pendingEngage"),
		("AntiAirFireUtils", "claimLetterByTargetId"),
		("AntiAirFireUtils", "claimExpireTickByTargetId"),
		("AntiAirFireUtils", "pendingEngageKeys"),
		("WD_SameTileTravelerClash", "caravanLastTile"),
		("WD_SameTileTravelerClash", "lastCleanupTick"),
		("WorldComponent_InterceptionScheduler", "pendingExternalAirborneWakeTickById"),
		("WorldComponent_InterceptionScheduler", "pendingAtCooldownWakeTickById"),
		("WorldComponent_InterceptionScheduler", "armedExternalAirborneAaPairs"),
		("WorldComponent_InterceptionScheduler", "armedMortarShellsById"),
		("WorldComponent_InterceptionScheduler", "npcT4Cursor"),
		("WorldComponent_InterceptionScheduler", "skipUntilByPair"),
		("WorldComponent_InterceptionScheduler", "cachedIntervalTicks"),
		("WorldComponent_InterceptionScheduler", "cachedIntervalTickLastRefresh"),
		("WorldComponent_CaravanChaseTraveler", "lastKnownTravelerTile"),
		("WorldComponent_SpreadManager", "cachedWorldTotalStrength"),
		("WorldComponent_SpreadManager", "cachedWorldThreatTier"),
		("WorldComponent_SpreadManager", "cachedWorldThreatPercent"),
		("WorldComponent_SpreadManager", "cachedWorldThreatMaxRaid"),
		("WorldComponent_SpreadManager", "cachedWorldThreatBaseline"),
		("WorldComponent_SpreadManager", "cachedWorldThreatScariest"),
		("WorldComponent_SpreadManager", "cachedWorldThreatScariestName"),
		("WorldComponent_SpreadManager", "cachedWorldThreatBreakdown"),
		("WorldComponent_SpreadManager", "cachedWorldThreatColony"),
		("WorldComponent_SpreadManager", "cachedThreatSettlements")
	};

	private static readonly HashSet<string> SimulationDataTypes = new HashSet<string>(StringComparer.Ordinal)
	{
		"DailyWorldSnapshot", "SpreadLogEntry+GlobalWorldStats", "SpreadLogEntry+FactionStat", "PendingRaidEvaluation", "PendingRaidEvaluation+CandidateEntry", "PendingTraderEvaluation", "PendingTraderEvaluation+CandidateEntry", "RaidTargetCandidate", "WorldActions_NpcLaunchStagger+PendingFortify", "WorldActions_NpcLaunchStagger+PendingRoad",
		"AntiAirFireUtils+PendingFlak", "AntiAirFireUtils+PendingEngage", "ThreatSettlementEntry"
	};

	private static AccessTools.FieldRef<int> nearbyRequirementTile;

	private static AccessTools.FieldRef<int> minDistanceRequirementTile;

	private static System.WeakReference<Game> nearbyRequirementGame;

	private static System.WeakReference<Game> minDistanceRequirementGame;

	private static Type settingsType;

	private static readonly Dictionary<Type, FieldInfo[]> ValueFields = new Dictionary<Type, FieldInfo[]>();

	[ThreadStatic]
	private static int settingsUiDepth;

	private static readonly HashSet<string> SettingsRecords = new HashSet<string>(StringComparer.Ordinal) { "WorldDominationSettings", "RaidOutcome", "RaidMarginShares", "RaidCasualtyEntry", "RaidSideLossEntry" };

	private static Func<object> simulationGame;

	private static Func<object, int> simulationOwnerTicks;

	private static AccessTools.FieldRef<object, object> simulationWorld;

	private static FieldInfo simulationTickingMap;

	private static MethodInfo simulationMapGetter;

	private static MethodInfo simulationMapPre;

	private static MethodInfo simulationMapPost;

	private static MethodInfo simulationWorldPre;

	private static MethodInfo simulationWorldPost;

	private static AccessTools.FieldRef<object, bool> defenseRaidLaunched;

	private static AccessTools.FieldRef<object, int> defenseRaidArrival;

	private static Action<object> launchDefenseRaid;

	private static AccessTools.FieldRef<bool> previewDontSync;

	[ThreadStatic]
	private static int localPreviewDepth;

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

	private static WorldDominationDecisionComponent Decisions => Current.Game?.GetComponent<WorldDominationDecisionComponent>();

	private static object SessionSettings
	{
		get
		{
			if (!(settingsType == null))
			{
				return Get(TypeOf("WorldDominationMod"), "settings");
			}
			return null;
		}
	}

	internal static bool HasSettings
	{
		get
		{
			if (settingsType != null)
			{
				return SessionSettings != null;
			}
			return false;
		}
	}

	private static Type TypeOf(string name)
	{
		return Types.GetOrAdd(name, (string n) => AccessTools.TypeByName("TSA_WorldDomination." + n) ?? throw new TypeLoadException("TSA_WorldDomination." + n));
	}

	public WorldDomination(ModContentPack mod)
	{
		PatchingUtilities.SetupAsyncTime();
		InstallSettings();
		InstallSimulation();
		InstallCommands();
	}

	private static MemberInfo Member(Type type, string name)
	{
		return Members.GetOrAdd((type, name), ((Type, string) key) => (MemberInfo)(((object)AccessTools.Field(key.Item1, key.Item2)) ?? ((object)(AccessTools.Property(key.Item1, key.Item2) ?? throw new MissingMemberException(key.Item1.FullName, key.Item2)))));
	}

	private static object Get(object target, string name)
	{
		if (target == null)
		{
			return null;
		}
		Type type = (target as Type) ?? target.GetType();
		object obj = ((target is Type) ? null : target);
		MemberInfo memberInfo = Member(type, name);
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				return propertyInfo.GetValue(obj);
			}
			return null;
		}
		return fieldInfo.GetValue(obj);
	}

	private static void Set(object target, string name, object value)
	{
		Type type = (target as Type) ?? target.GetType();
		object obj = ((target is Type) ? null : target);
		MemberInfo memberInfo = Member(type, name);
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				propertyInfo.SetValue(obj, value);
			}
		}
		else
		{
			fieldInfo.SetValue(obj, value);
		}
	}

	private static object Call(object target, string name, params object[] args)
	{
		Type type = (target as Type) ?? target.GetType();
		MethodInfo[] array = (from m in AccessTools.GetDeclaredMethods(type)
			where m.Name == name && m.GetParameters().Length == args.Length && m.GetParameters().Select((ParameterInfo p, int i) =>
			{
				if (args[i] != null)
				{
					return p.ParameterType.IsInstanceOfType(args[i]);
				}
				return !p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null;
			}).All((bool x) => x)
			select m).ToArray();
		if (array.Length != 1)
		{
			throw new AmbiguousMatchException($"{type.FullName}.{name}: {array.Length} matching methods");
		}
		return array[0].Invoke((target is Type) ? null : target, args);
	}

	private static void Patch(string type, string method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		Patch(AccessTools.DeclaredMethod(TypeOf(type), method) ?? throw new MissingMethodException("TSA_WorldDomination." + type, method), prefix, postfix, transpiler, finalizer);
	}

	private static void Patch(MethodBase target, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		MpCompat.harmony.Patch(target, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(WorldDomination), name);
			}
			return null;
		}
	}

	private static void InstallCommands()
	{
		InstallDecisionWorkers();
		Register("WorldObject_WD_Outpost", "SetMortarDefenseActive", "SetMortarDefenseMask", "SetMortarRaidTargetMask", "SetAntiAirKindMask", "SetAntiAirDefenseActive", "SetAntiAirGroup", "SetRapidResponseActive", "SetRapidResponseMask", "SetRapidResponseMinStrengthRatio", "SetRapidResponseMaxStrengthRatio", "SetRapidResponseRaidTargetMask", "SetRapidResponseRangeOverride", "SetMortarRangeOverride", "SetAntiAirRangeOverride", "ClearExpert", "TryAssignExpert", "SetSelectedRecruitPriority", "SetSelectedProduction", "SetSelectedScavenging", "SetSelectedHuntingAnimal", "SetSelectedFishingFish", "SetSelectedAcademySkill", "HaltRecruiting", "LetGoPrisoner", "RecruitPrisonersBatch", "ClearPendingUpgrade", "ClearPendingTypeChange", "AddCaravanPawnToOutpostRouted", "RemovePawnAsCaravan", "set_TakePrisoners", "RemovePawnsAsCaravan", "RemovePawnsAndStoredTransportAsCaravan", "RemovePawnsAndStoredTransportAndMechanoidsAsCaravan", "RemoveStoredAnimalsOrVehiclesAsCaravan");
		Register("WorldObject_AT_Turret", "SetDefenseActive", "SetDefenseMask", "SetRaidTargetMask", "SetRangeOverride", "SetDefaultAimTile");
		Register("WorldComponent_FortifyBlacklist", "AddRange", "RemoveRange", "Clear");
		Register("WorldComponent_PrisonerRecruitSchedule", "SetDest", "SetDestColony", "SetViaDropPod", "Clear", "SetSmartAssignExcluded");
		Register("WorldComponent_LogisticsManager", "SetAssignment");
		Register("CompOutpostLogistics", "ConvertInventoryToVirtualFood", "ConvertCaravanFoodToVirtualFood", "TryDissolveCaravanIntoOutpostVirtualFood");
		Register("WorldActions_Roads", "ClearRoadProject", "DestroyActiveRoadBuildersFrom");
		Register("WorldActions_RoadBlocks", "ClearRoadBlockProject");
		Register("WorldActions_SpikeTraps", "ClearSpikeTrapProject");
		Register("WorldActions_Decontamination", "ClearDecontaminationProject");
		Register("WorldActions_AtTurrets", "ClearAtTurretProject");
		Register("WorldActions_BuildBridge", "ClearBridgeProject", "BeginBridgeProject");
		Register("WorldActions_AtTurrets", "CommitAtTurretProject");
		Register("OutpostDispatchMode", "TrySetViaDropPod");
		Register("MortarFireUtils", "FireManualAtWorldTarget");
		Register("Patch_FortifyGizmo", "ExecuteFortify");
		Register("Patch_SabotageGizmo", "ExecuteSabotage");
		Register("Patch_DisinformationGizmo", "ExecuteDisinformation");
		Register("OutpostTypeChangeUtility", "TryLaunch");
		Register("OutpostUpgradeUtility", "TryPurchaseAndLaunch");
		Register("OutpostArmoryUtility", "TryStoreFromPawn", "TryDestroyFromPawn", "TryGiveStockToPawn", "TryGiveUniqueToPawn", "TryMoveThingToPawn", "TryAutoAssignFoodFromStore", "TryAutoAssignAmmoFromStore", "TryDepositAndDispose");
		Register("OutpostStorageShipping", "TryLaunch");
		Register("ColonyArmoryLaunchUtility", "TryLaunch");
		Register("Window_AllPlayerGear", "TryLaunchOutpostSelection");
		Register("OutpostStrengthBudget", "DestroyLostPawns");
		Register("PlayerPawnTransferUtility", "ExecuteAllTransfers", "ExecuteAllTransfersWithDropPodAllocation", "ExecuteOutpostGroupTransfer", "ExecuteColonyGroupTransfer", "ExecuteOutpostGroupDropPod", "ExecuteColonyGroupDropPod");
		Register("RemoteOutpostEstablishUtility", "TryLaunch");
		Register("PrisonerRosterUtility", "SetInteractionMode");
		Register("OutpostPrisonerUtility", "TryMovePrisoner", "TryMovePrisonerToExtreme");
		Register("WITab_Outpost_Pawns+<>c__DisplayClass160_1", "<DispatchSelectedPawnsToTarget>b__1");
		Register("WITab_Outpost_Pawns+<>c__DisplayClass161_1", "<DispatchSelectedPawnsToTile>b__1");
		Register("WorldObject_Traveler+<>c__DisplayClass189_0", "<GetFloatMenuOptions>b__0", "<GetFloatMenuOptions>b__1");
		Register("WD_SettlementEditUtility", "TrySpawnWdSettlementAt", "TryAdjustTierAtTile", "TryAdjustStrengthAtTile", "TryRemoveNpcSettlementAtTile");
		Register("WD_WorldRoadEditUtility", "TryPlaceRoadAlongPath", "TryRemoveRoadsAtTile", "TryPlaceBridgeBetweenBanks", "TryDestroyBridgeAtTile");
		Register("WorldComponent_RoadBlocks", "TryPlace", "TryClear");
		Register("WorldComponent_SpikeTraps", "TryPlace", "TryClear");
		Register("TravelerEndpointUtility", "AbortTraveler");
		Register("WorldActions_DiplomacyBuffsNerfs", "TrySetDiplomacyGoodwill");
		RegisterCallback("WD_WorldSetupTools+<>c", "<BeginRemoveFortification>b__33_0");
		RegisterCallback("WD_WorldSetupTools+<>c__DisplayClass23_0", "<BeginPlaceRoadBlockTargeting>b__0");
		RegisterCallback("WD_WorldSetupTools+<>c__DisplayClass26_0", "<BeginPlaceSpikeTrapTargeting>b__0");
		RegisterCallback("WD_WorldSetupTools+<>c__DisplayClass29_0", "<BeginPlaceAtTurretTargeting>b__0");
		RegisterCallback("WD_WorldSetupTools+<>c__DisplayClass31_0", "<BeginPlaceAdvancedBuildingTargeting>b__0");
		Register("WD_AssaultArtillerySupport", "TryIssueStrike");
		Register("OrderedTraderUtility", "LaunchPlayerOrderedTrader");
		Register("WD_Outpost_ConquestChoices", "DeliverRecruits", "GiveSettlementToAlly");
		Register("WdWorldDominationVictoryQuestHelper", "TryLaunchNowIfEligible");
		InstallModelFieldTransactions();
		InstallDialogCommands();
		InstallConquestCommands();
		InstallLogisticsCommands();
	}

	private static void RegisterCallback(string typeName, string methodName)
	{
		MethodInfo methodInfo = AccessTools.DeclaredMethod(TypeOf(typeName), methodName) ?? throw new MissingMethodException(typeName, methodName);
		MP.RegisterSyncMethod(methodInfo);
		Patch(methodInfo, "CommandWorldPrefix", null, null, "ScopeFinalizer");
		RegisteredCommands.Add(methodInfo);
	}

	private static void Register(string typeName, params string[] names)
	{
		Type type = TypeOf(typeName);
		foreach (string name in names)
		{
			MethodInfo[] array = (from m in AccessTools.GetDeclaredMethods(type)
				where m.Name == name
				select m).ToArray();
			if (array.Length == 0)
			{
				throw new MissingMethodException(type.FullName, name);
			}
			MethodInfo[] array2 = array;
			foreach (MethodInfo methodInfo in array2)
			{
				if (methodInfo.GetParameters().Any((ParameterInfo p) => p.ParameterType.IsByRef && !p.IsOut))
				{
					throw new NotSupportedException("WD command has ref input: " + methodInfo);
				}
				MP.RegisterSyncMethod(methodInfo);
				Patch(methodInfo, "CommandWorldPrefix", (methodInfo.ReturnType == typeof(bool)) ? "CommandBoolPostfix" : null, null, "ScopeFinalizer");
				RegisteredCommands.Add(methodInfo);
			}
		}
	}

	private static void CommandWorldPrefix(out IDisposable __state)
	{
		__state = (MP.IsExecutingSyncCommand ? SimulationWorldScope() : null);
	}

	private static void CommandBoolPostfix(MethodBase __originalMethod, object[] __args, ref bool __result)
	{
		if (Intercept)
		{
			__result = true;
		}
		else
		{
			if (__result || !MP.IsExecutingSyncCommand)
			{
				return;
			}
			ParameterInfo[] parameters = __originalMethod.GetParameters();
			for (int i = 0; i < parameters.Length; i++)
			{
				if (parameters[i].IsOut && parameters[i].ParameterType == typeof(string).MakeByRefType() && __args[i] is string text && !text.NullOrEmpty())
				{
					Messages.Message(text, MessageTypeDefOf.RejectInput, historical: false);
				}
			}
		}
	}

	private static WorldObject ResolveWorldObject(int id)
	{
		return Find.WorldObjects?.AllWorldObjects.FirstOrDefault((WorldObject w) => w.ID == id);
	}

	private static void InstallConquestCommands()
	{
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncFoundOutpost");
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncLeaveConquest");
		Patch("Dialog_OutpostSelection", "FinalizeOutpost", "FoundOutpostPrefix");
		ConstructorInfo original = AccessTools.DeclaredConstructor(TypeOf("ConquestOpportunityContext"), new Type[7]
		{
			typeof(int),
			typeof(string),
			typeof(int),
			TypeOf("SettlementTier"),
			typeof(Faction),
			typeof(int),
			typeof(bool)
		}) ?? throw new MissingMethodException("ConquestOpportunityContext", ".ctor");
		MpCompat.harmony.Patch(original, null, new HarmonyMethod(typeof(WorldDomination), "ConquestOpportunityPostfix"));
		Patch("ConquestOpportunityUtility", "OpenMenu", "BeginProcessedConquestMenu", "ConquestMenuPostfix", null, "EndProcessedConquestMenu");
		Patch("Patch_DeinitAndRemoveMap_ConquestOpportunity", "Postfix", null, null, "MapConquestContextTranspiler");
		Patch("ConquestOpportunityUtility", "TryApplyExperimentalPlayerConquestRaze", "ConquestRazePrefix");
		Patch("ConquestOpportunityUtility", "DestroyConquestRuinsAt", "LeaveConquestPrefix");
		Patch("WD_Outpost_ConquestChoices", "DeliverRecruits", "ConquestActionPrefix", "ConquestActionPostfix");
		Patch("WD_Outpost_ConquestChoices", "GiveSettlementToAlly", "ConquestActionPrefix", "ConquestActionPostfix");
	}

	private static bool ConquestRazePrefix(ref bool __result)
	{
		if (!MP.IsInMultiplayer || (!Intercept && processedConquestMenuDepth == 0))
		{
			return true;
		}
		__result = false;
		return false;
	}

	private static void ConquestOpportunityPostfix(object __instance)
	{
		if (MP.IsInMultiplayer && !Intercept && !(bool)Get(__instance, "consumed"))
		{
			Decisions?.RememberConquest(__instance);
		}
	}

	private static void ConquestMenuPostfix(object context)
	{
		if (MP.IsInMultiplayer && !Intercept && context != null && (bool)Get(context, "consumed"))
		{
			Decisions?.Conquests.RemoveAll((WorldDominationConquestDecision c) => c.tile == (int)Get(context, "tile"));
		}
	}

	private static bool FoundOutpostPrefix(object __instance, WorldObjectDef outpostDef)
	{
		if (!Intercept)
		{
			return true;
		}
		Caravan caravan = Get(__instance, "fromCaravan") as Caravan;
		SyncFoundOutpost((int)Get(__instance, "tile"), outpostDef, caravan?.ID ?? (-1));
		object obj = Get(__instance, "conquestContext");
		if (obj != null)
		{
			Set(obj, "consumed", true);
		}
		Set(__instance, "conquestChoiceConsumed", true);
		return false;
	}

	private static void SyncFoundOutpost(int tile, WorldObjectDef definition, int caravanId)
	{
		using (SimulationWorldScope())
		{
			if (definition?.worldObjectClass == null || !TypeOf("WorldObject_WD_Outpost").IsAssignableFrom(definition.worldObjectClass) || Find.WorldObjects.AllWorldObjects.Any((WorldObject w) => !w.Destroyed && w.Tile.tileId == tile && TypeOf("WorldObject_WD_Outpost").IsInstanceOfType(w)))
			{
				return;
			}
			Caravan caravan = ResolveWorldObject(caravanId) as Caravan;
			WorldDominationConquestDecision worldDominationConquestDecision = Decisions?.Conquests.FirstOrDefault((WorldDominationConquestDecision c) => c.tile == tile);
			if ((caravan == null && worldDominationConquestDecision == null) || (caravan != null && caravan.Destroyed) || (worldDominationConquestDecision != null && !ConquestTileAvailable(tile)))
			{
				return;
			}
			object obj = Enum.ToObject(TypeOf("SettlementTier"), worldDominationConquestDecision?.tier ?? 0);
			object[] array = ((caravan == null) ? new object[4] { tile, definition, obj, null } : new object[5] { tile, definition, caravan, null, true });
			if (!(bool)AccessTools.DeclaredMethod(TypeOf("Outpost_EstablishmentRequirements"), (caravan != null) ? "CanEstablishAt" : "CanEstablishAtForConquest").Invoke(null, array))
			{
				if (array[3] is string text)
				{
					Messages.Message(text, MessageTypeDefOf.RejectInput, historical: false);
				}
				return;
			}
			object obj2 = worldDominationConquestDecision?.ToNative();
			Call(Activator.CreateInstance(TypeOf("Dialog_OutpostSelection"), tile, worldDominationConquestDecision?.name ?? "", worldDominationConquestDecision?.ruinsId ?? (-1), obj, obj2, caravan, false, null, null, false), "FinalizeOutpost", definition);
			if (obj2 != null && (bool)Get(obj2, "consumed"))
			{
				Decisions.Conquests.Remove(worldDominationConquestDecision);
				CloseConquestWindows(tile);
			}
		}
	}

	private static bool LeaveConquestPrefix(int tile, int preferredRuinsId)
	{
		if (!Intercept)
		{
			return true;
		}
		SyncLeaveConquest(tile, preferredRuinsId);
		return false;
	}

	private static void SyncLeaveConquest(int tile, int ruinsId)
	{
		using (SimulationWorldScope())
		{
			WorldDominationConquestDecision worldDominationConquestDecision = Decisions?.Conquests.FirstOrDefault((WorldDominationConquestDecision c) => c.tile == tile && c.ruinsId == ruinsId);
			if (worldDominationConquestDecision != null && !worldDominationConquestDecision.bought)
			{
				Call(TypeOf("ConquestOpportunityUtility"), "DestroyConquestRuinsAt", tile, ruinsId);
				Decisions.Conquests.Remove(worldDominationConquestDecision);
				CloseConquestWindows(tile);
			}
		}
	}

	private static bool ConquestActionPrefix(int tile, MethodBase __originalMethod, object[] __args)
	{
		if (!MP.IsInMultiplayer || Intercept)
		{
			return true;
		}
		WorldDominationConquestDecision worldDominationConquestDecision = Decisions?.Conquests.FirstOrDefault((WorldDominationConquestDecision c) => c.tile == tile);
		if (worldDominationConquestDecision == null || !ConquestTileAvailable(tile))
		{
			return false;
		}
		bool flag = ((!(__originalMethod.Name == "DeliverRecruits")) ? ((int)__args[1] == worldDominationConquestDecision.ruinsId && Convert.ToInt32(__args[2]) == worldDominationConquestDecision.tier && __args[3] is Faction value && ((IEnumerable<Faction>)Call(TypeOf("WD_Outpost_ConquestChoices"), "GetEligibleGiftFactions", worldDominationConquestDecision.conqueredFaction)).Contains(value)) : (Convert.ToInt32(__args[1]) == worldDominationConquestDecision.tier && (int)__args[2] == worldDominationConquestDecision.caravanId && __args[3] == worldDominationConquestDecision.conqueredFaction && (int)__args[4] == worldDominationConquestDecision.ruinsId));
		if (flag)
		{
			Decisions.Conquests.Remove(worldDominationConquestDecision);
		}
		return flag;
	}

	private static bool ConquestTileAvailable(int tile)
	{
		object[] parameters = new object[2] { tile, null };
		return (bool)AccessTools.DeclaredMethod(TypeOf("ConquestOpportunityUtility"), "IsConquestTileStillAvailable").Invoke(null, parameters);
	}

	private static void ConquestActionPostfix(int tile)
	{
		if (MP.IsInMultiplayer && !Intercept)
		{
			WorldDominationDecisionComponent decisions = Decisions;
			if (decisions != null && !decisions.Conquests.Any((WorldDominationConquestDecision c) => c.tile == tile))
			{
				CloseConquestWindows(tile);
			}
		}
	}

	private static void CloseConquestWindows(int tile)
	{
		if (Find.WindowStack == null)
		{
			return;
		}
		Window[] array = Find.WindowStack.Windows.ToArray();
		foreach (Window window in array)
		{
			object obj = null;
			if (window.GetType() == TypeOf("Dialog_OutpostOpportunityChoices"))
			{
				obj = Get(window, "context");
			}
			else if (window.GetType() == TypeOf("Dialog_OutpostSelection") || window.GetType() == TypeOf("Dialog_ConquestAllyGift"))
			{
				obj = Get(window, "conquestContext");
			}
			if (obj != null && (int)Get(obj, "tile") == tile)
			{
				Set(obj, "consumed", true);
				if (window.GetType() == TypeOf("Dialog_OutpostSelection"))
				{
					Set(window, "conquestChoiceConsumed", true);
				}
				if (window.GetType() == TypeOf("Dialog_ConquestAllyGift"))
				{
					Set(window, "giftGiven", true);
				}
				window.Close(doCloseSound: false);
			}
		}
	}

	internal static object NativeType(string name)
	{
		return TypeOf(name);
	}

	internal static object NativeGet(object value, string name)
	{
		return Get(value, name);
	}

	internal static void ReopenDecisions(WorldDominationDecisionComponent component)
	{
		foreach (WorldDominationConquestDecision conquest in component.Conquests)
		{
			Find.WindowStack.Add((Window)Activator.CreateInstance(TypeOf("Dialog_OutpostOpportunityChoices"), conquest.ToNative(), !conquest.bought));
		}
		ReopenDefenseDecisions(component);
	}

	internal static void CaptureOpenDecisions(WorldDominationDecisionComponent component)
	{
		if (Find.WindowStack == null)
		{
			return;
		}
		foreach (Window window in Find.WindowStack.Windows)
		{
			string fullName = window.GetType().FullName;
			bool flag = !MP.IsInMultiplayer;
			if (flag)
			{
				bool flag2;
				switch (fullName)
				{
				case "TSA_WorldDomination.Dialog_OutpostOpportunityChoices":
				case "TSA_WorldDomination.Dialog_OutpostSelection":
				case "TSA_WorldDomination.Dialog_ConquestAllyGift":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag)
			{
				object obj = Get(window, (fullName == "TSA_WorldDomination.Dialog_OutpostOpportunityChoices") ? "context" : "conquestContext");
				if (obj != null && !(bool)Get(obj, "consumed"))
				{
					component.RememberConquest(obj);
				}
			}
			else if ((fullName == "TSA_WorldDomination.Dialog_OutpostDefenseChoice" || fullName == "TSA_WorldDomination.Dialog_OutpostDefenseDeploy") ? true : false)
			{
				component.RememberDefense((WorldObject)Get(window, "traveler"), (WorldObject)Get(window, "outpost"), (bool)Get(window, "isSkirmishFollowUp"));
			}
		}
	}

	private static void InstallDefenseDecisions()
	{
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncDefenseAuto");
		string[] array = new string[2] { "Dialog_OutpostDefenseChoice", "Dialog_OutpostDefenseDeploy" };
		for (int i = 0; i < array.Length; i++)
		{
			foreach (ConstructorInfo declaredConstructor in AccessTools.GetDeclaredConstructors(TypeOf(array[i])))
			{
				Patch(declaredConstructor, null, "DefenseDialogCreated");
			}
		}
		Patch("Dialog_OutpostDefenseChoice", "DoWindowContents", null, null, "DefenseAutoTranspiler");
		Patch("WorldObject_WD_Outpost", "ClearPendingSkirmishDefense", "SimulationMutationOnly");
	}

	private static void DefenseDialogCreated(object traveler, WorldObject outpost, bool isSkirmishFollowUp)
	{
		if (MP.IsInMultiplayer && !Intercept && traveler is WorldObject traveler2 && outpost != null)
		{
			Decisions?.RememberDefense(traveler2, outpost, isSkirmishFollowUp);
		}
	}

	private static WorldDominationDefenseDecision DefenseDecision(object traveler, WorldObject outpost, bool skirmish)
	{
		return Decisions?.Defenses.FirstOrDefault((WorldDominationDefenseDecision d) => d.traveler == traveler || (d.outpost == outpost && d.skirmish == skirmish));
	}

	private static IEnumerable<CodeInstruction> DefenseAutoTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.DeclaredMethod(TypeOf("Raid_Simulated"), "ResolvePlayerOutpostRaidArrival");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(WorldDomination), "ResolveDefenseDecision");
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException("WD defense auto-resolve call count changed: " + count);
		}
	}

	private static void ResolveDefenseDecision(object traveler, object manager, bool? forcedAttackerWon, bool suppressOutpostLetter, bool allowSkirmishRetry)
	{
		if (Intercept)
		{
			WorldDominationDefenseDecision worldDominationDefenseDecision = Decisions?.Defenses.FirstOrDefault((WorldDominationDefenseDecision d) => d.traveler == traveler);
			if (worldDominationDefenseDecision != null)
			{
				SyncDefenseAuto(worldDominationDefenseDecision.id);
			}
		}
		else
		{
			Call(TypeOf("Raid_Simulated"), "ResolvePlayerOutpostRaidArrival", traveler, manager, forcedAttackerWon, suppressOutpostLetter, allowSkirmishRetry);
		}
	}

	private static void SyncDefenseAuto(int id)
	{
		using (SimulationWorldScope())
		{
			WorldDominationDefenseDecision worldDominationDefenseDecision = Decisions?.Defenses.FirstOrDefault((WorldDominationDefenseDecision d) => d.id == id);
			if (worldDominationDefenseDecision?.traveler != null && worldDominationDefenseDecision.outpost != null && !worldDominationDefenseDecision.outpost.Destroyed && !(bool)Get(worldDominationDefenseDecision.outpost, "ManualDefenseActive"))
			{
				Decisions.Defenses.Remove(worldDominationDefenseDecision);
				Call(worldDominationDefenseDecision.outpost, "ClearPendingSkirmishDefense");
				object obj = RootTarget("WorldComponent_SpreadManager");
				Call(TypeOf("Raid_Simulated"), "ResolvePlayerOutpostRaidArrival", worldDominationDefenseDecision.traveler, obj, null, false, !worldDominationDefenseDecision.skirmish);
				CloseDefenseWindows(worldDominationDefenseDecision.outpost);
			}
		}
	}

	private static void CloseDefenseWindows(WorldObject outpost)
	{
		if (Find.WindowStack == null)
		{
			return;
		}
		Window[] array = Find.WindowStack.Windows.ToArray();
		foreach (Window window in array)
		{
			if ((window.GetType() == TypeOf("Dialog_OutpostDefenseChoice") || window.GetType() == TypeOf("Dialog_OutpostDefenseDeploy")) && Get(window, "outpost") == outpost)
			{
				if (window.GetType() == TypeOf("Dialog_OutpostDefenseDeploy"))
				{
					Set(window, "resolved", true);
				}
				window.Close(doCloseSound: false);
			}
		}
	}

	internal static void ReopenDefenseDecisions(WorldDominationDecisionComponent component)
	{
		object obj = RootTarget("WorldComponent_SpreadManager");
		WorldDominationDefenseDecision[] array = component.Defenses.ToArray();
		foreach (WorldDominationDefenseDecision worldDominationDefenseDecision in array)
		{
			if (worldDominationDefenseDecision.outpost != null && !worldDominationDefenseDecision.outpost.Destroyed && worldDominationDefenseDecision.traveler != null)
			{
				Find.WindowStack.Add((Window)Activator.CreateInstance(TypeOf("Dialog_OutpostDefenseChoice"), worldDominationDefenseDecision.traveler, worldDominationDefenseDecision.outpost, obj, worldDominationDefenseDecision.skirmish));
			}
		}
	}

	private static object CreateMapConquestContext(int tile, string name, int ruinsId, int tier, Faction conqueredFaction, int caravanId, bool bought)
	{
		object obj = Activator.CreateInstance(TypeOf("ConquestOpportunityContext"), tile, name, ruinsId, Enum.ToObject(TypeOf("SettlementTier"), tier), conqueredFaction, caravanId, bought);
		if (((!MP.IsInMultiplayer || Intercept) | bought) || Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
		{
			return obj;
		}
		processedConquestMenus.Add(obj, new ProcessedConquestMenu());
		Call(TypeOf("OutpostDataTracker"), "MarkFinished", tile);
		using (SimulationWorldScope())
		{
			if ((bool)Call(TypeOf("ConquestOpportunityUtility"), "TryApplyExperimentalPlayerConquestRaze", tile, name, conqueredFaction, ruinsId))
			{
				Set(obj, "consumed", true);
				Decisions?.Conquests.RemoveAll((WorldDominationConquestDecision c) => c.tile == tile);
			}
		}
		return obj;
	}

	private static void BeginProcessedConquestMenu(object context, out bool __state)
	{
		__state = MP.IsInMultiplayer && context != null && processedConquestMenus.TryGetValue(context, out var _);
		if (__state)
		{
			processedConquestMenuDepth++;
		}
	}

	private static Exception EndProcessedConquestMenu(Exception __exception, bool __state)
	{
		if (__state)
		{
			processedConquestMenuDepth--;
		}
		return __exception;
	}

	private static IEnumerable<CodeInstruction> MapConquestContextTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		Type contextType = TypeOf("ConquestOpportunityContext");
		MethodInfo factory = AccessTools.DeclaredMethod(typeof(WorldDomination), "CreateMapConquestContext");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo constructorInfo && constructorInfo.DeclaringType == contextType)
			{
				Type[] array = new Type[7]
				{
					typeof(int),
					typeof(string),
					typeof(int),
					TypeOf("SettlementTier"),
					typeof(Faction),
					typeof(int),
					typeof(bool)
				};
				if (!(from p in constructorInfo.GetParameters()
					select p.ParameterType).SequenceEqual(array) || Enum.GetUnderlyingType(array[3]) != typeof(int))
				{
					throw new InvalidOperationException("WD map conquest context signature changed");
				}
				instruction.opcode = OpCodes.Call;
				instruction.operand = factory;
				yield return instruction;
				yield return new CodeInstruction(OpCodes.Castclass, contextType);
				count++;
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException("WD map conquest context construction changed: " + count);
		}
	}

	private static void InstallDialogCommands()
	{
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncRaid");
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncDefenseDeploy");
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncTrade");
		InstallDefenseDecisions();
		Patch("Dialog_OutpostRaidMath", "ExecuteRaid", "RaidDecisionPrefix");
		Patch("Dialog_OutpostDefenseDeploy", "ConfirmFight", "DefenseDeployPrefix");
		foreach (string key in TradeKinds.Keys)
		{
			string[] array = key.Split('.');
			Patch(array[0], array[1], "TradePrefix");
		}
		Patch("SettlementBuyUtility", "CreatePaymentProbe", "PreviewPrefix", null, null, "PreviewFinalizer");
		Patch("SettlementBuyUtility", "UnitMarketValue", "PreviewPrefix", null, null, "PreviewFinalizer");
		Patch(AccessTools.DeclaredMethod(typeof(ThingDefCountClass), "ExposeData"), null, "StockExtrasSave");
	}

	private static bool RaidDecisionPrefix(object __instance, object[] __args, ref bool __result)
	{
		if (!Intercept)
		{
			return true;
		}
		if (!(Get(__instance, "source") is WorldObject worldObject) || !(Get(__instance, "target") is WorldObject worldObject2))
		{
			return false;
		}
		SyncRaid(worldObject.ID, worldObject2.ID, (bool)Get(__instance, "alliedOrder"), ((IEnumerable<int>)Get(__instance, "excludedAllyIds")).ToArray(), Convert.ToInt32(__args[0]));
		__result = true;
		return false;
	}

	private static void SyncRaid(int sourceId, int targetId, bool allied, int[] excluded, int outcome)
	{
		using (SimulationWorldScope())
		{
			WorldObject worldObject = ResolveWorldObject(sourceId);
			WorldObject worldObject2 = ResolveWorldObject(targetId);
			if (worldObject != null && worldObject2 != null && !worldObject.Destroyed && !worldObject2.Destroyed && excluded != null && excluded.Length <= 4096)
			{
				WorldObjectComp worldObjectComp = worldObject.AllComps.FirstOrDefault((WorldObjectComp c) => c.GetType() == TypeOf("CompViralSpread"));
				if (worldObjectComp != null && !(bool)Get(worldObjectComp, "IsRaidOnCooldown"))
				{
					object? target = Activator.CreateInstance(TypeOf("Dialog_OutpostRaidMath"), worldObject, worldObject2, allied);
					Set(target, "excludedAllyIds", new HashSet<int>(excluded));
					Call(target, "RecalculateFromSelection");
					Call(target, "ExecuteRaid", Enum.ToObject(TypeOf("RaidOrderOutcome"), outcome));
				}
			}
		}
	}

	private static bool DefenseDeployPrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		if (!(Get(__instance, "traveler") is WorldObject traveler) || !(Get(__instance, "outpost") is WorldObject outpost))
		{
			return false;
		}
		int[] selected = ((IEnumerable<int>)Get(__instance, "selectedIds")).ToArray();
		WorldDominationDefenseDecision worldDominationDefenseDecision = DefenseDecision(traveler, outpost, (bool)Get(__instance, "isSkirmishFollowUp"));
		if (worldDominationDefenseDecision == null)
		{
			return false;
		}
		SyncDefenseDeploy(worldDominationDefenseDecision.id, selected);
		Set(__instance, "resolved", true);
		((Window)__instance).Close(doCloseSound: false);
		return false;
	}

	private static void SyncDefenseDeploy(int decisionId, int[] selected)
	{
		using (SimulationWorldScope())
		{
			WorldDominationDefenseDecision worldDominationDefenseDecision = Decisions?.Defenses.FirstOrDefault((WorldDominationDefenseDecision d) => d.id == decisionId);
			WorldObject worldObject = worldDominationDefenseDecision?.traveler;
			WorldObject worldObject2 = worldDominationDefenseDecision?.outpost;
			if (worldObject != null && worldObject2 != null && !worldObject2.Destroyed && selected != null && selected.Length <= 4096 && !(bool)Get(worldObject2, "ManualDefenseActive"))
			{
				WorldComponent worldComponent = Find.World.components.FirstOrDefault((WorldComponent c) => c.GetType() == TypeOf("WorldComponent_SpreadManager"));
				object target = Activator.CreateInstance(TypeOf("Dialog_OutpostDefenseDeploy"), worldObject, worldObject2, worldComponent, worldDominationDefenseDecision.skirmish);
				Set(target, "selectedIds", new HashSet<int>(selected));
				List<Pawn> list = ((IEnumerable<Pawn>)Get(target, "candidates")).Where((Pawn p) => selected.Contains(p.thingIDNumber)).ToList();
				if (list.Count != 0 && (!(bool)Get(target, "enforceBudget") || !(list.Sum((Pawn p) => (float)Call(TypeOf("OutpostStrengthBudget"), "GetPawnCost", p)) > (float)Get(target, "available") + 0.001f)))
				{
					Decisions.Defenses.Remove(worldDominationDefenseDecision);
					Call(target, "ConfirmFight");
					CloseDefenseWindows(worldObject2);
				}
			}
		}
	}

	private static bool TradePrefix(MethodBase __originalMethod, object[] __args, ref bool __result)
	{
		if (!Intercept)
		{
			return true;
		}
		int num = TradeKinds[__originalMethod.DeclaringType.Name + "." + __originalMethod.Name];
		List<ThingDefCountClass> rows = __args.OfType<List<ThingDefCountClass>>().FirstOrDefault() ?? new List<ThingDefCountClass>();
		int choice = 0;
		switch (num)
		{
		case 0:
			choice = (int)__args[2];
			break;
		case 2:
		case 4:
			choice = Convert.ToInt32(__args[2]);
			break;
		}
		SyncTrade(num, __args.OfType<WorldObject>().FirstOrDefault()?.ID ?? (-1), (num == 4) ? ((Faction)__args[0]) : null, (num == 4) ? ((Faction)__args[1]) : null, choice, EncodeStock(rows));
		__args[^1] = null;
		__result = true;
		return false;
	}

	private static void SyncTrade(int kind, int targetId, Faction negotiator, Faction targetFaction, int choice, byte[] payload)
	{
		if (payload == null || payload.Length > 65536 || kind < 0 || kind > 4)
		{
			return;
		}
		using (SimulationWorldScope())
		{
			List<ThingDefCountClass> list = DecodeStock(payload);
			WorldObject worldObject = ResolveWorldObject(targetId);
			if (kind == 4 || (worldObject != null && !worldObject.Destroyed))
			{
				string name;
				string name2;
				object[] array;
				switch (kind)
				{
				case 0:
					name = "SettlementBuyUtility";
					name2 = "TryLaunchBuy";
					array = new object[4] { worldObject, list, choice, null };
					break;
				case 1:
					name = "SettlementGiftUtility";
					name2 = "TryLaunchGift";
					array = new object[3] { worldObject, list, null };
					break;
				case 2:
					name = "SettlementBribeUtility";
					name2 = "TryLaunchSettlementBribe";
					array = new object[4]
					{
						worldObject,
						list,
						Enum.ToObject(TypeOf("SettlementBribeUtility+BribeDurationPackage"), choice),
						null
					};
					break;
				case 3:
					name = "SettlementBribeUtility";
					name2 = "TryLaunchRaidBribe";
					array = new object[3] { worldObject, list, null };
					break;
				default:
					name = "DiplomacyNegotiateUtility";
					name2 = "TryLaunch";
					array = new object[5]
					{
						negotiator,
						targetFaction,
						Enum.ToObject(TypeOf("DiplomacyNegotiateAction"), choice),
						list,
						null
					};
					break;
				}
				if (!(bool)AccessTools.DeclaredMethod(TypeOf(name), name2).Invoke(null, array) && array[^1] is string text && !text.NullOrEmpty())
				{
					Messages.Message(text, MessageTypeDefOf.RejectInput, historical: false);
				}
			}
		}
	}

	private static byte[] EncodeStock(List<ThingDefCountClass> rows)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(rows.Count);
		foreach (ThingDefCountClass row in rows)
		{
			binaryWriter.Write(row.thingDef?.defName ?? "");
			binaryWriter.Write(row.stuff?.defName ?? "");
			binaryWriter.Write(row.count);
			binaryWriter.Write((int)row.quality);
			binaryWriter.Write(row.color.HasValue);
			Color? color = row.color;
			if (color.HasValue)
			{
				Color valueOrDefault = color.GetValueOrDefault();
				binaryWriter.Write(valueOrDefault.r);
				binaryWriter.Write(valueOrDefault.g);
				binaryWriter.Write(valueOrDefault.b);
				binaryWriter.Write(valueOrDefault.a);
			}
			binaryWriter.Write(row.chance.HasValue);
			float? chance = row.chance;
			if (chance.HasValue)
			{
				float valueOrDefault2 = chance.GetValueOrDefault();
				binaryWriter.Write(valueOrDefault2);
			}
			binaryWriter.Write((bool)Call(TypeOf("WdStockExtrasTable"), "GetWornByCorpse", row));
			binaryWriter.Write((bool)Call(TypeOf("WdStockExtrasTable"), "GetBiocoded", row));
			binaryWriter.Write((int)Call(TypeOf("WdStockExtrasTable"), "GetHitPoints", row));
		}
		return memoryStream.ToArray();
	}

	private static List<ThingDefCountClass> DecodeStock(byte[] data)
	{
		using MemoryStream memoryStream = new MemoryStream(data);
		using BinaryReader binaryReader = new BinaryReader(memoryStream);
		int num = binaryReader.ReadInt32();
		if (num < 0 || num > 4096)
		{
			throw new InvalidDataException("Invalid WD stock size");
		}
		List<ThingDefCountClass> list = new List<ThingDefCountClass>(num);
		for (int i = 0; i < num; i++)
		{
			string defName = binaryReader.ReadString();
			string text = binaryReader.ReadString();
			ThingDefCountClass thingDefCountClass = new ThingDefCountClass(DefDatabase<ThingDef>.GetNamedSilentFail(defName), binaryReader.ReadInt32())
			{
				stuff = ((text.Length == 0) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(text)),
				quality = (QualityCategory)binaryReader.ReadInt32()
			};
			if (binaryReader.ReadBoolean())
			{
				thingDefCountClass.color = new Color(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
			}
			if (binaryReader.ReadBoolean())
			{
				thingDefCountClass.chance = binaryReader.ReadSingle();
			}
			bool worn = binaryReader.ReadBoolean();
			bool biocoded = binaryReader.ReadBoolean();
			int hp = binaryReader.ReadInt32();
			if (thingDefCountClass.thingDef == null || thingDefCountClass.count <= 0 || thingDefCountClass.count > 100000000)
			{
				throw new InvalidDataException("Invalid WD stock row");
			}
			ApplyStockExtras(thingDefCountClass, worn, biocoded, hp);
			list.Add(thingDefCountClass);
		}
		if (memoryStream.Position != memoryStream.Length)
		{
			throw new InvalidDataException("Trailing WD stock data");
		}
		return list;
	}

	private static void ApplyStockExtras(ThingDefCountClass row, bool worn, bool biocoded, int hp)
	{
		if (worn || biocoded || hp >= 0)
		{
			object target = Call(TypeOf("WdStockExtrasTable"), "GetOrCreate", row);
			Set(target, "wornByCorpse", worn);
			Set(target, "biocoded", biocoded);
			Set(target, "hitPoints", hp);
		}
	}

	private static void StockExtrasSave(ThingDefCountClass __instance)
	{
		if (MP.IsInMultiplayer || Scribe.mode == LoadSaveMode.Saving)
		{
			bool value = (bool)Call(TypeOf("WdStockExtrasTable"), "GetWornByCorpse", __instance);
			bool value2 = (bool)Call(TypeOf("WdStockExtrasTable"), "GetBiocoded", __instance);
			int value3 = (int)Call(TypeOf("WdStockExtrasTable"), "GetHitPoints", __instance);
			Scribe_Values.Look(ref value, "mpWdStockWorn", defaultValue: false);
			Scribe_Values.Look(ref value2, "mpWdStockBiocoded", defaultValue: false);
			Scribe_Values.Look(ref value3, "mpWdStockHitPoints", -1);
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				ApplyStockExtras(__instance, value, value2, value3);
			}
		}
	}

	private static void InstallGoodwillPulse()
	{
		goodwillPulseTick = AccessTools.StaticFieldRefAccess<int>(AccessTools.DeclaredField(TypeOf("EscalationGoodwillDrain"), "lastPulseTick"));
		Patch("EscalationGoodwillDrain", "TryPulse", "GoodwillPulsePrefix", null, null, "GoodwillPulseFinalizer");
		Patch("EscalationGoodwillDrain", "ResetPulseClock", null, "GoodwillPulseReset");
	}

	internal static int GetGoodwillPulseTick()
	{
		return goodwillPulseTick();
	}

	internal static void SetGoodwillPulseTick(int tick)
	{
		goodwillPulseTick() = tick;
	}

	internal static int MigrateLegacyGoodwillPulse()
	{
		int num = ReadWorldTicks(Find.TickManager);
		if (num >= 0)
		{
			return num - num % 60000;
		}
		return -1;
	}

	private static void GoodwillPulsePrefix(out WorldDominationSimulationComponent __state)
	{
		__state = null;
		WorldDominationSimulationComponent worldDominationSimulationComponent = ((!MP.IsInMultiplayer) ? null : Current.Game?.GetComponent<WorldDominationSimulationComponent>());
		worldDominationSimulationComponent?.PrepareGoodwillPulse();
		__state = worldDominationSimulationComponent;
	}

	private static Exception GoodwillPulseFinalizer(Exception __exception, WorldDominationSimulationComponent __state)
	{
		__state?.CaptureGoodwillPulse();
		return __exception;
	}

	private static void GoodwillPulseReset()
	{
		if (MP.IsInMultiplayer)
		{
			Current.Game?.GetComponent<WorldDominationSimulationComponent>()?.CaptureGoodwillPulse();
		}
	}

	private static void InstallLogisticsCommands()
	{
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncLogistics");
		Patch("WITab_Outpost_Logistics", "SetLinkAssignment", "LogisticsAssignmentPrefix");
		Patch("WITab_Outpost_Logistics", "ResetAllocationToManualZero", "LogisticsResetPrefix");
		Patch("WorldComponent_LogisticsManager", "MergeDuplicateManualLinksFromSource", "SimulationMutationOnly");
	}

	private static bool SimulationMutationOnly()
	{
		return !Intercept;
	}

	private static bool LogisticsAssignmentPrefix(object __instance, int targetTile, float amount)
	{
		if (!Intercept)
		{
			return true;
		}
		if (Get(__instance, "SelOutpost") is WorldObject worldObject)
		{
			SyncLogistics(worldObject.ID, targetTile, amount, reset: false);
		}
		return false;
	}

	private static bool LogisticsResetPrefix(object __instance)
	{
		if (!Intercept)
		{
			return true;
		}
		if (Get(__instance, "SelOutpost") is WorldObject worldObject)
		{
			SyncLogistics(worldObject.ID, -1, 0f, reset: true);
		}
		return false;
	}

	private static void SyncLogistics(int sourceId, int destinationTile, float amount, bool reset)
	{
		using (SimulationWorldScope())
		{
			WorldObject worldObject = ResolveWorldObject(sourceId);
			if (worldObject == null || worldObject.Destroyed || !float.IsFinite(amount))
			{
				return;
			}
			WorldObjectComp worldObjectComp = worldObject.AllComps.FirstOrDefault((WorldObjectComp c) => c.GetType() == TypeOf("CompOutpostLogistics"));
			object obj = RootTarget("WorldComponent_LogisticsManager");
			if (worldObjectComp == null || obj == null || obj is Type)
			{
				return;
			}
			Set(worldObjectComp, "mode", Enum.ToObject(TypeOf("LogisticsMode"), 0));
			int tileId = worldObject.Tile.tileId;
			if (reset)
			{
				foreach (object item in (IEnumerable)Get(obj, "manualLinks"))
				{
					if ((int)Get(item, "sourceTile") == tileId)
					{
						Set(item, "manualAssignment", 0f);
					}
				}
				Call(obj, "NotifyManualLinksChanged");
			}
			else
			{
				Call(obj, "SetAssignment", tileId, destinationTile, (float)Math.Round(amount, 1), true);
				Call(obj, "ClampManualAssignmentsForSourceBudget", tileId);
			}
			Call(worldObject, "RecomputeProductionRequirementCache");
		}
	}

	private static void InstallModelFieldTransactions()
	{
		AddCommandFields("CompViralSpread", "autoFeedTransferredPawns", "selectedRoadTier", "roadIsClearing", "roadTargetTile", "roadProgress", "cachedRoadPathTiles", "roadWaypointTiles", "lastPathSourceTile", "roadTargetUsesDetachedStart", "cachedWorkTile", "roadTargetName", "roadBlockPlannedTiles", "roadBlockClickedNodes", "roadBlockCachedPathTiles", "roadBlockWorkIndex", "roadBlockProgress", "roadBlockIsClearing", "roadBlockClearAnyFortification", "roadBlockTargetName", "selectedRoadBlockKind", "roadBlockCachedWorkTile", "spikeTrapPlannedTiles", "spikeTrapClickedNodes", "spikeTrapCachedPathTiles", "spikeTrapWorkIndex", "spikeTrapProgress", "spikeTrapIsClearing", "spikeTrapTargetName", "selectedSpikeTrapKind", "spikeTrapCachedWorkTile", "decontamPlannedTiles", "decontamClickedNodes", "decontamCachedPathTiles", "decontamWorkIndex", "decontamProgress", "decontamTargetName", "decontamCachedWorkTile", "redirectionTargetTile");
		AddCommandFields("CompOutpostWarehouse", "autoShipEnabled", "autoShipFood", "autoShipWeapons", "autoShipApparel", "autoShipDrugs", "autoShipMedicine", "shipDestinationWorldObjectId", "dispatchViaDropPod");
		AddCommandFields("CompPlayerDispatchMode", "dispatchViaDropPod", "dropPodFallbackToLand", "pawnTravelViaDropPod");
		AddCommandFields("CompOutpostLogistics", "mode");
		AddCommandFields("WorldObject_WD_Outpost", "Name", "autoAssignExpertsOnSlotUnlock", "itemDeliveryTargetWorldObjectId", "dropPodFallbackToLand", "pawnTravelViaDropPod");
		AddCommandFields("WorldComponent_PrisonerRecruitSchedule", "autoSmartAssignNewPrisoners");
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncModelFields");
		foreach (Type item in from t in settingsType.Assembly.GetTypes()
			where !t.ContainsGenericParameters && IsFieldUiType(t)
			select t)
		{
			foreach (MethodInfo item2 in from m in AccessTools.GetDeclaredMethods(item)
				where !m.ContainsGenericParameters && m.GetMethodBody() != null
				select m)
			{
				if (PatchProcessor.GetOriginalInstructions(item2).Any((CodeInstruction i) => i.opcode == OpCodes.Stfld && i.operand is FieldInfo field && CommandFields.ContainsKey(FieldKey(field))))
				{
					Patch(item2, "BeforeModelWrites", null, "ModelWritesTranspiler", "AfterModelWrites");
				}
			}
		}
	}

	private static bool IsFieldUiType(Type type)
	{
		while (type.DeclaringType != null)
		{
			type = type.DeclaringType;
		}
		bool flag = type.Namespace == "TSA_WorldDomination.".TrimEnd('.');
		if (flag)
		{
			bool flag2 = type.Name.StartsWith("Action_Outpost_") || type.Name.StartsWith("Action_Settlement_") || type.Name.StartsWith("WITab_") || type.Name.StartsWith("Dialog_") || type.Name.StartsWith("Window_");
			if (!flag2)
			{
				bool flag3;
				switch (type.Name)
				{
				case "Outpost_DispatchMode_Gizmos":
				case "Outpost_Warehouse_Gizmos":
				case "OutpostLogisticsGizmos":
				case "PlayerPawnDropPodUtility":
				case "Outpost_Production":
				case "Outpost_Warehouse_Delivery":
					flag3 = true;
					break;
				default:
					flag3 = false;
					break;
				}
				flag2 = flag3;
			}
			flag = flag2;
		}
		return flag;
	}

	private static string FieldKey(FieldInfo field)
	{
		return field.DeclaringType.FullName + ":" + field.Name;
	}

	private static void AddCommandFields(string name, params string[] fields)
	{
		foreach (string text in fields)
		{
			FieldInfo fieldInfo = AccessTools.DeclaredField(TypeOf(name), text) ?? throw new MissingFieldException("TSA_WorldDomination." + name, text);
			if (!fieldInfo.FieldType.IsEnum && !(fieldInfo.FieldType == typeof(bool)) && !(fieldInfo.FieldType == typeof(int)) && !(fieldInfo.FieldType == typeof(float)) && !(fieldInfo.FieldType == typeof(string)) && !(fieldInfo.FieldType == typeof(List<int>)))
			{
				throw new NotSupportedException("Unreviewed WD command field " + fieldInfo);
			}
			CommandFields.Add(FieldKey(fieldInfo), fieldInfo);
		}
	}

	private static void BeforeModelWrites(MethodBase __originalMethod, out bool __state)
	{
		__state = Intercept;
		if (__state)
		{
			if (activeFieldEdits == null)
			{
				activeFieldEdits = new FieldEditBatch();
			}
			activeFieldEdits.Submit |= !__originalMethod.Name.StartsWith("Ensure", StringComparison.Ordinal);
			activeFieldEdits.Depth++;
		}
	}

	private static void StageFieldWrite<T>(object target, T value, RuntimeFieldHandle handle)
	{
		FieldInfo field = FieldInfo.GetFieldFromHandle(handle);
		if (Intercept && activeFieldEdits != null && !activeFieldEdits.Edits.Any((FieldEdit e) => e.Target == target && e.Field == field))
		{
			activeFieldEdits.Edits.Add(new FieldEdit
			{
				Target = target,
				Field = field,
				Before = field.GetValue(target)
			});
		}
		field.SetValue(target, value);
	}

	private static IEnumerable<CodeInstruction> ModelWritesTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo setter = AccessTools.DeclaredMethod(typeof(WorldDomination), "StageFieldWrite");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Stfld)
			{
				object operand = instruction.operand;
				if (operand is FieldInfo field && CommandFields.ContainsKey(FieldKey(field)))
				{
					CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldtoken, field);
					codeInstruction.labels.AddRange(instruction.labels);
					instruction.labels.Clear();
					codeInstruction.blocks.AddRange(instruction.blocks);
					instruction.blocks.Clear();
					yield return codeInstruction;
					instruction.opcode = OpCodes.Call;
					instruction.operand = setter.MakeGenericMethod(field.FieldType);
				}
			}
			yield return instruction;
		}
	}

	private static Exception AfterModelWrites(Exception __exception, bool __state)
	{
		if (!__state || activeFieldEdits == null || --activeFieldEdits.Depth != 0)
		{
			return __exception;
		}
		FieldEditBatch fieldEditBatch = activeFieldEdits;
		activeFieldEdits = null;
		List<int> list = new List<int>();
		List<string> list2 = new List<string>();
		List<byte[]> list3 = new List<byte[]>();
		try
		{
			foreach (FieldEdit edit in fieldEditBatch.Edits)
			{
				object value = edit.Field.GetValue(edit.Target);
				if (object.Equals(value, edit.Before) || (value is List<int> first && edit.Before is List<int> second && first.SequenceEqual(second)))
				{
					continue;
				}
				using MemoryStream memoryStream = new MemoryStream();
				using BinaryWriter writer = new BinaryWriter(memoryStream);
				WriteSettingsValue(writer, edit.Field.FieldType, value);
				object target = edit.Target;
				int num;
				if (!(target is WorldObject worldObject))
				{
					if (!(target is WorldObjectComp worldObjectComp))
					{
						if (!(target is WorldComponent))
						{
							throw new NotSupportedException("WD field target " + edit.Target.GetType());
						}
						num = -1;
					}
					else
					{
						num = worldObjectComp.parent.ID;
					}
				}
				else
				{
					num = worldObject.ID;
				}
				int item = num;
				list.Add(item);
				list2.Add(FieldKey(edit.Field));
				list3.Add(memoryStream.ToArray());
			}
		}
		finally
		{
			foreach (FieldEdit edit2 in fieldEditBatch.Edits)
			{
				edit2.Field.SetValue(edit2.Target, edit2.Before);
			}
		}
		if (__exception == null && fieldEditBatch.Submit && list.Count > 0)
		{
			SyncModelFields(list.ToArray(), list2.ToArray(), list3.ToArray());
		}
		return __exception;
	}

	private static void SyncModelFields(int[] ids, string[] keys, byte[][] values)
	{
		if (ids == null || keys == null || values == null || ids.Length != keys.Length || ids.Length != values.Length || ids.Length > 128)
		{
			return;
		}
		List<(object, FieldInfo, object)> list = new List<(object, FieldInfo, object)>();
		for (int i = 0; i < ids.Length; i++)
		{
			if (!CommandFields.TryGetValue(keys[i], out var field) || values[i] == null || values[i].Length > 65536)
			{
				return;
			}
			object obj;
			if (typeof(WorldComponent).IsAssignableFrom(field.DeclaringType))
			{
				obj = Find.World?.components.FirstOrDefault((WorldComponent c) => c.GetType() == field.DeclaringType);
			}
			else
			{
				WorldObject worldObject = ResolveWorldObject(ids[i]);
				if (worldObject == null || worldObject.Destroyed)
				{
					return;
				}
				obj = (typeof(WorldObjectComp).IsAssignableFrom(field.DeclaringType) ? ((object)worldObject.AllComps.FirstOrDefault((WorldObjectComp c) => c.GetType() == field.DeclaringType)) : ((object)worldObject));
			}
			if (obj == null || !field.DeclaringType.IsInstanceOfType(obj))
			{
				return;
			}
			using MemoryStream memoryStream = new MemoryStream(values[i]);
			using BinaryReader reader = new BinaryReader(memoryStream);
			object item = ReadSettingsValue(reader, field.FieldType);
			if (memoryStream.Position != memoryStream.Length)
			{
				return;
			}
			list.Add((obj, field, item));
		}
		using (SimulationWorldScope())
		{
			foreach (var item2 in list)
			{
				item2.Item2.SetValue(item2.Item1, item2.Item3);
			}
			if (list.Any(((object Target, FieldInfo Field, object Value) e) => e.Target is WorldObjectComp))
			{
				WorldComponent worldComponent = Find.World?.components.FirstOrDefault((WorldComponent c) => c.GetType() == TypeOf("WorldComponent_LogisticsManager"));
				if (worldComponent != null)
				{
					Call(worldComponent, "NotifyFoodLogisticsInputsChanged");
				}
			}
		}
	}

	private static object RootTarget(string name)
	{
		Type type = TypeOf(name);
		if (!typeof(WorldComponent).IsAssignableFrom(type))
		{
			return type;
		}
		return Find.World?.components.FirstOrDefault((WorldComponent c) => c.GetType() == type);
	}

	internal static byte[] CaptureSimulation(out List<ILoadReferenceable> references)
	{
		StateGraph stateGraph = new StateGraph();
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(1);
		binaryWriter.Write(SimulationRoots.Length);
		(string, string)[] simulationRoots = SimulationRoots;
		for (int i = 0; i < simulationRoots.Length; i++)
		{
			(string, string) tuple = simulationRoots[i];
			stateGraph.Write(binaryWriter, Get(RootTarget(tuple.Item1), tuple.Item2));
		}
		references = stateGraph.References;
		return memoryStream.ToArray();
	}

	internal static void RestoreSimulation(byte[] data, List<ILoadReferenceable> references)
	{
		if (data == null)
		{
			return;
		}
		StateGraph graph = new StateGraph(references);
		using MemoryStream memoryStream = new MemoryStream(data);
		BinaryReader reader = new BinaryReader(memoryStream);
		try
		{
			if (reader.ReadInt32() != 1 || reader.ReadInt32() != SimulationRoots.Length)
			{
				throw new InvalidDataException("WD simulation snapshot schema changed");
			}
			object[] array = SimulationRoots.Select(((string Type, string Field) _) => graph.Read(reader)).ToArray();
			if (memoryStream.Position != memoryStream.Length)
			{
				throw new InvalidDataException("Trailing WD simulation snapshot data");
			}
			for (int num = 0; num < SimulationRoots.Length; num++)
			{
				(string, string) tuple = SimulationRoots[num];
				object obj = RootTarget(tuple.Item1);
				FieldInfo fieldInfo = AccessTools.DeclaredField(TypeOf(tuple.Item1), tuple.Item2);
				if (!fieldInfo.IsStatic && obj == null && array[num] == null)
				{
					continue;
				}
				if (fieldInfo.IsInitOnly)
				{
					object value = fieldInfo.GetValue(fieldInfo.IsStatic ? null : obj);
					value.GetType().GetMethod("Clear").Invoke(value, null);
					if (value is IDictionary dictionary)
					{
						foreach (DictionaryEntry item in (IDictionary)array[num])
						{
							dictionary.Add(item.Key, item.Value);
						}
						continue;
					}
					MethodInfo method = value.GetType().GetMethod("Add");
					foreach (object item2 in (IEnumerable)array[num])
					{
						method.Invoke(value, new object[1] { item2 });
					}
				}
				else
				{
					fieldInfo.SetValue((obj is Type) ? null : obj, array[num]);
				}
			}
		}
		finally
		{
			if (reader != null)
			{
				((IDisposable)reader).Dispose();
			}
		}
	}

	private static void InstallRequirementQueries()
	{
		Type type = TypeOf("Outpost_EstablishmentRequirements");
		nearbyRequirementTile = AccessTools.StaticFieldRefAccess<int>(AccessTools.DeclaredField(type, "cachedNearbyTile"));
		minDistanceRequirementTile = AccessTools.StaticFieldRefAccess<int>(AccessTools.DeclaredField(type, "cachedMinDistTile"));
		Patch("Outpost_EstablishmentRequirements", "CountNearbySettlements", null, "NearbyRequirementQueried", "RequirementMemoTranspiler");
		Patch("Outpost_EstablishmentRequirements", "MeetsMinDistance", null, "MinDistanceRequirementQueried", "RequirementMemoTranspiler");
	}

	private static bool RequirementMemoAvailable(System.WeakReference<Game> owner)
	{
		if (MP.IsInMultiplayer)
		{
			if (Intercept && owner != null && owner.TryGetTarget(out var target))
			{
				return target == Current.Game;
			}
			return false;
		}
		return true;
	}

	private static int NearbyRequirementMemoTile()
	{
		if (!RequirementMemoAvailable(nearbyRequirementGame))
		{
			return -1;
		}
		return nearbyRequirementTile();
	}

	private static int MinDistanceRequirementMemoTile()
	{
		if (!RequirementMemoAvailable(minDistanceRequirementGame))
		{
			return -1;
		}
		return minDistanceRequirementTile();
	}

	private static void RememberRequirementGame(ref System.WeakReference<Game> owner)
	{
		if (owner == null)
		{
			owner = new System.WeakReference<Game>(Current.Game);
		}
		else
		{
			owner.SetTarget(Current.Game);
		}
	}

	private static void NearbyRequirementQueried(int tile, int radiusTiles)
	{
		if (MP.IsInMultiplayer && Find.WorldGrid != null && tile >= 0 && radiusTiles > 0)
		{
			RememberRequirementGame(ref nearbyRequirementGame);
		}
	}

	private static void MinDistanceRequirementQueried(int tile)
	{
		if (MP.IsInMultiplayer && Find.WorldGrid != null && tile >= 0)
		{
			RememberRequirementGame(ref minDistanceRequirementGame);
		}
	}

	private static IEnumerable<CodeInstruction> RequirementMemoTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		bool flag = __originalMethod.Name == "CountNearbySettlements";
		FieldInfo field = AccessTools.DeclaredField(TypeOf("Outpost_EstablishmentRequirements"), flag ? "cachedNearbyTile" : "cachedMinDistTile");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(WorldDomination), flag ? "NearbyRequirementMemoTile" : "MinDistanceRequirementMemoTile");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Ldsfld && object.Equals(instruction.operand, field))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException("WD requirement memo key changed: " + __originalMethod?.ToString() + " (" + count + ")");
		}
	}

	private static void InstallSettings()
	{
		settingsType = TypeOf("WorldDominationSettings");
		ValidateSettingsValue(settingsType, new HashSet<Type>());
		MP.RegisterSyncMethod(typeof(WorldDomination), "SyncSettings");
		Patch("WorldDominationMod", "DoSettingsWindowContents", "BeforeSettingsUI", null, null, "AfterSettingsUI");
		Patch("WorldDominationMod", "SaveSettingsToDisk", "SaveSettingsPrefix");
		foreach (Type item in settingsType.Assembly.GetTypes().Where((Type t) =>
		{
			bool flag = t.Namespace == "TSA_WorldDomination.".TrimEnd('.');
			if (flag)
			{
				bool flag2 = t.Name.StartsWith("Dialog_") && t.Name.Contains("Settings");
				if (!flag2)
				{
					string name = t.Name;
					bool flag3 = ((name == "Dialog_WdWorldGenAllegiances" || name == "WorldDominationMod") ? true : false);
					flag2 = flag3;
				}
				flag = flag2;
			}
			return flag;
		}))
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(item, "DoWindowContents");
			if (methodInfo != null)
			{
				Patch(methodInfo, "BeforeSettingsUI", null, null, "AfterSettingsUI");
			}
			Type[] nestedTypes = item.GetNestedTypes(AccessTools.all);
			for (int num = 0; num < nestedTypes.Length; num++)
			{
				foreach (MethodInfo item2 in from m in AccessTools.GetDeclaredMethods(nestedTypes[num])
					where m.Name.Contains("b__") && m.ReturnType == typeof(void)
					select m)
				{
					Patch(item2, "BeforeSettingsUI", null, null, "AfterSettingsUI");
				}
			}
		}
	}

	private static bool SaveSettingsPrefix()
	{
		if (MP.IsInMultiplayer)
		{
			return MP.IsExecutingSyncCommand;
		}
		return true;
	}

	private static void BeforeSettingsUI(out byte[] __state)
	{
		__state = null;
		if (Intercept && settingsUiDepth++ == 0)
		{
			__state = CaptureSettings();
		}
	}

	private static Exception AfterSettingsUI(Exception __exception, byte[] __state)
	{
		if (settingsUiDepth > 0)
		{
			settingsUiDepth--;
		}
		if (__state == null)
		{
			return __exception;
		}
		byte[] array = CaptureSettings();
		ApplySettings(__state);
		if (__exception == null && !array.SequenceEqual(__state))
		{
			SyncSettings(array);
		}
		return __exception;
	}

	private static void SyncSettings(byte[] data)
	{
		if (data != null && data.Length <= 65536 && HasSettings)
		{
			ApplySettings(data);
			Call(SessionSettings, "InvalidateRaidOutcomesCache");
			WorldComponent worldComponent = Find.World?.components.FirstOrDefault((WorldComponent c) => c.GetType() == TypeOf("WorldComponent_SpreadManager"));
			if (worldComponent != null)
			{
				Call(worldComponent, "NotifyInfluenceSettingsChanged");
			}
			object obj = Get(TypeOf("WorldComponent_InterceptionScheduler"), "Current");
			if (obj != null)
			{
				Call(obj, "NotifyAtTurretRangeSettingsChanged");
			}
			Call(TypeOf("WorldActions_Roads"), "ApplyVanillaRoadMovementSettings");
		}
	}

	private static FieldInfo[] FieldsOf(Type type)
	{
		if (!ValueFields.TryGetValue(type, out var value))
		{
			value = (ValueFields[type] = (from f in AccessTools.GetDeclaredFields(type)
				where !f.IsStatic && !f.IsLiteral && !f.Name.StartsWith("_cached", StringComparison.Ordinal)
				select f).OrderBy((FieldInfo f) => f.Name, StringComparer.Ordinal).ToArray());
		}
		return value;
	}

	private static void ValidateSettingsValue(Type type, HashSet<Type> visited)
	{
		if (type.IsPrimitive || type.IsEnum || type == typeof(string))
		{
			return;
		}
		if (type.IsArray)
		{
			ValidateSettingsValue(type.GetElementType(), visited);
			return;
		}
		if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(HashSet<>) || type.GetGenericTypeDefinition() == typeof(Dictionary<, >)))
		{
			Type[] genericArguments = type.GetGenericArguments();
			for (int i = 0; i < genericArguments.Length; i++)
			{
				ValidateSettingsValue(genericArguments[i], visited);
			}
			return;
		}
		if (type.Namespace != "TSA_WorldDomination.".TrimEnd('.') || !SettingsRecords.Contains(type.Name))
		{
			throw new NotSupportedException("Unreviewed WD settings type: " + type);
		}
		if (visited.Add(type))
		{
			FieldInfo[] array = FieldsOf(type);
			for (int i = 0; i < array.Length; i++)
			{
				ValidateSettingsValue(array[i].FieldType, visited);
			}
		}
	}

	internal static byte[] CaptureSettings()
	{
		if (!HasSettings)
		{
			return null;
		}
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter writer = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true);
		WriteSettingsValue(writer, settingsType, SessionSettings);
		return memoryStream.ToArray();
	}

	internal static void ApplySettings(byte[] data)
	{
		if (!HasSettings || data == null)
		{
			return;
		}
		using MemoryStream memoryStream = new MemoryStream(data);
		using BinaryReader reader = new BinaryReader(memoryStream, Encoding.UTF8, leaveOpen: true);
		object obj = ReadSettingsValue(reader, settingsType);
		if (memoryStream.Position != memoryStream.Length)
		{
			throw new InvalidDataException("Trailing WD settings data");
		}
		FieldInfo[] array = FieldsOf(settingsType);
		foreach (FieldInfo fieldInfo in array)
		{
			fieldInfo.SetValue(SessionSettings, fieldInfo.GetValue(obj));
		}
		Set(SessionSettings, "_cachedSortedRaidOutcomes", null);
	}

	private static void WriteSettingsValue(BinaryWriter writer, Type type, object value)
	{
		if (!type.IsValueType)
		{
			writer.Write(value != null);
			if (value == null)
			{
				return;
			}
		}
		if (type.IsEnum)
		{
			writer.Write(Convert.ToInt32(value));
		}
		else if (type == typeof(bool))
		{
			writer.Write((bool)value);
		}
		else if (type == typeof(int))
		{
			writer.Write((int)value);
		}
		else if (type == typeof(float))
		{
			writer.Write((float)value);
		}
		else if (type == typeof(string))
		{
			writer.Write((string)value);
		}
		else if (type.IsArray || type.IsGenericType)
		{
			object[] array = ((IEnumerable)value).Cast<object>().ToArray();
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
			{
				array = array.OrderBy((object x) => x, Comparer<object>.Default).ToArray();
			}
			if (value is IDictionary dictionary)
			{
				string[] array2 = dictionary.Keys.Cast<string>().OrderBy((string x) => x, StringComparer.Ordinal).ToArray();
				writer.Write(array2.Length);
				string[] array3 = array2;
				foreach (string text in array3)
				{
					WriteSettingsValue(writer, typeof(string), text);
					WriteSettingsValue(writer, typeof(float), dictionary[text]);
				}
			}
			else
			{
				writer.Write(array.Length);
				Type type2 = (type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0]);
				object[] array4 = array;
				foreach (object value2 in array4)
				{
					WriteSettingsValue(writer, type2, value2);
				}
			}
		}
		else
		{
			FieldInfo[] array5 = FieldsOf(type);
			foreach (FieldInfo fieldInfo in array5)
			{
				WriteSettingsValue(writer, fieldInfo.FieldType, fieldInfo.GetValue(value));
			}
		}
	}

	private static object ReadSettingsValue(BinaryReader reader, Type type)
	{
		if (!type.IsValueType && !reader.ReadBoolean())
		{
			return null;
		}
		if (type.IsEnum)
		{
			return Enum.ToObject(type, reader.ReadInt32());
		}
		if (type == typeof(bool))
		{
			return reader.ReadBoolean();
		}
		if (type == typeof(int))
		{
			return reader.ReadInt32();
		}
		if (type == typeof(float))
		{
			return reader.ReadSingle();
		}
		if (type == typeof(string))
		{
			return reader.ReadString();
		}
		if (type.IsArray || type.IsGenericType)
		{
			int num = reader.ReadInt32();
			if (num < 0 || num > 10000)
			{
				throw new InvalidDataException("Invalid WD settings collection size");
			}
			Type type2 = (type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0]);
			if (type.IsArray)
			{
				Array array = Array.CreateInstance(type2, num);
				for (int i = 0; i < num; i++)
				{
					array.SetValue(ReadSettingsValue(reader, type2), i);
				}
				return array;
			}
			object obj = Activator.CreateInstance(type);
			if (obj is IDictionary dictionary)
			{
				for (int j = 0; j < num; j++)
				{
					dictionary.Add(ReadSettingsValue(reader, typeof(string)), ReadSettingsValue(reader, typeof(float)));
				}
				return dictionary;
			}
			MethodInfo method = type.GetMethod("Add", new Type[1] { type2 });
			for (int k = 0; k < num; k++)
			{
				method.Invoke(obj, new object[1] { ReadSettingsValue(reader, type2) });
			}
			return obj;
		}
		object obj2 = Activator.CreateInstance(type);
		FieldInfo[] array2 = FieldsOf(type);
		foreach (FieldInfo fieldInfo in array2)
		{
			fieldInfo.SetValue(obj2, ReadSettingsValue(reader, fieldInfo.FieldType));
		}
		return obj2;
	}

	private static void InstallSimulation()
	{
		InitializeSimulationScopes();
		previewDontSync = AccessTools.StaticFieldRefAccess<bool>(AccessTools.DeclaredField("Multiplayer.Client.Multiplayer:dontSync") ?? throw new MissingFieldException("Multiplayer:dontSync"));
		Patch(AccessTools.DeclaredMethod(typeof(PawnGenerator), "GeneratePawn", new Type[1] { typeof(PawnGenerationRequest) }), "PreviewPawnRequestPrefix");
		Patch(AccessTools.DeclaredMethod(typeof(Ideo), "Notify_MemberGenerated"), "PreviewMemberNotificationPrefix");
		InstallWorldClocks();
		InstallGoodwillPulse();
		InstallRequirementQueries();
		Type type = TypeOf("WD_MapComponent_OutpostDefense");
		defenseRaidLaunched = AccessTools.FieldRefAccess<bool>(type, "raidLaunched");
		defenseRaidArrival = AccessTools.FieldRefAccess<int>(type, "raidArrivalTick");
		MethodInfo launch = AccessTools.DeclaredMethod(type, "LaunchPendingRaid");
		launchDefenseRaid = (object obj) =>
		{
			launch.Invoke(obj, null);
		};
		Patch("WD_MapComponent_OutpostDefense", "LaunchPendingRaidIfDue", "DefenseArrivalPrefix");
		string[] array = new string[3] { "WD_CaravanClashUtility", "WD_OutpostDefenseEncounterUtility", "WD_MapComponent_OutpostDefense" };
		for (int num = 0; num < array.Length; num++)
		{
			foreach (MethodInfo item in from m in AccessTools.GetDeclaredMethods(TypeOf(array[num]))
				where !m.ContainsGenericParameters && m.GetMethodBody() != null
				select m)
			{
				if (PatchProcessor.GetOriginalInstructions(item).Any((CodeInstruction i) => i.operand is MethodInfo methodInfo && methodInfo.DeclaringType == typeof(LongEventHandler) && methodInfo.Name == "QueueLongEvent"))
				{
					Patch(item, null, null, "LongEventsTranspiler");
				}
			}
		}
		MapEffects("WD_CaravanClashUtility", "ExecuteInterceptionRaidIncident");
		MapEffects("WD_OutpostDefenseEncounterUtility", "SpawnDefenders", "ExecuteRaidIncident", "ComputeRaidPoints");
		MapEffects("WD_OutpostDefenseStructureSpawner", "SpawnDefenses");
		MapEffects("AssaultArtilleryProjectileLaunch", "TryLaunchAssaultShell");
		MapEffects("WdDropPodCrashUtility", "LandCrashed", "LandKilledAsCorpses");
		MapEffects("WdRaidParmsUtility", "TryManualAssaultSpawn");
		array = new string[2] { "PlayerPawnTransferUtility", "RemoteOutpostEstablishUtility" };
		for (int num = 0; num < array.Length; num++)
		{
			Patch(array[num], "PrepareMapPawnForTransfer", "PawnMapEffectPrefix", null, null, "ScopeFinalizer");
		}
		array = new string[2] { "BeginEncounter", "ScheduleRaidArrival" };
		foreach (string method in array)
		{
			Patch("WD_MapComponent_OutpostDefense", method, "ComponentMapEffectPrefix", null, null, "ScopeFinalizer");
		}
		(string, string)[] array2 = new (string, string)[3]
		{
			("Dialog_OutpostProduction", "OpenAnimalInfoCard"),
			("WorldObject_WD_Outpost", "CreateTempPawnForUI"),
			("Dialog_NameNewOutpost", "RerollName")
		};
		for (int num = 0; num < array2.Length; num++)
		{
			(string, string) tuple = array2[num];
			Patch(tuple.Item1, tuple.Item2, "PreviewPrefix", null, null, "PreviewFinalizer");
		}
		Patch(AccessTools.PropertyGetter(TypeOf("WorldObject_WD_Outpost"), "StoredMechanoidPawnCount"), "StoredCountPrefix");
		Patch(AccessTools.PropertyGetter(TypeOf("WorldObject_WD_Outpost"), "StoredTransportPawnCount"), "StoredCountPrefix");
		Patch("TransportPodAaLaunchWarnCompat", "TryLaunch_Prefix", "TransportPodWarningPrefix");
		Patch("WorldComponent_SpreadManager", "PrunePlayerWdRaidLaunchTicks", "SimulationMutationOnly");
		Patch("WorldComponent_SpreadManager", "FinalizeInit", null, "RestoreInitializedScheduler", "FinalizeBudgetTranspiler");
		Patch("WorldComponent_SpreadManager", "BootstrapLateGameMetrics", "BootstrapWorldPrefix", null, "BootstrapSnapshotTranspiler", "ScopeFinalizer");
	}

	private static void InstallWorldClocks()
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		string[] array = new string[16]
		{
			"WorldObject_WD_Outpost", "WorldObject_Traveler", "CompViralSpread", "WorldActions_Traveler", "WorldComponent_SpreadManager", "WorldComponent_PrisonerRecruitSchedule", "NpcT4GlobalFireStagger", "WD_AssaultArtillerySupport", "AntiAirFireUtils", "Dialog_WdWorldGenAllegiances",
			"WorldActions_DiplomacyBuffsNerfs", "WorldObject_AT_Turret", "WorldObject_WD_ArtilleryTurret", "WdPostLoadGuard", "Outpost_EstablishmentRequirements", "EscalationGoodwillDrain"
		};
		for (int i = 0; i < array.Length; i++)
		{
			foreach (MethodInfo item in from m in AccessTools.GetDeclaredMethods(TypeOf(array[i]))
				where !m.ContainsGenericParameters && m.GetMethodBody() != null
				select m)
			{
				if (PatchProcessor.GetOriginalInstructions(item).Any((CodeInstruction code) => code.Calls(getter)))
				{
					Patch(item, null, null, "WorldTicksTranspiler");
				}
			}
		}
	}

	private static void RestoreInitializedScheduler(bool fromLoad)
	{
		if (MP.IsInMultiplayer & fromLoad)
		{
			Current.Game?.GetComponent<WorldDominationSimulationComponent>()?.RestoreAfterNativeInitialization();
		}
	}

	private static void InitializeDailyBudget(object manager)
	{
		if (MP.IsInMultiplayer)
		{
			Game game = Current.Game;
			if (game != null && game.GetComponent<WorldDominationSimulationComponent>()?.Restored == true && (bool)Get(manager, "firstTickRun"))
			{
				return;
			}
		}
		Call(manager, "CalculateDailyBudget");
	}

	private static void BootstrapWorldPrefix(out IDisposable __state)
	{
		__state = (MP.IsInMultiplayer ? SimulationWorldScope() : null);
	}

	private static object BootstrapSnapshot(object manager)
	{
		if (MP.IsInMultiplayer)
		{
			Game game = Current.Game;
			if (game != null && game.GetComponent<WorldDominationSimulationComponent>()?.Restored == true)
			{
				object obj = Get(manager, "dailySnapshot");
				if (obj != null)
				{
					return obj;
				}
			}
		}
		return Call(TypeOf("DailyWorldSnapshot"), "Build");
	}

	private static IEnumerable<CodeInstruction> BootstrapSnapshotTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo build = AccessTools.DeclaredMethod(TypeOf("DailyWorldSnapshot"), "Build");
		MethodInfo replacement = AccessTools.DeclaredMethod(typeof(WorldDomination), "BootstrapSnapshot");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(build))
			{
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction.labels.AddRange(instruction.labels);
				instruction.labels.Clear();
				codeInstruction.blocks.AddRange(instruction.blocks);
				instruction.blocks.Clear();
				yield return codeInstruction;
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
				yield return instruction;
				yield return new CodeInstruction(OpCodes.Castclass, build.ReturnType);
				count++;
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException("WD bootstrap snapshot construction changed: " + count);
		}
	}

	private static IEnumerable<CodeInstruction> FinalizeBudgetTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.DeclaredMethod(TypeOf("WorldComponent_SpreadManager"), "CalculateDailyBudget");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(WorldDomination), "InitializeDailyBudget");
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException("WD finalize daily budget call count changed: " + count);
		}
	}

	private static bool TransportPodWarningPrefix(ref bool __result)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return true;
		}
		Set(TypeOf("TransportPodAaLaunchWarnCompat"), "allowNext", false);
		__result = true;
		return false;
	}

	private static int ReadWorldTicks(TickManager manager)
	{
		if (!MP.IsInMultiplayer)
		{
			return manager.TicksGame;
		}
		object obj = simulationGame();
		object obj2 = ((obj == null) ? null : simulationWorld(obj));
		if (obj2 != null)
		{
			return simulationOwnerTicks(obj2);
		}
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			return manager.TicksGame;
		}
		throw new InvalidOperationException("WorldDomination requires an initialized Multiplayer world owner");
	}

	private static IEnumerable<CodeInstruction> WorldTicksTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(TickManager), "TicksGame");
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(getter))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(WorldDomination), "ReadWorldTicks");
			}
			yield return instruction;
		}
	}

	private static bool StoredCountPrefix(object __instance, MethodBase __originalMethod, ref int __result)
	{
		if (!Intercept)
		{
			return true;
		}
		string text = ((__originalMethod.Name == "get_StoredMechanoidPawnCount") ? "storedMechanoids" : "storedAnimalsAndVehicles");
		IEnumerable<Pawn> obj = Get(__instance, text) as IEnumerable<Pawn>;
		__result = ((obj != null) ? obj.Count((Pawn p) => p != null && !p.Destroyed && !p.Dead) : 0);
		if (text == "storedAnimalsAndVehicles" && ModsConfig.OdysseyActive)
		{
			int num = __result;
			IEnumerable<Thing> obj2 = Get(__instance, "storedPassengerShuttles") as IEnumerable<Thing>;
			__result = num + ((obj2 != null) ? obj2.Count((Thing t) => t != null && !t.Destroyed) : 0);
		}
		return false;
	}

	private static bool DefenseArrivalPrefix(object __instance)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!defenseRaidLaunched(__instance) && defenseRaidArrival(__instance) >= 0 && Find.TickManager.TicksGame >= defenseRaidArrival(__instance))
		{
			launchDefenseRaid(__instance);
		}
		return false;
	}

	private static void MapEffects(string type, params string[] names)
	{
		foreach (string name in names)
		{
			MethodInfo[] array = (from m in AccessTools.GetDeclaredMethods(TypeOf(type))
				where m.Name == name && m.GetParameters().Any((ParameterInfo p) => p.ParameterType == typeof(Map))
				select m).ToArray();
			if (array.Length == 0)
			{
				throw new MissingMethodException("TSA_WorldDomination." + type, name);
			}
			MethodInfo[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				Patch(array2[num], "MapEffectPrefix", null, null, "ScopeFinalizer");
			}
		}
	}

	private static void MapEffectPrefix(object[] __args, out IDisposable __state)
	{
		object obj;
		if (MP.IsInMultiplayer)
		{
			Map map = __args.OfType<Map>().FirstOrDefault();
			if (map != null)
			{
				obj = SimulationMapScope(map);
				goto IL_0020;
			}
		}
		obj = null;
		goto IL_0020;
		IL_0020:
		__state = (IDisposable)obj;
	}

	private static void PawnMapEffectPrefix(Pawn pawn, out IDisposable __state)
	{
		object obj;
		if (MP.IsInMultiplayer)
		{
			Map map = pawn?.Map;
			if (map != null)
			{
				obj = SimulationMapScope(map);
				goto IL_0021;
			}
		}
		obj = null;
		goto IL_0021;
		IL_0021:
		__state = (IDisposable)obj;
	}

	private static void ComponentMapEffectPrefix(MapComponent __instance, out IDisposable __state)
	{
		__state = (MP.IsInMultiplayer ? SimulationMapScope(__instance.map) : null);
	}

	private static Exception ScopeFinalizer(Exception __exception, IDisposable __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void PreviewPrefix(out LocalPreviewScope __state)
	{
		__state = (Intercept ? new LocalPreviewScope() : null);
	}

	private static Exception PreviewFinalizer(Exception __exception, LocalPreviewScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	private static void PreviewPawnRequestPrefix(ref PawnGenerationRequest request)
	{
		if (localPreviewDepth != 0)
		{
			request.ForceGenerateNewPawn = true;
			request.CanGeneratePawnRelations = false;
		}
	}

	private static bool PreviewMemberNotificationPrefix()
	{
		return localPreviewDepth == 0;
	}

	private static IEnumerable<CodeInstruction> LongEventsTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.DeclaredMethod(typeof(LongEventHandler), "QueueLongEvent", new Type[7]
		{
			typeof(Action),
			typeof(string),
			typeof(bool),
			typeof(Action<Exception>),
			typeof(bool),
			typeof(bool),
			typeof(Action)
		});
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				instruction.opcode = OpCodes.Call;
				instruction.operand = AccessTools.DeclaredMethod(typeof(WorldDomination), "QueueWorldEvent");
			}
			yield return instruction;
		}
	}

	private static void QueueWorldEvent(Action action, string textKey, bool asynchronous, Action<Exception> exceptionHandler, bool extraUI, bool hideUI, Action callback)
	{
		if (MP.IsInMultiplayer)
		{
			Action original = action;
			action = () =>
			{
				using (SimulationWorldScope())
				{
					original();
				}
			};
		}
		LongEventHandler.QueueLongEvent(action, textKey, asynchronous, exceptionHandler, extraUI, hideUI, callback);
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
		DynamicMethod dynamicMethod = new DynamicMethod("WorldDomination_GetMultiplayerGame", typeof(object), Type.EmptyTypes, typeof(WorldDomination), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldsfld, field);
		iLGenerator.Emit(OpCodes.Ret);
		simulationGame = (Func<object>)dynamicMethod.CreateDelegate(typeof(Func<object>));
		MethodInfo methodInfo = AccessTools.DeclaredMethod("Multiplayer.Client.SimulationTimeContext:ReadTicks") ?? throw new MissingMethodException("SimulationTimeContext:ReadTicks");
		DynamicMethod dynamicMethod2 = new DynamicMethod("WorldDomination_ReadOwnerTicks", typeof(int), new Type[1] { typeof(object) }, typeof(WorldDomination), skipVisibility: true);
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
		return ((obj == null) ? null : simulationWorld(obj)) ?? throw new InvalidOperationException("WorldDomination requires an initialized Multiplayer world owner");
	}

	private static IDisposable SimulationMapScope(Map map)
	{
		if (map == null)
		{
			throw new ArgumentNullException("map");
		}
		if (MP.IsInMultiplayer)
		{
			return new WorldDominationSimulationScope(simulationMapGetter.Invoke(null, new object[1] { map }), simulationMapPre, simulationMapPost, map);
		}
		return EmptyWorldDominationScope.Instance;
	}

	private static IDisposable SimulationWorldScope()
	{
		if (MP.IsInMultiplayer)
		{
			return new WorldDominationSimulationScope(WorldSimulationOwner(), simulationWorldPre, simulationWorldPost, null);
		}
		return EmptyWorldDominationScope.Instance;
	}

	private static void InstallDecisionWorkers()
	{
		MP.RegisterSyncWorker(delegate(SyncWorker sync, ref ThingDefCountClass value)
		{
			object obj;
			if (!sync.isWriting || value == null)
			{
				obj = null;
			}
			else
			{
				int num2 = 1;
				List<ThingDefCountClass> list = new List<ThingDefCountClass>(num2);
				CollectionsMarshal.SetCount(list, num2);
				CollectionsMarshal.AsSpan(list)[0] = value;
				obj = EncodeStock(list);
			}
			byte[] obj2 = (byte[])obj;
			sync.Bind(ref obj2);
			if (!sync.isWriting)
			{
				value = ((obj2 == null) ? null : DecodeStock(obj2).Single());
			}
		});
		RegisterValueWorker("PlayerPawnRosterEntry", "pawn", "shuttle", "sourceOutpost", "mapParent", "sourceCaravan", "locationKind", "outpostRole", "isMovable", "thingId");
		RegisterValueWorker("PlayerPawnTransferDestination", "kind", "colony", "outpost");
		RegisterValueWorker("PlayerPawnTransferUtility+PlayerPawnTransferAssignment", "entry", "destination");
		RegisterValueWorker("PlayerPawnDropPodUtility+OriginAllocation", "origin", "mode");
		RegisterValueWorker("PlayerPawnDropPodUtility+AllocationResult", "need", "have", "podCount", "landCount", "abortCount", "rows");
		RegisterValueWorker("Window_AllPlayerGear+LaunchItem", "stockRow", "thing", "sendCount", "holderPawn");
		RegisterValueWorker("AssaultArtilleryOutpostEntry", "Outpost", "IsGhostRelay", "DistanceTiles", "EtaTicks", "ScatterRadius", "OnCooldown", "NoShootingSkill", "CooldownDaysLeft", "Tier");
		RegisterValueWorker("WITab_Outpost_Pawns+<>c__DisplayClass160_0", "selectedPawns", "outpost", "target");
		RegisterValueWorker("WITab_Outpost_Pawns+<>c__DisplayClass160_1", "pod", "land", "abort", "CS$<>8__locals1");
		RegisterValueWorker("WITab_Outpost_Pawns+<>c__DisplayClass161_0", "selectedPawns", "outpost", "tileId");
		RegisterValueWorker("WITab_Outpost_Pawns+<>c__DisplayClass161_1", "pod", "land", "abort", "CS$<>8__locals1");
		RegisterValueWorker("WorldObject_Traveler+<>c__DisplayClass189_0", "caravan", "targetTile", "<>4__this");
		RegisterValueWorker("WD_WorldSetupTools+<>c");
		RegisterValueWorker("WD_WorldSetupTools+<>c__DisplayClass23_0", "builder", "kind");
		RegisterValueWorker("WD_WorldSetupTools+<>c__DisplayClass26_0", "faction", "kind");
		RegisterValueWorker("WD_WorldSetupTools+<>c__DisplayClass29_0", "faction", "tier");
		RegisterValueWorker("WD_WorldSetupTools+<>c__DisplayClass31_0", "def");
		Type[] array = new Type[4]
		{
			typeof(Pawn),
			typeof(Building_PassengerShuttle),
			TypeOf("PlayerPawnRosterEntry"),
			TypeOf("PlayerPawnTransferUtility+PlayerPawnTransferAssignment")
		};
		for (int num = 0; num < array.Length; num++)
		{
			RegisterReadOnlyListWorker(array[num]);
		}
	}

	private static void RegisterValueWorker(string name, params string[] names)
	{
		Type type = TypeOf(name);
		FieldInfo[] fields = names.Select((string n) => AccessTools.DeclaredField(type, n) ?? throw new MissingFieldException(type.FullName, n)).ToArray();
		MP.RegisterSyncWorker(delegate(SyncWorker sync, ref object value)
		{
			bool obj = sync.isWriting && value != null;
			sync.Bind(ref obj);
			if (!obj)
			{
				value = null;
			}
			else
			{
				if (!sync.isWriting)
				{
					value = Activator.CreateInstance(type, nonPublic: true);
				}
				FieldInfo[] array = fields;
				foreach (FieldInfo fieldInfo in array)
				{
					object obj2 = (sync.isWriting ? fieldInfo.GetValue(value) : null);
					sync.Bind(ref obj2, fieldInfo.FieldType);
					if (!sync.isWriting)
					{
						fieldInfo.SetValue(value, obj2);
					}
				}
			}
		}, type);
	}

	private static void RegisterReadOnlyListWorker(Type element)
	{
		Type listType = typeof(List<>).MakeGenericType(element);
		Type targetType = typeof(IReadOnlyList<>).MakeGenericType(element);
		MP.RegisterSyncWorker(delegate(SyncWorker sync, ref object value)
		{
			object obj = value;
			if (sync.isWriting && value != null && !listType.IsInstanceOfType(value))
			{
				IList list = (IList)Activator.CreateInstance(listType);
				foreach (object item in (IEnumerable)value)
				{
					list.Add(item);
				}
				obj = list;
			}
			sync.Bind(ref obj, listType);
			if (!sync.isWriting)
			{
				value = obj;
			}
		}, targetType);
	}
}
