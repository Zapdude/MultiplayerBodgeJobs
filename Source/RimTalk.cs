using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("cj.rimtalk")]
public class RimTalk
{
	private sealed class MetadataQueue
	{
		internal readonly Dictionary<int, System.WeakReference<Pawn>> Dirty = new Dictionary<int, System.WeakReference<Pawn>>();

		internal readonly Dictionary<int, long> Issued = new Dictionary<int, long>();

		internal readonly HashSet<int> Warned = new HashSet<int>();

		internal double Next;
	}

	private sealed class RequestOwner
	{
		internal Game game;

		internal int epoch;

		internal volatile bool cancelled;
	}

	private sealed class PumpClock
	{
		internal int observed = -1;

		internal int lastRegular;

		internal double next;

		internal double nextCache;
	}

	private sealed class StreamingBatch
	{
		private readonly object request;

		private readonly RequestOwner owner;

		private readonly List<object> received = new List<object>();

		private Guid parent;

		private bool directivesProcessed;

		private CancellationToken cancellation;

		private int responseCount;

		private bool CurrentOwner
		{
			get
			{
				if (!owner.cancelled && owner.game == pipelineGame)
				{
					return owner.epoch == Volatile.Read(in pipelineEpoch);
				}
				return false;
			}
		}

		internal StreamingBatch(object request, RequestOwner owner)
		{
			this.request = request;
			this.owner = owner;
		}

		internal async Task Run()
		{
			incoming.Enqueue(() =>
			{
				if (CurrentOwner && Get(request, "Initiator") is Pawn pawn)
				{
					generatingOwners.Remove(pawn);
					generatingOwners.Add(pawn, owner);
					Set(Call(TypeOf("Data.Cache"), "Get", pawn), "IsGeneratingTalk", true);
				}
			});
			ParameterExpression parameterExpression = Expression.Parameter(TypeOf("Data.TalkResponse"));
			Delegate obj = Expression.Lambda(typeof(Action<>).MakeGenericType(parameterExpression.Type), Expression.Call(Expression.Constant(this), AccessTools.Method(typeof(StreamingBatch), "Receive"), Expression.Convert(parameterExpression, typeof(object))), parameterExpression).Compile();
			try
			{
				Task task = (Task)Call(TypeOf("Service.AIService"), "ChatStreaming", request, obj);
				cancellation = (CancellationToken)Get(TypeOf("Service.AIService"), "CurrentCancellationToken");
				await task.ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception)
			{
			}
			finally
			{
				incoming.Enqueue(Finish);
			}
		}

		private void Receive(object response)
		{
			if (CurrentOwner && !cancellation.IsCancellationRequested && Interlocked.Increment(ref responseCount) <= 128)
			{
				object copy = Activator.CreateInstance(TypeOf("Data.TalkResponse"), Get(response, "TalkType"), (string)Get(response, "Name"), (string)Get(response, "Text"));
				string[] array = new string[4] { "Id", "TargetName", "InteractionRaw", "Directive" };
				foreach (string name in array)
				{
					Set(copy, name, Get(response, name));
				}
				object target = copy;
				IEnumerable<string> obj = Get(response, "Directives") as IEnumerable<string>;
				Set(target, "Directives", (obj != null) ? obj.ToList() : null);
				incoming.Enqueue(() =>
				{
					Process(copy);
				});
			}
		}

		private void Process(object response)
		{
			if (!CurrentOwner || cancellation.IsCancellationRequested || !MP.IsInMultiplayer || !PhysicalHostEffectAllowed || Current.Game != owner.game)
			{
				return;
			}
			using (new LocalAiScope())
			{
				Pawn pawn = ResolveParticipant(request, Get(response, "Name") as string);
				if (pawn == null || pawn.thingIDNumber <= 0 || pawn.Destroyed || !pawn.Spawned || !Find.Maps.Contains(pawn.Map))
				{
					return;
				}
				using (new ReadOnlyClock(pawn.Map))
				{
					object obj = Call(TypeOf("Data.Cache"), "Get", pawn);
					if (obj == null)
					{
						return;
					}
					Set(response, "Name", pawn.LabelShort);
					string name = Get(response, "TargetName") as string;
					Pawn pawn2 = ResolveParticipant(request, name);
					if (pawn2?.Map != pawn.Map || pawn2.thingIDNumber <= 0)
					{
						pawn2 = null;
					}
					Set(response, "TargetPawn", pawn2);
					Set(response, "TargetName", pawn2?.LabelShort);
					Set(response, "ParentTalkId", parent);
					parent = (Guid)Get(response, "Id");
					object obj2 = Get(request, "TalkType");
					bool flag = (bool)Get(request, "IsAnnouncement");
					Set(response, "TalkType", flag ? Enum.Parse(TypeOf("Source.Data.TalkType"), "User") : obj2);
					IList list = Get(response, "Directives") as IList;
					bool flag2 = !directivesProcessed && list != null;
					if (flag2)
					{
						string text = obj2.ToString();
						bool flag3 = ((text == "User" || text == "Announcement") ? true : false);
						flag2 = flag3;
					}
					if (flag2 && (bool)Get(Get(Call(TypeOf("Settings"), "Get"), "Context"), "EnableMemory"))
					{
						directivesProcessed = true;
						IEnumerable<Pawn> enumerable = (Get(request, "Participants") as IEnumerable<Pawn>) ?? new _003C_003Ez__ReadOnlySingleElementList<Pawn>(pawn);
						Pawn obj3 = Get(request, "Initiator") as Pawn;
						Pawn pawn3 = ((obj3 != null && obj3.thingIDNumber > 0) ? (Get(request, "Initiator") as Pawn) : (Get(request, "Recipient") as Pawn));
						bool flag4 = false;
						IEnumerable<Pawn> enumerable3;
						if (!flag)
						{
							IEnumerable<Pawn> enumerable2 = new Pawn[1] { pawn3 };
							enumerable3 = enumerable2;
						}
						else
						{
							enumerable3 = enumerable;
						}
						foreach (Pawn item in enumerable3)
						{
							if (item != null && item.thingIDNumber > 0)
							{
								flag4 |= (bool)Call(StatePersona(item), "UpdateDirectives", list);
							}
						}
						Set(response, "HasDirectiveModified", flag4);
					}
					received.Add(response);
					responseOwners.Add(response, owner);
					Call(obj, "QueueIncomingResponse", response);
				}
			}
		}

