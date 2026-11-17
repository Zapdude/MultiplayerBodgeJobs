using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class IsekaiDeferredExecution
{
	private readonly record struct PendingAction(Action Action, object Owner, Faction Faction);

	private static readonly Queue<PendingAction> Pending = new Queue<PendingAction>();

	private static Game pendingGame;

	private static long pendingTransaction;

	private static bool draining;

	private static Func<long> currentTransaction;

	private static Func<object> currentOwner;

	private static Func<object, Map> ownerMap;

	private static Action<object> mapPre;

	private static Action<object> mapPost;

	private static Action<object> worldPre;

	private static Action<object> worldPost;

	private static Func<Map> getTickingMap;

	private static Action<Map> setTickingMap;

	private static Action<Map, Faction, bool> pushFaction;

	private static Func<Map, Faction> popFaction;

	private static HashSet<Assembly> isekaiAssemblies;

	private static bool installed;

	public static void Setup(ModContentPack mod)
	{
		List<Assembly> list = mod?.assemblies?.loadedAssemblies;
		if (list == null || list.Count == 0)
		{
			return;
		}
		if (isekaiAssemblies == null)
		{
			isekaiAssemblies = new HashSet<Assembly>();
		}
		foreach (Assembly item in list)
		{
			isekaiAssemblies.Add(item);
		}
		if (!installed)
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(LongEventHandler), "ExecuteWhenFinished");
			if (methodInfo == null)
			{
				Log.Error("[IsekaiMP] Could not find LongEventHandler.ExecuteWhenFinished — deferred quest actions will stay unsynchronized.");
				return;
			}
			InitializeTransactions();
			MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(IsekaiDeferredExecution), "ExecuteWhenFinishedPrefix"));
			installed = true;
		}
	}

	private static void InitializeTransactions()
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.SimulationTransaction") ?? throw new TypeLoadException("Multiplayer.Client.SimulationTransaction");
		currentTransaction = AccessTools.PropertyGetter(type, "CurrentId").CreateDelegate<Func<long>>();
		currentOwner = AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.SimulationTimeContext") ?? throw new TypeLoadException("Multiplayer.Client.SimulationTimeContext"), "CurrentOwner").CreateDelegate<Func<object>>();
		Type type2 = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp") ?? throw new TypeLoadException("Multiplayer.Client.AsyncTimeComp");
		Type type3 = AccessTools.TypeByName("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp") ?? throw new TypeLoadException("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
		mapPre = BindOwnerAction(AccessTools.DeclaredMethod(type2, "PreContext"));
		mapPost = BindOwnerAction(AccessTools.DeclaredMethod(type2, "PostContext"));
		worldPre = BindOwnerAction(AccessTools.DeclaredMethod(type3, "PreContextForCallback"));
		worldPost = BindOwnerAction(AccessTools.DeclaredMethod(type3, "PostContext"));
		FieldInfo field = AccessTools.DeclaredField(type2, "map");
		DynamicMethod dynamicMethod = new DynamicMethod("IsekaiDeferredOwnerMap", typeof(Map), new Type[1] { typeof(object) }, typeof(IsekaiDeferredExecution), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		Label label = iLGenerator.DefineLabel();
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Isinst, type2);
		iLGenerator.Emit(OpCodes.Dup);
		iLGenerator.Emit(OpCodes.Brfalse_S, label);
		iLGenerator.Emit(OpCodes.Ldfld, field);
		iLGenerator.Emit(OpCodes.Ret);
		iLGenerator.MarkLabel(label);
		iLGenerator.Emit(OpCodes.Pop);
		iLGenerator.Emit(OpCodes.Ldnull);
		iLGenerator.Emit(OpCodes.Ret);
		ownerMap = dynamicMethod.CreateDelegate<Func<object, Map>>();
		FieldInfo field2 = AccessTools.DeclaredField(type2, "tickingMap");
		DynamicMethod dynamicMethod2 = new DynamicMethod("IsekaiDeferredGetMap", typeof(Map), Type.EmptyTypes, typeof(IsekaiDeferredExecution), skipVisibility: true);
		ILGenerator iLGenerator2 = dynamicMethod2.GetILGenerator();
		iLGenerator2.Emit(OpCodes.Ldsfld, field2);
		iLGenerator2.Emit(OpCodes.Ret);
		getTickingMap = dynamicMethod2.CreateDelegate<Func<Map>>();
		DynamicMethod dynamicMethod3 = new DynamicMethod("IsekaiDeferredSetMap", typeof(void), new Type[1] { typeof(Map) }, typeof(IsekaiDeferredExecution), skipVisibility: true);
		ILGenerator iLGenerator3 = dynamicMethod3.GetILGenerator();
		iLGenerator3.Emit(OpCodes.Ldarg_0);
		iLGenerator3.Emit(OpCodes.Stsfld, field2);
		iLGenerator3.Emit(OpCodes.Ret);
		setTickingMap = dynamicMethod3.CreateDelegate<Action<Map>>();
		Type type4 = AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions");
		pushFaction = AccessTools.DeclaredMethod(type4, "PushFaction", new Type[3]
		{
			typeof(Map),
			typeof(Faction),
			typeof(bool)
		}).CreateDelegate<Action<Map, Faction, bool>>();
		popFaction = AccessTools.DeclaredMethod(type4, "PopFaction", new Type[1] { typeof(Map) }).CreateDelegate<Func<Map, Faction>>();
		EventInfo? eventInfo = type.GetEvent("Completing") ?? throw new MissingMemberException(type.FullName, "Completing");
		EventInfo eventInfo2 = type.GetEvent("Aborted") ?? throw new MissingMemberException(type.FullName, "Aborted");
		eventInfo.AddEventHandler(null, new Action<long>(DrainPending));
		eventInfo2.AddEventHandler(null, new Action(ClearPending));
	}

	private static Action<object> BindOwnerAction(MethodInfo method)
	{
		DynamicMethod dynamicMethod = new DynamicMethod("IsekaiDeferred" + method.DeclaringType.Name + method.Name, typeof(void), new Type[1] { typeof(object) }, typeof(IsekaiDeferredExecution), skipVisibility: true);
		ILGenerator iLGenerator = dynamicMethod.GetILGenerator();
		iLGenerator.Emit(OpCodes.Ldarg_0);
		iLGenerator.Emit(OpCodes.Castclass, method.DeclaringType);
		iLGenerator.Emit(OpCodes.Call, method);
		iLGenerator.Emit(OpCodes.Ret);
		return dynamicMethod.CreateDelegate<Action<object>>();
	}

	private static bool ExecuteWhenFinishedPrefix(Action action)
	{
		if (!MP.IsInMultiplayer || action == null || isekaiAssemblies == null || Current.Game == null || Scribe.mode != LoadSaveMode.Inactive)
		{
			return true;
		}
		Type type = action.Method?.DeclaringType;
		if (type == null || !isekaiAssemblies.Contains(type.Assembly))
		{
			return true;
		}
		while (type.DeclaringType != null)
		{
			type = type.DeclaringType;
		}
		if (!typeof(QuestPart).IsAssignableFrom(type))
		{
			return true;
		}
		if (pendingGame != Current.Game)
		{
			ClearPending();
		}
		long num = currentTransaction();
		object obj = currentOwner();
		if (num == 0L || obj == null)
		{
			return true;
		}
		if (Pending.Count > 0 && pendingTransaction != num)
		{
			throw new InvalidOperationException("Isekai deferred actions outlived their simulation transaction");
		}
		pendingGame = Current.Game;
		pendingTransaction = num;
		Pending.Enqueue(new PendingAction(action, obj, Find.FactionManager?.OfPlayer));
		return false;
	}

	private static void ClearPending()
	{
		Pending.Clear();
		pendingGame = null;
		pendingTransaction = 0L;
	}

	private static void DrainPending(long transaction)
	{
		if (!MP.IsInMultiplayer || pendingGame != Current.Game)
		{
			ClearPending();
		}
		else
		{
			if (draining || transaction != pendingTransaction)
			{
				return;
			}
			Game game = pendingGame;
			draining = true;
			try
			{
				while (Pending.Count > 0 && pendingGame == game && Current.Game == game)
				{
					try
					{
						Run(Pending.Dequeue());
					}
					catch (Exception value)
					{
						Log.Warning($"[IsekaiMP] Deferred action failed: {value}");
					}
				}
			}
			finally
			{
				draining = false;
				if (pendingGame == game)
				{
					ClearPending();
				}
			}
		}
	}

	private static void Run(PendingAction entry)
	{
		Map map = ownerMap(entry.Owner);
		Map obj = getTickingMap();
		bool flag = currentOwner() != entry.Owner;
		bool flag2 = false;
		if (flag)
		{
			((map == null) ? worldPre : mapPre)(entry.Owner);
		}
		try
		{
			if (map != null)
			{
				setTickingMap(map);
			}
			if (entry.Faction != null)
			{
				pushFaction(map, entry.Faction, arg3: true);
				flag2 = true;
			}
			entry.Action();
		}
		finally
		{
			try
			{
				if (flag2)
				{
					popFaction(map);
				}
			}
			finally
			{
				try
				{
					if (flag)
					{
						((map == null) ? worldPost : mapPost)(entry.Owner);
					}
				}
				finally
				{
					setTickingMap(obj);
				}
			}
		}
	}
}