		private void Finish()
		{
			if (owner.game != pipelineGame || owner.epoch != Volatile.Read(in pipelineEpoch) || Current.Game != owner.game || !MP.IsInMultiplayer || !PhysicalHostEffectAllowed)
			{
				return;
			}
			using (new LocalAiScope())
			{
				Pawn pawn = Get(request, "Initiator") as Pawn;
				using (new ReadOnlyClock(pawn?.Map ?? (Get(request, "Recipient") as Pawn)?.Map))
				{
					if (pawn != null && generatingOwners.TryGetValue(pawn, out var value) && value == owner)
					{
						object obj = Call(TypeOf("Data.Cache"), "Get", pawn);
						if (obj != null)
						{
							Set(obj, "IsGeneratingTalk", false);
						}
						generatingOwners.Remove(pawn);
					}
					if (!CurrentOwner || received.Count == 0 || cancellation.IsCancellationRequested)
					{
						return;
					}
					IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(TypeOf("Data.TalkResponse")));
					foreach (object item in received)
					{
						list.Add(item);
					}
					Call(TypeOf("Service.TalkService"), "AddResponsesToHistory", list, Get(request, "Prompt"), request);
				}
			}
		}
	}

	private sealed class ReadOnlyClock : IDisposable
	{
		private readonly TickManager manager;

		private readonly Game game;

		private readonly object oldMap;

		private readonly object oldTicks;

		private readonly object oldSpeed;

		private readonly object oldSlower;

		private readonly object oldAbs;

		internal ReadOnlyClock(Map map)
		{
			if (map != null && Find.Maps.Contains(map))
			{
				object obj = pipelineMapOwner.Invoke(null, new object[1] { map });
				if (obj != null)
				{
					object[] array = new object[5]
					{
						obj,
						0,
						TimeSpeed.Paused,
						null,
						0
					};
					pipelineReadClock.Invoke(null, array);
					manager = Find.TickManager;
					game = Current.Game;
					oldMap = pipelineMapIndex.GetValue(game);
					oldTicks = pipelineTicks.GetValue(manager);
					oldSpeed = pipelineSpeed.GetValue(manager);
					oldSlower = pipelineSlower.GetValue(manager);
					oldAbs = pipelineAbsBase.GetValue(manager);
					pipelineMapIndex.SetValue(game, (sbyte)map.Index);
					pipelineTicks.SetValue(manager, array[1]);
					pipelineSpeed.SetValue(manager, array[2]);
					pipelineSlower.SetValue(manager, array[3]);
					pipelineAbsBase.SetValue(manager, array[4]);
				}
			}
		}

		public void Dispose()
		{
			if (manager != null)
			{
				pipelineTicks.SetValue(manager, oldTicks);
				pipelineSpeed.SetValue(manager, oldSpeed);
				pipelineSlower.SetValue(manager, oldSlower);
				pipelineAbsBase.SetValue(manager, oldAbs);
				pipelineMapIndex.SetValue(game, oldMap);
			}
		}
	}

	private sealed class LocalAiScope : IDisposable
	{
		private readonly UnityEngine.Random.State unity = UnityEngine.Random.state;

		private bool disposed;

		internal LocalAiScope()
		{
			Rand.PushState();
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
				UnityEngine.Random.state = unity;
			}
			finally
			{
				Rand.PopState();
			}
		}
	}

	private const string Ns = "RimTalk.";

	private static readonly ConcurrentDictionary<string, Type> Types = new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

	private static readonly ConcurrentDictionary<(Type, string), MemberInfo> Members = new ConcurrentDictionary<(Type, string), MemberInfo>();

	internal const string ExternalInputPolicy = "rimtalk.physical-host-api.explicit-reply-and-persona-ledger.v1";

	private static Func<bool> checkpointCanExternal;

	private static Func<bool> checkpointIsWorker;

	private static bool checkpointWorkerRoleContract;

	private static bool pipelineIsolationInstalled;

	private static bool metadataIsolationInstalled;

	private static readonly List<MethodBase> externalPolicyTargets = new List<MethodBase>();

	private static readonly List<(MethodBase Target, string Prefix, string Postfix, string Transpiler, string Finalizer)> externalInstalledHooks = new List<(MethodBase, string, string, string, string)>();

	private static readonly ConditionalWeakTable<Game, MetadataQueue> MetadataGames = new ConditionalWeakTable<Game, MetadataQueue>();

	private static Func<object> metadataServer;

	private static FieldInfo metadataReloading;

	private static FieldInfo metadataRunning;

	private static FieldInfo metadataWorld;

	private static FieldInfo metadataSaved;

	private static readonly ConditionalWeakTable<object, RequestOwner> requestOwners = new ConditionalWeakTable<object, RequestOwner>();

	private static readonly ConditionalWeakTable<Pawn, RequestOwner> generatingOwners = new ConditionalWeakTable<Pawn, RequestOwner>();

	private static readonly ConditionalWeakTable<object, RequestOwner> responseOwners = new ConditionalWeakTable<object, RequestOwner>();

	private static readonly ConcurrentQueue<Action> incoming = new ConcurrentQueue<Action>();

	private static readonly Dictionary<int, PumpClock> pumpClocks = new Dictionary<int, PumpClock>();

	private static Game pipelineGame;

	private static int pipelineEpoch;

	private static Func<object> pipelineClient;

	private static Action<Action> pipelineEnqueue;

	private static MethodInfo pipelineMapOwner;

	private static MethodInfo pipelineReadTicks;

	private static MethodInfo pipelineReadClock;

	private static FieldInfo pipelineTicks;

	private static FieldInfo pipelineMapIndex;

	private static FieldInfo pipelineSpeed;

	private static FieldInfo pipelineSlower;

	private static FieldInfo pipelineAbsBase;

	private static AccessTools.FieldRef<bool> localPlayerDontSync;

	[ThreadStatic]
	private static bool dispatching;

	[ThreadStatic]
	private static Map pumpMap;

	private const int ReplayMaxTextCharacters = 8192;

	private const int ReplayMaxTextBytes = 16384;

	private static Type ReplayEntryType;

	private static Type ReplayTalkType;

	private static Type ReplayInteractionType;

	private static ConstructorInfo ReplayEntryConstructor;

	private static FieldInfo ReplayCachedString;

	private static PropertyInfo ReplayConversation;

	private static PropertyInfo ReplayInteraction;

	private static PropertyInfo ReplayTalk;

	private static PropertyInfo ReplayDirective;

	private static MethodInfo ReplayThoughtDef;

	private static object ReplayNoInteraction;

	private static object ReplayChatInteraction;

	[ThreadStatic]
	private static Pawn replaySpeaker;

	[ThreadStatic]
	private static string replayText;

	[ThreadStatic]
	private static bool replayTextActive;

	private static ConstructorInfo ReplayRequestConstructor;

	private static ConstructorInfo ReplayApiLogConstructor;

	private static FieldInfo ReplayApiLogId;

	private static FieldInfo ReplayApiLogInteraction;

	private static PropertyInfo ReplayApiLogConversation;

	private static PropertyInfo ReplayApiLogTarget;

	private static PropertyInfo ReplayApiLogSpokenTick;

	private static PropertyInfo ReplayApiLogSpokenTime;

	private static MethodInfo ReplayApiGet;

	private static MethodInfo ReplayApiRegister;

	private static MethodInfo ReplayOverlayNotify;

	private static object ReplayStreamChannel;

	private static bool replayPresentationWarning;

	private static Type StatePersonaType;

	private static Type StateMemoryType;

	private static FieldInfo StatePersonality;

	private static FieldInfo StateWeight;

	private static FieldInfo StateSpoken;

	private static FieldInfo StateMemories;

	private static FieldInfo StateImpressionTick;

	private static FieldInfo StateImpressionTarget;

	private static FieldInfo StateImpression;

	private static FieldInfo[] StateMemoryFields;

	private static Func<Pawn, bool> StateIsBaby;

	private static Func<Pawn, bool> StateIsVisitor;

	private static Func<Pawn, bool> StateIsEnemy;

	private static Func<Pawn, bool> StateIsPlayer;

	private static object[] StateAdultProfiles;

	private static readonly ConditionalWeakTable<Game, RimTalkPersonaStore> StateGames = new ConditionalWeakTable<Game, RimTalkPersonaStore>();

	internal static bool Enabled { get; private set; }

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

	private static bool PhysicalHostEffectAllowed => RimTalkExternalEffectPolicy.Allows(checkpointCanExternal?.Invoke(), checkpointIsWorker?.Invoke(), MP.IsInMultiplayer, MP.IsHosting);

	private static bool LocalPresentationAllowed => !(checkpointIsWorker?.Invoke() ?? false);

	internal static bool MetadataPhysicalHost => PhysicalHostEffectAllowed;

	internal static bool MetadataInitialHostBaseline
	{
		get
		{
			if (!MP.IsInMultiplayer || !PhysicalHostEffectAllowed || metadataServer == null || !(bool)metadataReloading.GetValue(null))
			{
				return false;
			}
			object obj = metadataServer();
			if (obj != null && !(bool)metadataRunning.GetValue(obj))
			{
				return metadataSaved.GetValue(metadataWorld.GetValue(obj)) == null;
			}
			return false;
		}
	}

	internal static bool ReplayAvailable => ReplayEntryType != null;

	internal static bool StateAvailable => StatePersonaType != null;

	private static Type TypeOf(string name)
	{
		return Types.GetOrAdd(name, (string n) => AccessTools.TypeByName("RimTalk." + n) ?? throw new TypeLoadException("RimTalk." + n));
	}

	public RimTalk(ModContentPack mod)
	{
		CommandAuthorityBridge.Install();
		InstallExternalServiceIsolation();
		InstallState();
		InstallReplay();
		InstallPipeline();
		InstallMetadata();
		Enabled = true;
		RegisterExternalInputPolicy();
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
		MemberInfo memberInfo = Member((target as Type) ?? target.GetType(), name);
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				return propertyInfo.GetValue((target is Type) ? null : target);
			}
			return null;
		}
		return fieldInfo.GetValue((target is Type) ? null : target);
	}

	private static void Set(object target, string name, object value)
	{
		MemberInfo memberInfo = Member((target as Type) ?? target.GetType(), name);
		if (!(memberInfo is FieldInfo fieldInfo))
		{
			if (memberInfo is PropertyInfo propertyInfo)
			{
				propertyInfo.SetValue((target is Type) ? null : target, value);
			}
		}
		else
		{
			fieldInfo.SetValue((target is Type) ? null : target, value);
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
			throw new AmbiguousMatchException($"{type.FullName}.{name}: {array.Length} matches");
		}
		return array[0].Invoke((target is Type) ? null : target, args);
	}

	private static void Patch(string type, string method, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		Patch(AccessTools.DeclaredMethod(TypeOf(type), method) ?? throw new MissingMethodException("RimTalk." + type, method), prefix, postfix, transpiler, finalizer);
	}

	private static void Patch(MethodBase target, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
	{
		MpCompat.harmony.Patch(target, Hook(prefix), Hook(postfix), Hook(transpiler), Hook(finalizer));
		externalInstalledHooks.Add((target, prefix, postfix, transpiler, finalizer));
		static HarmonyMethod Hook(string name)
		{
			if (name != null)
			{
				return new HarmonyMethod(typeof(RimTalk), name);
			}
			return null;
		}
	}

	private static void InstallExternalServiceIsolation()
	{
		Type core = AccessTools.TypeByName("Multiplayer.Client.Compatibility.CheckpointState");
		checkpointCanExternal = Read("CanPerformExternalSideEffects");
		checkpointIsWorker = Read("IsWorker");
		checkpointWorkerRoleContract = core != null && AccessTools.DeclaredField(core, "WorkerRoleVersion")?.GetRawConstantValue() is int num && num == 1;
		Func<bool> Read(string name)
		{
			MethodInfo methodInfo = ((core == null) ? null : AccessTools.PropertyGetter(core, name));
			if (methodInfo == null)
			{
				return null;
			}
			if (!methodInfo.IsStatic || methodInfo.ReturnType != typeof(bool) || methodInfo.GetParameters().Length != 0)
			{
				throw new NotSupportedException("Unreviewed Core checkpoint effect API");
			}
			return methodInfo.CreateDelegate<Func<bool>>();
		}
	}

	private static void RegisterExternalInputPolicy()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.Checkpoints.CheckpointInputs");
		((type == null) ? null : AccessTools.DeclaredMethod(type, "RegisterExternalServiceInputContract", new Type[3]
		{
			typeof(string),
			typeof(string),
			typeof(Func<bool>)
		}))?.Invoke(null, new object[3]
		{
			"cj.rimtalk",
			"rimtalk.physical-host-api.explicit-reply-and-persona-ledger.v1",
			new Func<bool>(VerifyExternalInputPolicy)
		});
	}

	internal static bool VerifyExternalInputPolicy()
	{
		if (Enabled && checkpointWorkerRoleContract && checkpointCanExternal != null && checkpointIsWorker != null && pipelineIsolationInstalled && metadataIsolationInstalled)
		{
			FieldInfo[] stateMemoryFields = StateMemoryFields;
			if (stateMemoryFields != null && stateMemoryFields.Length == 13)
			{
				if (externalPolicyTargets.Count != 0 && externalInstalledHooks.Count != 0)
				{
					return externalInstalledHooks.All(((MethodBase Target, string Prefix, string Postfix, string Transpiler, string Finalizer) h) =>
					{
						Patches patchInfo = Harmony.GetPatchInfo(h.Target);
						return patchInfo != null && Has(patchInfo.Prefixes, h.Prefix) && Has(patchInfo.Postfixes, h.Postfix) && Has(patchInfo.Transpilers, h.Transpiler) && Has(patchInfo.Finalizers, h.Finalizer);
					});
				}
				return false;
			}
		}
		return false;
		static bool Has(IEnumerable<Patch> patches, string name)
		{
			if (name != null)
			{
				return patches.Any((Patch p) => p.PatchMethod.DeclaringType == typeof(RimTalk) && p.PatchMethod.Name == name);
			}
			return true;
		}
	}

	private static void InstallMetadata()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
		Type type2 = AccessTools.TypeByName("Multiplayer.Common.MultiplayerServer");
		Type type3 = AccessTools.TypeByName("Multiplayer.Common.WorldData");
		metadataServer = (AccessTools.PropertyGetter(type, "LocalServer") ?? throw new MissingMemberException("Multiplayer:LocalServer")).CreateDelegate<Func<object>>();
		metadataReloading = AccessTools.DeclaredField(type, "reloading") ?? throw new MissingFieldException("Multiplayer:reloading");
		metadataRunning = AccessTools.DeclaredField(type2, "running") ?? throw new MissingFieldException("MultiplayerServer:running");
		metadataWorld = AccessTools.DeclaredField(type2, "worldData") ?? throw new MissingFieldException("MultiplayerServer:worldData");
		metadataSaved = AccessTools.DeclaredField(type3, "savedGame") ?? throw new MissingFieldException("WorldData:savedGame");
		Dictionary<string, int> mutations = new Dictionary<string, int>(StringComparer.Ordinal)
		{
			["TryMarkAsSpoken"] = 1,
			["RecordMemory"] = 3,
			["RecordMilestone"] = 2,
			["UpdateDirectives"] = 1,
			["RecordDirectives"] = 1,
			["RecordDirective"] = 1,
			["RecordCoreTrauma"] = 2,
			["GetImpressionOf"] = 1,
			["GetRelationalMemoryOf"] = 1,
			["CleanFadedMemories"] = 1
		};
		MethodInfo[] methods = (from m in AccessTools.GetDeclaredMethods(StatePersonaType)
			where mutations.ContainsKey(m.Name)
			select m).ToArray();
		if (mutations.Any((KeyValuePair<string, int> pair) => methods.Count((MethodInfo m) => m.Name == pair.Key) != pair.Value) || methods.Any((MethodInfo m) => m.IsStatic))
		{
			throw new NotSupportedException("Unreviewed RimTalk persona mutation methods");
		}
		MethodInfo[] array = methods;
		for (int num = 0; num < array.Length; num++)
		{
			Patch(array[num], null, "MetadataMutationPostfix");
		}
		MP.RegisterSyncMethod(typeof(RimTalk), "SyncPersonaMetadata").SetHostOnly().SetVersion(33);
		metadataIsolationInstalled = true;
	}

	private static void MetadataMutationPostfix(Hediff __instance)
	{
		MarkMetadataDirty(__instance?.pawn);
	}

	internal static void MarkMetadataDirty(Pawn pawn)
	{
		if (metadataIsolationInstalled && MP.IsInMultiplayer && PhysicalHostEffectAllowed && UnityData.IsInMainThread && Current.Game != null && pawn != null && pawn.thingIDNumber > 0)
		{
			MetadataQueue value = MetadataGames.GetValue(Current.Game, (Game _) => new MetadataQueue());
			if (!value.Dirty.ContainsKey(pawn.thingIDNumber) && value.Dirty.Count < 4096)
			{
				value.Dirty[pawn.thingIDNumber] = new System.WeakReference<Pawn>(pawn);
			}
		}
	}

	private static void FlushMetadata()
	{
		if (!PhysicalHostEffectAllowed || !MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand || Scribe.mode != LoadSaveMode.Inactive || Current.Game == null)
		{
			return;
		}
		MetadataQueue value = MetadataGames.GetValue(Current.Game, (Game _) => new MetadataQueue());
		double num = (double)Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
		if (num < value.Next || value.Dirty.Count == 0)
		{
			return;
		}
		value.Next = num + 0.25;
		RimTalkPersonaStore rimTalkPersonaStore = StateStoreFor(Current.Game);
		int num2 = 0;
		int num3 = 0;
		int[] array = value.Dirty.Keys.OrderBy((int id) => id).Take(32).ToArray();
		foreach (int num5 in array)
		{
			long num6 = rimTalkPersonaStore.Revision(num5);
			if (value.Issued.TryGetValue(num5, out var value2) && value2 > num6)
			{
				continue;
			}
			if (!value.Dirty[num5].TryGetTarget(out var target) || target.thingIDNumber != num5)
			{
				value.Dirty.Remove(num5);
				continue;
			}
			try
			{
				RimTalkMetadataCodec.Persona persona = MetadataTransfer(StateCapturePersona(num5, rimTalkPersonaStore.Get(target)));
				persona.Revision = 0L;
				string item = RimTalkMetadataCodec.Encode(persona).digest;
				RimTalkPersonaRecord rimTalkPersonaRecord = rimTalkPersonaStore.Committed(num5);
				if (rimTalkPersonaRecord == null)
				{
					goto IL_01b5;
				}
				RimTalkMetadataCodec.Persona persona2 = MetadataTransfer(rimTalkPersonaRecord);
				persona2.Revision = 0L;
				if (!(RimTalkMetadataCodec.Encode(persona2).digest == item))
				{
					goto IL_01b5;
				}
				value.Dirty.Remove(num5);
				continue;
				IL_01b5:
				persona.Revision = checked(num6 + 1);
				(string, string) tuple = RimTalkMetadataCodec.Encode(persona);
				SyncPersonaMetadata(num5, persona.Revision, tuple.Item1, tuple.Item2);
				value.Issued[num5] = persona.Revision;
				num2++;
				num3 += Encoding.UTF8.GetByteCount(tuple.Item1);
				if (num3 >= 65536)
				{
					value.Next = num + 2.0;
					break;
				}
				if (num2 >= 4)
				{
					break;
				}
			}
			catch (Exception ex) when ((ex is InvalidDataException || ex is OverflowException || ex is NotSupportedException) ? true : false)
			{
				if (value.Warned.Add(num5))
				{
					Log.Warning("RimTalk metadata cannot enter the bounded shared ledger (" + ex.GetType().Name + ").");
				}
				value.Next = num + 2.0;
			}
		}
	}

	private static void SyncPersonaMetadata(int pawnId, long revision, string payload, string digest)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || Current.Game == null || pawnId <= 0)
		{
			return;
		}
		try
		{
			RimTalkPersonaRecord record = MetadataRecord(RimTalkMetadataCodec.Decode(pawnId, revision, payload, digest));
			StateStoreFor(Current.Game).Accept(record);
		}
		catch (Exception ex) when ((ex is InvalidDataException || ex is JsonException || ex is OverflowException) ? true : false)
		{
		}
	}

	private static RimTalkMetadataCodec.Persona MetadataTransfer(RimTalkPersonaRecord record)
	{
		RimTalkMetadataCodec.Persona persona = new RimTalkMetadataCodec.Persona
		{
			PawnId = record.PawnId,
			Revision = record.Revision,
			Personality = record.Personality,
			Weight = record.Weight,
			Spoken = new SortedDictionary<string, int>(record.Spoken ?? new Dictionary<string, int>(), StringComparer.Ordinal)
		};
		foreach (IExposable item in record.Memories ?? new List<IExposable>())
		{
			if (item != null)
			{
				if (item.GetType() != StateMemoryType)
				{
					throw new NotSupportedException("Unreviewed RimTalk memory subclass");
				}
				persona.Memories.Add(new RimTalkMetadataCodec.Memory
				{
					TargetPawnId = (int)Get(item, "TargetPawnId"),
					TargetPawnName = (string)Get(item, "TargetPawnName"),
					EventKey = (string)Get(item, "EventKey"),
					BaseWeight = (float)Get(item, "BaseWeight"),
					CreatedTick = (int)Get(item, "CreatedTick"),
					LastRecalledTick = (int)Get(item, "LastRecalledTick"),
					Note = (string)Get(item, "Note"),
					IsDirective = (bool)Get(item, "IsDirective"),
					IsCoreTrauma = (bool)Get(item, "IsCoreTrauma"),
					IsMilestone = (bool)Get(item, "IsMilestone"),
					Perspective = Convert.ToInt32(Get(item, "Perspective")),
					Count = (int)Get(item, "Count"),
					LastTick = (int)Get(item, "LastTick")
				});
			}
		}
		return persona;
	}

	private static RimTalkPersonaRecord MetadataRecord(RimTalkMetadataCodec.Persona dto)
	{
		RimTalkPersonaRecord rimTalkPersonaRecord = new RimTalkPersonaRecord
		{
			PawnId = dto.PawnId,
			Revision = dto.Revision,
			Personality = dto.Personality,
			Weight = dto.Weight,
			Spoken = new Dictionary<string, int>(dto.Spoken, StringComparer.Ordinal)
		};
		Type fieldType = StateMemoryFields.Single((FieldInfo f) => f.Name == "Perspective").FieldType;
		foreach (RimTalkMetadataCodec.Memory memory in dto.Memories)
		{
			if (!Enum.IsDefined(fieldType, memory.Perspective))
			{
				throw new InvalidDataException("Unknown RimTalk memory perspective");
			}
			IExposable exposable = (IExposable)RuntimeHelpers.GetUninitializedObject(StateMemoryType);
			Set(exposable, "TargetPawnId", memory.TargetPawnId);
			Set(exposable, "TargetPawnName", memory.TargetPawnName);
			Set(exposable, "EventKey", memory.EventKey);
			Set(exposable, "BaseWeight", memory.BaseWeight);
			Set(exposable, "CreatedTick", memory.CreatedTick);
			Set(exposable, "LastRecalledTick", memory.LastRecalledTick);
			Set(exposable, "Note", memory.Note);
			Set(exposable, "IsDirective", memory.IsDirective);
			Set(exposable, "IsCoreTrauma", memory.IsCoreTrauma);
			Set(exposable, "IsMilestone", memory.IsMilestone);
			Set(exposable, "Perspective", Enum.ToObject(fieldType, memory.Perspective));
			Set(exposable, "Count", memory.Count);
			Set(exposable, "LastTick", memory.LastTick);
			rimTalkPersonaRecord.Memories.Add(exposable);
		}
		return rimTalkPersonaRecord;
	}

	private static void InstallPipeline()
	{
		pipelineClient = (AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "Client") ?? throw new MissingMemberException("Multiplayer:Client")).CreateDelegate<Func<object>>();
		pipelineEnqueue = (AccessTools.DeclaredMethod("Multiplayer.Client.OnMainThread:Enqueue", new Type[1] { typeof(Action) }) ?? throw new MissingMethodException("OnMainThread:Enqueue")).CreateDelegate<Action<Action>>();
		localPlayerDontSync = AccessTools.StaticFieldRefAccess<bool>(AccessTools.DeclaredField("Multiplayer.Client.Multiplayer:dontSync") ?? throw new MissingFieldException("Multiplayer:dontSync"));
		pipelineMapOwner = AccessTools.DeclaredMethod("Multiplayer.Client.Extensions:AsyncTime") ?? throw new MissingMethodException("Extensions:AsyncTime");
		pipelineReadTicks = AccessTools.DeclaredMethod("Multiplayer.Client.SimulationTimeContext:ReadTicks") ?? throw new MissingMethodException("SimulationTimeContext:ReadTicks");
		pipelineReadClock = AccessTools.DeclaredMethod("Multiplayer.Client.SimulationTimeContext:ReadClock") ?? throw new MissingMethodException("SimulationTimeContext:ReadClock");
		pipelineTicks = AccessTools.Field(typeof(TickManager), "ticksGameInt") ?? throw new MissingFieldException("TickManager:ticksGameInt");
		pipelineMapIndex = AccessTools.Field(typeof(Game), "currentMapIndex") ?? throw new MissingFieldException("Game:currentMapIndex");
		pipelineSpeed = AccessTools.Field(typeof(TickManager), "curTimeSpeed");
		pipelineSlower = AccessTools.Field(typeof(TickManager), "slower");
		pipelineAbsBase = AccessTools.Field(typeof(TickManager), "gameStartAbsTick");
		Patch(AccessTools.PropertyGetter(TypeOf("Data.Cache"), "Keys"), null, "PumpKeysPostfix");
		Patch("Data.UserRequestPool", "GetNextUserRequest", "PumpNextRequestPrefix");
		Patch("Data.PawnState", "CanGenerateTalk", "PumpCanGeneratePrefix");
		Patch("Patch.TickManagerPatch", "Postfix", "SinglePlayerOnly");
		Patch("Data.Cache", "InitializePlayerPawn", "LocalPlayerPrefix");
		Patch("Service.TalkService", "GenerateTalk", "GeneratePrefix", null, null, "AiScopeFinalizer");
		Patch("Service.TalkService", "GenerateTalkDebug", "GenerateDebugPrefix", null, null, "AiScopeFinalizer");
		Patch("Service.TalkService", "GenerateAndProcessTalkAsync", "StreamingPrefix");
		Patch("Service.TalkService", "CreateInteraction", "SubmitReplyPrefix");
		Patch("UI.PersonaEditorWindow", "DoWindowContents", "LocalContextPrefix", null, null, "AiScopeFinalizer");
		Patch("Data.PersonaService", "GeneratePersona", "PersonaQueryPrefix", null, null, "AiScopeFinalizer");
		Patch("Service.AIService", "ChatStreaming", "HostTaskPrefix");
		Patch("Service.AIService", "CancelCurrent", "CancelRequestPrefix");
		Patch(AccessTools.DeclaredMethod(TypeOf("Service.AIService"), "Query").MakeGenericMethod(TypeOf("Data.PersonalityData")), "HostQueryPrefix");
		Patch("Client.AIClientFactory", "GetAIClientAsync", "HostQueryPrefix");
		Patch("Client.AIClientFactory", "CreateServiceInstanceAsync", "HostQueryPrefix");
		foreach (Type item in from t in TypeOf("Client.AIClientFactory").Assembly.GetTypes()
			where !t.IsNested && !t.IsInterface && (t.Namespace?.StartsWith("RimTalk.Client.", StringComparison.Ordinal) ?? false)
			select t)
		{
			foreach (MethodInfo item2 in from m in AccessTools.GetDeclaredMethods(item)
				where !m.ContainsGenericParameters && m.GetMethodBody() != null && typeof(Task).IsAssignableFrom(m.ReturnType)
				select m)
			{
				Patch(item2, "HostIoPrefix");
				externalPolicyTargets.Add(item2);
			}
		}
		Patch("Service.CustomDialogueService", "DispatchDialogue", "DispatchPrefix");
		foreach (MethodInfo item3 in from m in AccessTools.GetDeclaredMethods(TypeOf("Service.CustomDialogueService"))
			where m.Name == "ExecuteDialogue"
			select m)
		{
			Patch(item3, "ExecuteDialoguePrefix");
		}
		foreach (MethodInfo item4 in from m in AccessTools.GetDeclaredMethods(TypeOf("PawnMemory.MemoryHookService"))
			where m.ReturnType == typeof(void) && !m.IsSpecialName && !m.ContainsGenericParameters
			select m)
		{
			Patch(item4, "HostContextPrefix", null, null, "AiScopeFinalizer");
		}
		Patch("Patch.ThoughtTracker", "TryMarkAsProcessed", "HostContextPrefix", null, null, "AiScopeFinalizer");
		Patch("Patch.InteractionLogPatch", "Postfix", "InteractionContextPrefix", null, null, "AiScopeFinalizer");
		MP.RegisterSyncMethod(typeof(RimTalk), "SyncDialogueRequest").SetVersion(2);
		pipelineIsolationInstalled = true;
	}

	private static bool SinglePlayerOnly()
	{
		if (!MP.IsInMultiplayer)
		{
			return LocalPresentationAllowed;
		}
		return false;
	}

	private static void PumpKeysPostfix(ref IEnumerable<Pawn> __result)
	{
		if (MP.IsInMultiplayer && pumpMap != null)
		{
			__result = __result.Where((Pawn p) => p?.Map == pumpMap).ToArray();
		}
	}

	private static bool PumpNextRequestPrefix(ref Pawn __result)
	{
		if (!MP.IsInMultiplayer || pumpMap == null)
		{
			return true;
		}
		IEnumerable<Pawn> obj = Get(TypeOf("Data.UserRequestPool"), "UserRequests") as IEnumerable<Pawn>;
		__result = ((obj != null) ? obj.FirstOrDefault((Pawn p) => p == null || p.Destroyed || p.Map == pumpMap) : null);
		return false;
	}

	private static bool PumpCanGeneratePrefix(object __instance, ref bool __result)
	{
		if (!LocalPresentationAllowed)
		{
			__result = false;
			return false;
		}
		if (!MP.IsInMultiplayer || pumpMap == null)
		{
			return true;
		}
		Pawn pawn = Get(__instance, "Pawn") as Pawn;
		__result = false;
		if (pawn?.Map != pumpMap)
		{
			return false;
		}
		Call(__instance, "DrainIncomingTalkResponses");
		int num;
		if (!(bool)Get(__instance, "IsGeneratingTalk") && (bool)Call(__instance, "CanDisplayTalk") && pawn.Awake())
		{
			IList obj = Get(__instance, "TalkResponses") as IList;
			if (obj != null && obj.Count == 0)
			{
				num = (((bool)Call(TypeOf("Util.CommonUtil"), "HasPassed", (int)Get(__instance, "LastTalkTick"), Convert.ToDouble(Get(Call(TypeOf("Settings"), "Get"), "ReplyInterval")))) ? 1 : 0);
				goto IL_011c;
			}
		}
		num = 0;
		goto IL_011c;
		IL_011c:
		__result = (byte)num != 0;
		return false;
	}

	private static bool HostTaskPrefix(ref Task __result)
	{
		if (PhysicalHostEffectAllowed)
		{
			return true;
		}
		__result = Task.CompletedTask;
		return false;
	}

	private static bool CancelRequestPrefix()
	{
		if (!LocalPresentationAllowed)
		{
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		object obj = Get(TypeOf("Service.AIService"), "CurrentRequest");
		if (obj != null && requestOwners.TryGetValue(obj, out var value))
		{
			value.cancelled = true;
		}
		return true;
	}

	private static bool HostQueryPrefix(MethodBase __originalMethod, ref object __result)
	{
		if (PhysicalHostEffectAllowed)
		{
			return true;
		}
		Type type = ((MethodInfo)__originalMethod).ReturnType.GetGenericArguments().Single();
		__result = typeof(Task).GetMethods().Single((MethodInfo m) => m.Name == "FromResult").MakeGenericMethod(type)
			.Invoke(null, new object[1]);
		return false;
	}

	private static bool HostIoPrefix(MethodBase __originalMethod, ref object __result)
	{
		if (PhysicalHostEffectAllowed)
		{
			return true;
		}
		Type returnType = ((MethodInfo)__originalMethod).ReturnType;
		if (returnType == typeof(Task))
		{
			__result = Task.CompletedTask;
		}
		else
		{
			Type type = returnType.GetGenericArguments().Single();
			__result = typeof(Task).GetMethods().Single((MethodInfo m) => m.Name == "FromResult").MakeGenericMethod(type)
				.Invoke(null, new object[1] { type.IsValueType ? Activator.CreateInstance(type) : null });
		}
		return false;
	}

	private static bool GeneratePrefix(object talkRequest, ref bool __result, out LocalAiScope __state)
	{
		__state = null;
		if (!PhysicalHostEffectAllowed)
		{
			__result = false;
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		BindRequest(talkRequest);
		__state = new LocalAiScope();
		return true;
	}

	private static bool GenerateDebugPrefix(object talkRequest, out LocalAiScope __state)
	{
		__state = null;
		if (!PhysicalHostEffectAllowed)
		{
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!UnityData.IsInMainThread)
		{
			Game game = Current.Game;
			object client = pipelineClient();
			int epoch = Volatile.Read(in pipelineEpoch);
			bool bound = Volatile.Read(in pipelineGame) == game;
			if (game != null && client != null)
			{
				pipelineEnqueue(() =>
				{
					if (MP.IsInMultiplayer && PhysicalHostEffectAllowed && Current.Game == game && pipelineClient() == client && (!bound || Volatile.Read(in pipelineEpoch) == epoch))
					{
						Call(TypeOf("Service.TalkService"), "GenerateTalkDebug", talkRequest);
					}
				});
			}
			return false;
		}
		BindRequest(talkRequest);
		__state = new LocalAiScope();
		return true;
	}

	private static void BindRequest(object request)
	{
		EnsurePipelineGame();
		requestOwners.Remove(request);
		requestOwners.Add(request, new RequestOwner
		{
			game = Current.Game,
			epoch = pipelineEpoch
		});
	}

	private static void EnsurePipelineGame()
	{
		if (pipelineGame != Current.Game)
		{
			pipelineGame = Current.Game;
			Interlocked.Increment(ref pipelineEpoch);
			pumpClocks.Clear();
			Action result;
			while (incoming.TryDequeue(out result))
			{
			}
			Call(TypeOf("Service.AIService"), "CancelCurrent");
		}
	}

	private static bool StreamingPrefix(object talkRequest, ref Task __result)
	{
		if (!PhysicalHostEffectAllowed)
		{
			__result = Task.CompletedTask;
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!requestOwners.TryGetValue(talkRequest, out var value))
		{
			__result = Task.CompletedTask;
			return false;
		}
		StreamingBatch streamingBatch = new StreamingBatch(talkRequest, value);
		__result = streamingBatch.Run();
		return false;
	}

	private static Pawn ResolveParticipant(object request, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		IEnumerable<Pawn> obj = Get(request, "Participants") as IEnumerable<Pawn>;
		List<Pawn> participants = ((obj != null) ? obj.Where((Pawn p) => p != null).Distinct().ToList() : null);
		if (participants == null)
		{
			return null;
		}
		name = name.Trim();
		Pawn[] array = participants.Where((Pawn p) => string.Equals(name, (string)Call(TypeOf("Service.PromptService"), "GetUniqueName", p, participants), StringComparison.OrdinalIgnoreCase)).ToArray();
		if (array.Length == 1)
		{
			return array[0];
		}
		Pawn[] array2 = participants.Where((Pawn p) => string.Equals(name, p.LabelShort, StringComparison.OrdinalIgnoreCase)).ToArray();
		if (array2.Length != 1)
		{
			return null;
		}
		return array2[0];
	}

	private static bool SubmitReplyPrefix(Pawn __0, object __1)
	{
		if (!PhysicalHostEffectAllowed)
		{
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!MP.InInterface)
		{
			return false;
		}
		if (__0 == null || __0.thingIDNumber <= 0 || !__0.Spawned || __0.Destroyed)
		{
			return false;
		}
		if (responseOwners.TryGetValue(__1, out var value) && (value.cancelled || value.epoch != Volatile.Read(in pipelineEpoch) || value.game != Current.Game))
		{
			object obj = Call(TypeOf("Data.Cache"), "Get", __0);
			if (obj != null)
			{
				Call(obj, "IgnoreTalkResponse");
			}
			return false;
		}
		Guid guid = (Guid)Get(__1, "Id");
		Pawn pawn = Get(__1, "TargetPawn") as Pawn;
		if (pawn?.Map != __0.Map || (pawn != null && pawn.thingIDNumber <= 0))
		{
			pawn = null;
		}
		object target = Call(TypeOf("Settings"), "Get");
		object obj2 = Call(TypeOf("Data.ApiHistory"), "GetApiLog", guid);
		ReplaySubmit(__0, pawn, guid.ToString("D"), (string)Get(__1, "Text"), Convert.ToInt32(Get(__1, "TalkType")), (string)Get(__1, "InteractionRaw"), (bool)Get(__1, "HasDirectiveModified"), (bool)Get(target, "ApplyMoodAndSocialEffects"), (obj2 == null) ? (-1) : ((int)Get(obj2, "ConversationId")));
		Call(TypeOf("Service.TalkService"), "GetTalk", __0);
		return false;
	}

	private static bool LocalPlayerPrefix()
	{
		if (!LocalPresentationAllowed)
		{
			return false;
		}
		if (!MP.IsInMultiplayer || Current.Game == null)
		{
			return true;
		}
		Type target = TypeOf("Data.Cache");
		Pawn pawn = Get(target, "_playerPawn") as Pawn;
		string text = (string)Get(Call(TypeOf("Settings"), "Get"), "PlayerName");
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "Player";
		}
		if (pawn == null)
		{
			pawn = new Pawn
			{
				def = ThingDefOf.Human,
				kindDef = PawnKindDefOf.Colonist,
				thingIDNumber = -178144
			};
			Set(target, "_playerPawn", pawn);
		}
		bool flag = localPlayerDontSync();
		try
		{
			localPlayerDontSync() = true;
			pawn.Name = new NameSingle(text);
		}
		finally
		{
			localPlayerDontSync() = flag;
		}
		IDictionary dictionary = Get(target, "PawnCache") as IDictionary;
		IDictionary dictionary2 = Get(target, "NameCache") as IDictionary;
		if (!dictionary.Contains(pawn))
		{
			dictionary[pawn] = Activator.CreateInstance(TypeOf("Data.PawnState"), pawn);
		}
		dictionary2[text] = pawn;
		return false;
	}

	private static bool DispatchPrefix(Pawn initiator, Pawn recipient, string message, bool isAnnouncement, string imageBase64)
	{
		if (!LocalPresentationAllowed && !MP.IsExecutingSyncCommand)
		{
			return false;
		}
		if (!Intercept || dispatching)
		{
			return true;
		}
		if ((message != null && message.Length > 8000) || !string.IsNullOrEmpty(imageBase64))
		{
			return false;
		}
		bool flag = initiator != null && initiator.thingIDNumber < 0;
		if ((!flag && initiator == null) || (recipient != null && recipient.thingIDNumber < 0))
		{
			return false;
		}
		SyncDialogueRequest(flag ? null : initiator, recipient, message, isAnnouncement, flag);
		return false;
	}

	private static bool ExecuteDialoguePrefix(object[] __args)
	{
		if (Intercept && !dispatching)
		{
			return DispatchPrefix((Pawn)__args[0], (Pawn)__args[1], (string)__args[2], (bool)__args[3], (__args.Length > 4) ? ((string)__args[4]) : null);
		}
		return PhysicalHostEffectAllowed;
	}

	private static void PumpPendingDialogues(Map map)
	{
		if (!(Get(TypeOf("Service.CustomDialogueService"), "PendingDialogues") is IDictionary dictionary))
		{
			return;
		}
		Pawn[] array = (from Pawn p in dictionary.Keys
			where p == null || p.Destroyed || p.Map == map
			select p).ToArray();
		foreach (Pawn pawn in array)
		{
			object target = dictionary[pawn];
			Pawn pawn2 = Get(target, "Recipient") as Pawn;
			if (pawn == null || pawn.Destroyed || pawn2 == null || pawn2.Destroyed)
			{
				dictionary.Remove(pawn);
			}
			else if (pawn2.Map == map && (bool)Call(TypeOf("Service.CustomDialogueService"), "CanTalk", pawn, pawn2))
			{
				dispatching = true;
				try
				{
					Call(TypeOf("Service.CustomDialogueService"), "ExecuteDialogue", pawn, pawn2, Get(target, "Message"), Get(target, "IsAnnouncement"), Get(target, "ImageBase64"));
				}
				finally
				{
					dispatching = false;
				}
				dictionary.Remove(pawn);
			}
		}
	}

	private static void SyncDialogueRequest(Pawn initiator, Pawn recipient, string message, bool announcement, bool player)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || message == null || message.Length > 8000 || recipient == null || !recipient.Spawned || recipient.Dead || recipient.Destroyed)
		{
			return;
		}
		if (player)
		{
			if (initiator != null || !CommandAuthorityBridge.CanActForFaction(Faction.OfPlayer))
			{
				return;
			}
		}
		else if (initiator == null || !initiator.Spawned || initiator.Dead || initiator.Destroyed || initiator.Map != recipient.Map || !CommandAuthorityBridge.CanActForPawn(initiator))
		{
			return;
		}
		dispatching = true;
		try
		{
			if (!player)
			{
				Call(TypeOf("Service.CustomDialogueService"), "DispatchDialogue", initiator, recipient, message, announcement, null);
			}
			else if (PhysicalHostEffectAllowed)
			{
				Call(TypeOf("Service.CustomDialogueService"), "DispatchDialogue", Call(TypeOf("Data.Cache"), "GetPlayer"), recipient, message, announcement, null);
			}
			if (!PhysicalHostEffectAllowed && initiator != null)
			{
				(Get(TypeOf("Service.CustomDialogueService"), "PendingDialogues") as IDictionary)?.Remove(initiator);
			}
		}
		finally
		{
			dispatching = false;
		}
	}

	private static bool HostContextPrefix(out LocalAiScope __state)
	{
		__state = null;
		if (!PhysicalHostEffectAllowed)
		{
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__state = new LocalAiScope();
		return true;
	}

	private static bool LocalContextPrefix(out LocalAiScope __state)
	{
		__state = null;
		if (!LocalPresentationAllowed)
		{
			return false;
		}
		if (MP.IsInMultiplayer)
		{
			__state = new LocalAiScope();
		}
		return true;
	}

	private static bool PersonaQueryPrefix(MethodBase __originalMethod, ref object __result, out LocalAiScope __state)
	{
		__state = null;
		if (!LocalPresentationAllowed)
		{
			return HostQueryPrefix(__originalMethod, ref __result);
		}
		return LocalContextPrefix(out __state);
	}

	private static bool InteractionContextPrefix(LogEntry entry, out LocalAiScope __state)
	{
		__state = null;
		if (!LocalPresentationAllowed)
		{
			return false;
		}
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (TypeOf("PlayLogEntry_RimTalkInteraction").IsInstanceOfType(entry))
		{
			return true;
		}
		if (!PhysicalHostEffectAllowed)
		{
			return false;
		}
		__state = new LocalAiScope();
		return true;
	}

	private static Exception AiScopeFinalizer(Exception __exception, LocalAiScope __state)
	{
		__state?.Dispose();
		return __exception;
	}

	internal static void PipelineUpdate()
	{
		if (!Enabled || !LocalPresentationAllowed)
		{
			return;
		}
		if (!MP.IsInMultiplayer || !PhysicalHostEffectAllowed || Current.Game == null)
		{
			if (pipelineGame != null)
			{
				Interlocked.Increment(ref pipelineEpoch);
				Call(TypeOf("Service.AIService"), "CancelCurrent");
				pipelineGame = null;
				pumpClocks.Clear();
				Action result;
				while (incoming.TryDequeue(out result))
				{
				}
			}
		}
		else
		{
			if (!MP.InInterface)
			{
				return;
			}
			EnsurePipelineGame();
			for (int i = 0; i < 256; i++)
			{
				if (!incoming.TryDequeue(out var result2))
				{
					break;
				}
				result2();
			}
			FlushMetadata();
			if (!(bool)Get(Call(TypeOf("Settings"), "Get"), "IsEnabled"))
			{
				return;
			}
			double num = (double)Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
			foreach (Map item in Find.Maps.OrderBy((Map m) => m.uniqueID))
			{
				if (!pumpClocks.TryGetValue(item.uniqueID, out var value))
				{
					value = (pumpClocks[item.uniqueID] = new PumpClock());
				}
				if (num < value.next)
				{
					continue;
				}
				object obj = pipelineMapOwner.Invoke(null, new object[1] { item });
				if (obj == null)
				{
					continue;
				}
				int num2 = (int)pipelineReadTicks.Invoke(null, new object[1] { obj });
				object[] array = new object[5]
				{
					obj,
					0,
					TimeSpeed.Paused,
					null,
					0
				};
				pipelineReadClock.Invoke(null, array);
				if ((TimeSpeed)array[2] == TimeSpeed.Paused || num2 == value.observed)
				{
					continue;
				}
				if (value.observed < 0)
				{
					value.lastRegular = num2;
				}
				value.observed = num2;
				value.next = num + 0.25;
				int num3 = (int)pipelineTicks.GetValue(Find.TickManager);
				object value2 = pipelineMapIndex.GetValue(Current.Game);
				object value3 = pipelineSpeed.GetValue(Find.TickManager);
				object value4 = pipelineSlower.GetValue(Find.TickManager);
				object value5 = pipelineAbsBase.GetValue(Find.TickManager);
				Map map = pumpMap;
				using (new LocalAiScope())
				{
					try
					{
						pipelineMapIndex.SetValue(Current.Game, (sbyte)item.Index);
						pipelineTicks.SetValue(Find.TickManager, num2);
						pipelineSpeed.SetValue(Find.TickManager, array[2]);
						pipelineSlower.SetValue(Find.TickManager, array[3]);
						pipelineAbsBase.SetValue(Find.TickManager, array[4]);
						pumpMap = item;
						if (num >= value.nextCache)
						{
							Call(TypeOf("Data.Cache"), "Refresh");
							value.nextCache = num + 5.0;
						}
						PumpPendingDialogues(item);
						Call(TypeOf("Service.TalkService"), "DisplayTalk");
						Call(TypeOf("Patch.TickManagerPatch"), "ProcessFastTrackRequests");
						Set(TypeOf("Patch.TickManagerPatch"), "_lastTalkEndTick", value.lastRegular);
						Call(TypeOf("Patch.TickManagerPatch"), "ProcessRegularTalkRequests");
						value.lastRegular = (int)Get(TypeOf("Patch.TickManagerPatch"), "_lastTalkEndTick");
					}
					finally
					{
						pipelineTicks.SetValue(Find.TickManager, num3);
						pipelineSpeed.SetValue(Find.TickManager, value3);
						pipelineSlower.SetValue(Find.TickManager, value4);
						pipelineAbsBase.SetValue(Find.TickManager, value5);
						pipelineMapIndex.SetValue(Current.Game, value2);
						pumpMap = map;
					}
				}
			}
		}
	}

	private static void InstallReplay()
	{
		ReplayEntryType = TypeOf("PlayLogEntry_RimTalkInteraction");
		ReplayTalkType = TypeOf("Source.Data.TalkType");
		ReplayInteractionType = TypeOf("Source.Data.InteractionType");
		ReplayEntryConstructor = AccessTools.DeclaredConstructor(ReplayEntryType, new Type[4]
		{
			typeof(InteractionDef),
			typeof(Pawn),
			typeof(Pawn),
			typeof(List<RulePackDef>)
		}) ?? throw new MissingMethodException(ReplayEntryType.FullName, ".ctor");
		ReplayCachedString = AccessTools.DeclaredField(ReplayEntryType, "_cachedString") ?? throw new MissingFieldException(ReplayEntryType.FullName, "_cachedString");
		if (ReplayCachedString.FieldType != typeof(string))
		{
			throw new NotSupportedException("Unreviewed RimTalk interaction text schema");
		}
		ReplayConversation = Property("ConversationId", typeof(int));
		ReplayInteraction = Property("InteractionType", ReplayInteractionType);
		ReplayTalk = Property("TalkType", ReplayTalkType);
		ReplayDirective = Property("HasDirective", typeof(bool));
		ReplayThoughtDef = AccessTools.DeclaredMethod(TypeOf("Source.Data.InteractionExtensions"), "GetThoughtDef", new Type[1] { ReplayInteractionType }) ?? throw new MissingMethodException("RimTalk.Source.Data.InteractionExtensions", "GetThoughtDef");
		ReplayNoInteraction = Enum.Parse(ReplayInteractionType, "None");
		ReplayChatInteraction = Enum.Parse(ReplayInteractionType, "Chat");
		MP.RegisterSyncMethod(typeof(RimTalk), "SyncReply").SetHostOnly().SetVersion(2);
		MethodInfo original = AccessTools.DeclaredMethod(TypeOf("Service.TalkService"), "GetTalk", new Type[1] { typeof(Pawn) }) ?? throw new MissingMethodException("RimTalk.Service.TalkService", "GetTalk");
		MpCompat.harmony.Patch(original, new HarmonyMethod(typeof(RimTalk), "ReplayGetTalkPrefix"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(PlayLogEntry_Interaction), "ExposeData"), null, new HarmonyMethod(typeof(RimTalk), "ReplayExposeEntryPostfix"));
		MethodInfo original2 = AccessTools.DeclaredMethod(TypeOf("Patches.SaveGamePatch"), "PreSaveGame") ?? throw new MissingMethodException("RimTalk.Patches.SaveGamePatch", "PreSaveGame");
		MpCompat.harmony.Patch(original2, new HarmonyMethod(typeof(RimTalk), "ReplayConvertSavePrefix"));
		InstallReplayPresentation();
		static PropertyInfo Property(string name, Type type)
		{
			PropertyInfo propertyInfo = AccessTools.DeclaredProperty(ReplayEntryType, name) ?? throw new MissingMemberException(ReplayEntryType.FullName, name);
			if (propertyInfo.PropertyType != type || !propertyInfo.CanRead || !propertyInfo.CanWrite)
			{
				throw new NotSupportedException("Unreviewed RimTalk interaction property " + name);
			}
			return propertyInfo;
		}
	}

	internal static void ReplaySubmit(Pawn speaker, Pawn target, string id, string text, int talkType, string interactionRaw, bool directive, bool effects, int conversation)
	{
		if (MP.IsInMultiplayer && PhysicalHostEffectAllowed && !MP.IsExecutingSyncCommand && MP.InInterface)
		{
			SyncReply(speaker, target, id, text, talkType, interactionRaw, directive, effects, conversation);
		}
	}

	private static bool ReplayValidText(string text)
	{
		if (string.IsNullOrWhiteSpace(text) || text.Length > 8192 || Encoding.UTF8.GetByteCount(text) > 16384)
		{
			return false;
		}
		try
		{
			XmlConvert.VerifyXmlChars(text);
			return true;
		}
		catch (XmlException)
		{
			return false;
		}
	}

	private static void SyncReply(Pawn speaker, Pawn target, string id, string text, int talkType, string interactionRaw, bool directive, bool effects, int conversation)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || !ReplayAvailable || Current.Game == null || speaker == null || speaker.Destroyed || !speaker.Spawned || speaker.Dead || speaker.thingIDNumber <= 0 || (target != null && (target.Destroyed || !target.Spawned || target.Dead || target.thingIDNumber <= 0 || target.Map != speaker.Map)) || !RimTalkReplyLedger.ValidId(id) || !ReplayValidText(text) || conversation < -1 || (interactionRaw != null && interactionRaw.Length > 64) || !Enum.IsDefined(ReplayTalkType, talkType))
		{
			return;
		}
		RimTalkReplyLedger component = Current.Game.GetComponent<RimTalkReplyLedger>();
		if (component == null || component.Contains(id))
		{
			return;
		}
		InteractionDef namedSilentFail = DefDatabase<InteractionDef>.GetNamedSilentFail("RimTalkInteraction");
		if (namedSilentFail == null || Find.PlayLog == null)
		{
			return;
		}
		if (target == null)
		{
			target = speaker;
		}
		object obj = ReplayNoInteraction;
		if (!string.IsNullOrWhiteSpace(interactionRaw) && Enum.TryParse(ReplayInteractionType, interactionRaw, ignoreCase: true, out object result))
		{
			obj = result;
		}
		Pawn pawn = replaySpeaker;
		string text2 = replayText;
		bool flag = replayTextActive;
		LogEntry logEntry;
		try
		{
			replaySpeaker = speaker;
			replayText = text;
			replayTextActive = true;
			logEntry = (LogEntry)ReplayEntryConstructor.Invoke(new object[4] { namedSilentFail, speaker, target, null });
		}
		finally
		{
			replaySpeaker = pawn;
			replayText = text2;
			replayTextActive = flag;
		}
		ReplayConversation.SetValue(logEntry, conversation);
		ReplayInteraction.SetValue(logEntry, obj);
		ReplayTalk.SetValue(logEntry, Enum.ToObject(ReplayTalkType, talkType));
		ReplayDirective.SetValue(logEntry, directive);
		if (!component.Remember(id))
		{
			return;
		}
		Find.PlayLog.Add(logEntry);
		ReplayPresentHistory(speaker, target, id, text, talkType, interactionRaw, conversation);
		if (!effects || speaker == target)
		{
			return;
		}
		ThoughtDef thoughtDef = (ThoughtDef)ReplayThoughtDef.Invoke(null, new object[1] { obj });
		if (thoughtDef != null)
		{
			target.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef, speaker);
			if (obj.Equals(ReplayChatInteraction))
			{
				speaker.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef, target);
			}
		}
	}

	private static bool ReplayGetTalkPrefix(Pawn pawn, ref string __result)
	{
		if (!replayTextActive || pawn != replaySpeaker)
		{
			return true;
		}
		__result = replayText;
		return false;
	}

	private static bool ReplayConvertSavePrefix()
	{
		return !MP.IsInMultiplayer;
	}

	private static void ReplayExposeEntryPostfix(PlayLogEntry_Interaction __instance)
	{
		if (!(ReplayEntryType == null) && !(__instance.GetType() != ReplayEntryType))
		{
			string value = (string)ReplayCachedString.GetValue(__instance);
			int value2 = (int)ReplayConversation.GetValue(__instance);
			int value3 = Convert.ToInt32(ReplayInteraction.GetValue(__instance));
			int value4 = Convert.ToInt32(ReplayTalk.GetValue(__instance));
			bool value5 = (bool)ReplayDirective.GetValue(__instance);
			Scribe_Values.Look(ref value, "mpRimTalkText");
			Scribe_Values.Look(ref value2, "mpRimTalkConversation", -1);
			Scribe_Values.Look(ref value3, "mpRimTalkInteraction", 0);
			Scribe_Values.Look(ref value4, "mpRimTalkTalkType", 0);
			Scribe_Values.Look(ref value5, "mpRimTalkDirective", defaultValue: false);
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				ReplayCachedString.SetValue(__instance, value);
				ReplayConversation.SetValue(__instance, value2);
				ReplayInteraction.SetValue(__instance, Enum.ToObject(ReplayInteractionType, value3));
				ReplayTalk.SetValue(__instance, Enum.ToObject(ReplayTalkType, value4));
				ReplayDirective.SetValue(__instance, value5);
			}
		}
	}

	private static void InstallReplayPresentation()
	{
		Type type = TypeOf("Data.TalkRequest");
		Type log = TypeOf("Data.ApiLog");
		Type type2 = TypeOf("Client.Payload");
		Type type3 = TypeOf("Source.Data.Channel");
		ReplayRequestConstructor = AccessTools.DeclaredConstructor(type, new Type[4]
		{
			typeof(string),
			typeof(Pawn),
			typeof(Pawn),
			ReplayTalkType
		}) ?? throw new MissingMethodException(type.FullName, ".ctor");
		ReplayApiLogConstructor = AccessTools.DeclaredConstructor(log, new Type[6]
		{
			typeof(string),
			type,
			typeof(string),
			type2,
			typeof(DateTime),
			type3
		}) ?? throw new MissingMethodException(log.FullName, ".ctor");
		ReplayApiLogId = AccessTools.DeclaredField(log, "<Id>k__BackingField") ?? throw new MissingFieldException(log.FullName, "<Id>k__BackingField");
		if (ReplayApiLogId.FieldType != typeof(Guid))
		{
			throw new NotSupportedException("Unreviewed RimTalk API history identity");
		}
		ReplayApiLogInteraction = AccessTools.DeclaredField(log, "InteractionType") ?? throw new MissingFieldException(log.FullName, "InteractionType");
		ReplayApiLogConversation = Property("ConversationId", typeof(int));
		ReplayApiLogTarget = Property("TargetName", typeof(string));
		ReplayApiLogSpokenTick = Property("SpokenTick", typeof(int));
		ReplayApiLogSpokenTime = Property("SpokenTime", typeof(DateTime?));
		ReplayApiGet = AccessTools.DeclaredMethod(TypeOf("Data.ApiHistory"), "GetApiLog", new Type[1] { typeof(Guid) }) ?? throw new MissingMethodException("RimTalk.Data.ApiHistory", "GetApiLog");
		ReplayApiRegister = AccessTools.DeclaredMethod(TypeOf("Data.ApiHistory"), "RegisterLog", new Type[1] { log }) ?? throw new MissingMethodException("RimTalk.Data.ApiHistory", "RegisterLog");
		ReplayOverlayNotify = AccessTools.DeclaredMethod(TypeOf("UI.Overlay"), "NotifyLogUpdated", Type.EmptyTypes) ?? throw new MissingMethodException("RimTalk.UI.Overlay", "NotifyLogUpdated");
		ReplayStreamChannel = Enum.Parse(type3, "Stream");
		PropertyInfo Property(string name, Type expected)
		{
			PropertyInfo propertyInfo = AccessTools.DeclaredProperty(log, name) ?? throw new MissingMemberException(log.FullName, name);
			if (propertyInfo.PropertyType != expected || !propertyInfo.CanWrite)
			{
				throw new NotSupportedException("Unreviewed RimTalk API history property " + name);
			}
			return propertyInfo;
		}
	}

	private static void ReplayPresentHistory(Pawn speaker, Pawn target, string id, string text, int talkType, string interaction, int conversation)
	{
		if (!LocalPresentationAllowed)
		{
			return;
		}
		try
		{
			Guid guid = Guid.ParseExact(id, "D");
			if (ReplayApiGet.Invoke(null, new object[1] { guid }) == null)
			{
				object obj = ReplayRequestConstructor.Invoke(new object[4]
				{
					null,
					speaker,
					target,
					Enum.ToObject(ReplayTalkType, talkType)
				});
				DateTime now = DateTime.Now;
				object obj2 = ReplayApiLogConstructor.Invoke(new object[6] { speaker.LabelShort, obj, text, null, now, ReplayStreamChannel });
				ReplayApiLogId.SetValue(obj2, guid);
				ReplayApiLogConversation.SetValue(obj2, conversation);
				ReplayApiLogTarget.SetValue(obj2, target?.LabelShort);
				ReplayApiLogInteraction.SetValue(obj2, interaction);
				ReplayApiLogSpokenTick.SetValue(obj2, Find.TickManager.TicksGame);
				ReplayApiLogSpokenTime.SetValue(obj2, now);
				ReplayApiRegister.Invoke(null, new object[1] { obj2 });
				ReplayOverlayNotify.Invoke(null, null);
			}
		}
		catch (Exception ex)
		{
			if (!replayPresentationWarning)
			{
				replayPresentationWarning = true;
				Log.Warning("Multiplayer RimTalk dialogue panel could not mirror a synchronized reply (" + ex.GetType().Name + ").");
			}
		}
	}

	private static void InstallState()
	{
		StatePersonaType = TypeOf("Data.Hediff_Persona");
		StateMemoryType = TypeOf("PawnMemory.MemoryEntry");
		StatePersonality = Field("Personality");
		StateWeight = Field("TalkInitiationWeight");
		StateSpoken = Field("_spokenThoughtTicks");
		StateMemories = Field("_memories");
		StateImpressionTick = Field("_lastImpressionTick");
		StateImpressionTarget = Field("_lastImpressionTargetId");
		StateImpression = Field("_cachedImpression");
		if (StatePersonality.FieldType != typeof(string) || StateWeight.FieldType != typeof(float) || StateSpoken.FieldType != typeof(Dictionary<string, int>) || StateMemories.FieldType != typeof(List<>).MakeGenericType(StateMemoryType))
		{
			throw new NotSupportedException("Unreviewed RimTalk persona collection schema");
		}
		StateMemoryFields = (from f in AccessTools.GetDeclaredFields(StateMemoryType)
			where !f.IsStatic
			select f).OrderBy((FieldInfo f) => f.Name, StringComparer.Ordinal).ToArray();
		string[] second = new string[13]
		{
			"BaseWeight", "Count", "CreatedTick", "EventKey", "IsCoreTrauma", "IsDirective", "IsMilestone", "LastRecalledTick", "LastTick", "Note",
			"Perspective", "TargetPawnId", "TargetPawnName"
		};
		if (!StateMemoryFields.Select((FieldInfo f) => f.Name).SequenceEqual(second))
		{
			throw new NotSupportedException("Unreviewed RimTalk memory entry schema");
		}
		Type[] array = new Type[13]
		{
			typeof(float),
			typeof(int),
			typeof(int),
			typeof(string),
			typeof(bool),
			typeof(bool),
			typeof(bool),
			typeof(int),
			typeof(int),
			typeof(string),
			TypeOf("PawnMemory.MemoryPerspective"),
			typeof(int),
			typeof(string)
		};
		if (!StateMemoryFields.Select((FieldInfo f) => f.FieldType).SequenceEqual(array) || Enum.GetUnderlyingType(array[10]) != typeof(int) || !Enum.GetValues(array[10]).Cast<object>().Select(Convert.ToInt32)
			.SequenceEqual(new int[3] { 0, 1, 2 }))
		{
			throw new NotSupportedException("Unreviewed RimTalk memory entry types");
		}
		StateIsBaby = PawnQuery("IsBaby");
		StateIsVisitor = PawnQuery("IsVisitor");
		StateIsEnemy = PawnQuery("IsEnemy");
		StateIsPlayer = PawnQuery("IsPlayer");
		Patch("Data.Hediff_Persona", "GetOrAddNew", "StateGetPersonaPrefix");
		MP.RegisterSyncMethod(typeof(RimTalk), "StateSyncPersona").SetVersion(2);
		Patch("Data.PersonaService", "SetPersonality", "StateSetPersonalityPrefix", "StatePersonaSettingPostfix");
		Patch("Data.PersonaService", "SetTalkInitiationWeight", "StateSetWeightPrefix", "StatePersonaSettingPostfix");
		static FieldInfo Field(string name)
		{
			return AccessTools.DeclaredField(StatePersonaType, name) ?? throw new MissingFieldException(StatePersonaType.FullName, name);
		}
		static Func<Pawn, bool> PawnQuery(string name)
		{
			return AccessTools.DeclaredMethod(TypeOf("Util.PawnUtil"), name, new Type[1] { typeof(Pawn) })?.CreateDelegate<Func<Pawn, bool>>() ?? throw new MissingMethodException("RimTalk.Util.PawnUtil", name);
		}
	}

	private static bool StateGetPersonaPrefix(Pawn pawn, ref object __result)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		__result = StatePersona(pawn);
		return false;
	}

	internal static object StatePersona(Pawn pawn)
	{
		if (StatePersonaType == null || pawn == null || Current.Game == null)
		{
			return null;
		}
		return StateStoreFor(Current.Game).Get(pawn);
	}

	internal static RimTalkPersonaStore StateStoreFor(Game game)
	{
		return StateGames.GetValue(game, (Game _) => new RimTalkPersonaStore());
	}

	private static bool StateSetPersonalityPrefix(Pawn pawn, string personality)
	{
		if (!MP.IsInMultiplayer || (pawn != null && StateIsPlayer(pawn)))
		{
			return true;
		}
		if (!MP.InInterface || MP.IsExecutingSyncCommand)
		{
			return MP.IsHosting;
		}
		StateSyncPersona(pawn, personality, 0f, 1);
		return false;
	}

	private static bool StateSetWeightPrefix(Pawn pawn, float frequency)
	{
		if (!MP.IsInMultiplayer || (pawn != null && StateIsPlayer(pawn)))
		{
			return true;
		}
		if (!MP.InInterface || MP.IsExecutingSyncCommand)
		{
			return MP.IsHosting;
		}
		StateSyncPersona(pawn, null, frequency, 2);
		return false;
	}

	private static void StatePersonaSettingPostfix(Pawn pawn)
	{
		MarkMetadataDirty(pawn);
	}

	private static void StateSyncPersona(Pawn pawn, string personality, float weight, byte fields)
	{
		bool flag = Current.Game == null || pawn == null || pawn.thingIDNumber <= 0 || StateIsPlayer(pawn);
		if (!flag)
		{
			bool flag2 = ((fields < 1 || fields > 3) ? true : false);
			flag = flag2;
		}
		if (flag || ((fields & 1) != 0 && (personality?.Length ?? 0) > 8192) || ((fields & 2) != 0 && (!float.IsFinite(weight) || weight < 0f)))
		{
			return;
		}
		StateStoreFor(Current.Game).ApplyOrderedFields(pawn, personality, weight, fields);
		if (!MP.IsHosting)
		{
			return;
		}
		object obj = StatePersona(pawn);
		if (obj != null)
		{
			if ((fields & 1) != 0)
			{
				StatePersonality.SetValue(obj, personality);
			}
			if ((fields & 2) != 0)
			{
				StateWeight.SetValue(obj, weight);
			}
			MarkMetadataDirty(pawn);
		}
	}

	internal static object StateMakePersona(Pawn pawn, RimTalkPersonaRecord record)
	{
		Hediff hediff = (Hediff)RuntimeHelpers.GetUninitializedObject(StatePersonaType);
		hediff.pawn = pawn;
		hediff.def = DefDatabase<HediffDef>.GetNamedSilentFail("RimTalk_PersonaData");
		StatePersonality.SetValue(hediff, record.Personality);
		StateWeight.SetValue(hediff, record.Weight);
		StateSpoken.SetValue(hediff, new Dictionary<string, int>(record.Spoken ?? new Dictionary<string, int>(), StringComparer.Ordinal));
		IList list = (IList)Activator.CreateInstance(StateMemories.FieldType);
		foreach (IExposable item in record.Memories ?? new List<IExposable>())
		{
			if (item != null)
			{
				list.Add(StateCloneMemory(item));
			}
		}
		StateMemories.SetValue(hediff, list);
		StateImpressionTick.SetValue(hediff, -1);
		StateImpressionTarget.SetValue(hediff, -1);
		StateImpression.SetValue(hediff, string.Empty);
		return hediff;
	}

	internal static RimTalkPersonaRecord StateCapturePersona(int pawnId, object native)
	{
		RimTalkPersonaRecord rimTalkPersonaRecord = new RimTalkPersonaRecord
		{
			PawnId = pawnId,
			Personality = (string)StatePersonality.GetValue(native),
			Weight = (float)StateWeight.GetValue(native),
			Spoken = new Dictionary<string, int>(((Dictionary<string, int>)StateSpoken.GetValue(native)) ?? new Dictionary<string, int>(), StringComparer.Ordinal)
		};
		if (StateMemories.GetValue(native) is IList list)
		{
			foreach (object item in list)
			{
				if (item != null)
				{
					rimTalkPersonaRecord.Memories.Add((IExposable)StateCloneMemory(item));
				}
			}
		}
		return rimTalkPersonaRecord;
	}

	private static object StateCloneMemory(object memory)
	{
		if (memory.GetType() != StateMemoryType)
		{
			throw new NotSupportedException("Unreviewed RimTalk memory entry type");
		}
		object uninitializedObject = RuntimeHelpers.GetUninitializedObject(StateMemoryType);
		FieldInfo[] stateMemoryFields = StateMemoryFields;
		foreach (FieldInfo fieldInfo in stateMemoryFields)
		{
			fieldInfo.SetValue(uninitializedObject, fieldInfo.GetValue(memory));
		}
		return uninitializedObject;
	}

	internal static RimTalkPersonaRecord StateCloneRecord(RimTalkPersonaRecord source)
	{
		RimTalkPersonaRecord rimTalkPersonaRecord = new RimTalkPersonaRecord
		{
			PawnId = source.PawnId,
			Revision = source.Revision,
			Personality = source.Personality,
			Weight = source.Weight,
			Spoken = new Dictionary<string, int>(source.Spoken ?? new Dictionary<string, int>(), StringComparer.Ordinal)
		};
		foreach (IExposable item in source.Memories ?? new List<IExposable>())
		{
			if (item != null)
			{
				rimTalkPersonaRecord.Memories.Add((IExposable)StateCloneMemory(item));
			}
		}
		return rimTalkPersonaRecord;
	}

	internal static RimTalkPersonaRecord StateInitialRecord(Pawn pawn)
	{
		Hediff hediff = pawn.health?.hediffSet?.hediffs.FirstOrDefault((Hediff h) => h.GetType() == StatePersonaType);
		if (hediff != null)
		{
			return StateCapturePersona(pawn.thingIDNumber, hediff);
		}
		Type target = TypeOf("Data.Constant");
		object target2;
		if (pawn.IsMutant)
		{
			target2 = Get(target, "PersonaNonHuman");
		}
		else if (pawn.RaceProps.Humanlike)
		{
			target2 = ((!StateIsBaby(pawn)) ? StateAdultProfile(pawn) : Get(target, "PersonaBaby"));
		}
		else
		{
			target2 = Get(target, pawn.RaceProps.Animal ? "PersonaAnimal" : (pawn.RaceProps.IsMechanoid ? "PersonaMech" : "PersonaNonHuman"));
		}
		return new RimTalkPersonaRecord
		{
			PawnId = pawn.thingIDNumber,
			Personality = (string)Get(target2, "Persona"),
			Weight = ((pawn.IsSlave || pawn.IsPrisoner || StateIsVisitor(pawn) || StateIsEnemy(pawn)) ? 0.2f : ((float)Get(target2, "Chattiness")))
		};
	}

	private static object StateAdultProfile(Pawn pawn)
	{
		if (StateAdultProfiles == null)
		{
			StateAdultProfiles = ((IEnumerable)Get(TypeOf("Data.Constant"), "Personalities")).Cast<object>().ToArray();
		}
		if (StateAdultProfiles.Length == 0)
		{
			throw new InvalidOperationException("RimTalk has no persona profiles");
		}
		int num = new System.Random(pawn.thingIDNumber ^ 0x52A17F9).Next(StateAdultProfiles.Length);
		return StateAdultProfiles[num];
	}

	internal static void StateRefreshAge(Pawn pawn, object native)
	{
		if (pawn.RaceProps.Humanlike && !StateIsBaby(pawn))
		{
			object target = Get(TypeOf("Data.Constant"), "PersonaBaby");
			if (!((string)StatePersonality.GetValue(native) != (string)Get(target, "Persona")))
			{
				object target2 = StateAdultProfile(pawn);
				StatePersonality.SetValue(native, Get(target2, "Persona"));
				StateWeight.SetValue(native, Get(target2, "Chattiness"));
			}
		}
	}
}
