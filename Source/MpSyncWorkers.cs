using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public static class MpSyncWorkers
{
	private static readonly HashSet<Type> AlreadyRegistered = new HashSet<Type>();

	public static void Requires<T>()
	{
		Requires(typeof(T));
	}

	public static void Requires(Type type)
	{
		if (AlreadyRegistered.Add(type))
		{
			if (type == typeof(PocketMapParent))
			{
				MP.RegisterSyncWorker<PocketMapParent>(SyncPocketMapParent, null, isImplicit: true);
			}
			else if (HasSyncWorker(type))
			{
				Log.Warning($"Sync worker of type {type} already exists in MP, temporary sync worker can be removed from MP Compat");
			}
			else if (type == typeof(GameCondition))
			{
				MP.RegisterSyncWorker<GameCondition>(SyncGameCondition, null, isImplicit: true);
			}
			else
			{
				Log.Error($"Trying to register SyncWorker of type {type}, but it's not supported.\n{new StackTrace(1)}");
			}
		}
	}

	private static void SyncGameCondition(SyncWorker sync, ref GameCondition gameCondition)
	{
		if (sync.isWriting)
		{
			sync.Write(gameCondition.gameConditionManager.ownerMap);
			sync.Write(gameCondition.uniqueID);
			return;
		}
		Map map = sync.Read<Map>();
		int id = sync.Read<int>();
		GameConditionManager gameConditionManager = ((map == null) ? Find.World.GameConditionManager : map.GameConditionManager);
		gameCondition = gameConditionManager.ActiveConditions.FirstOrDefault((GameCondition condition) => condition.uniqueID == id);
	}

	private static void SyncPocketMapParent(SyncWorker sync, ref PocketMapParent pmp)
	{
		if (sync.isWriting)
		{
			sync.Write(pmp?.ID ?? (-1));
			return;
		}
		int id = sync.Read<int>();
		if (id != -1 && pmp == null)
		{
			pmp = Find.World.pocketMaps.Find((PocketMapParent p) => p.ID == id);
		}
	}

	private static bool HasSyncWorker(Type type)
	{
		FieldInfo fieldInfo = AccessTools.DeclaredField("Multiplayer.Client.Multiplayer:serialization");
		if (fieldInfo == null)
		{
			Error("failed to find field Multiplayer.Client.Multiplayer:serialization");
			return false;
		}
		if (!fieldInfo.IsStatic)
		{
			Error("field Multiplayer.Client.Multiplayer:serialization is not static");
			return false;
		}
		object value = fieldInfo.GetValue(null);
		if (value == null)
		{
			Error("field Multiplayer.Client.Multiplayer:serialization is null");
			return false;
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod(fieldInfo.FieldType, "CanHandle", new Type[1] { typeof(SyncType) });
		if (methodInfo == null)
		{
			Error("failed to find method CanHandle");
			return false;
		}
		if (methodInfo.ReturnType != typeof(bool))
		{
			Error($"return type is not bool but {methodInfo.ReturnType} for method {"CanHandle"}");
			return false;
		}
		if (methodInfo.IsStatic)
		{
			Log.Warning("Method CanHandle is static, but we expected it to be non-static - the results may potentially be incorrect.");
			return (bool)methodInfo.Invoke(null, new object[1]
			{
				new SyncType(type)
			});
		}
		return (bool)methodInfo.Invoke(value, new object[1]
		{
			new SyncType(type)
		});
		void Error(string message)
		{
			Log.Error($"Failed to check if sync worker for type {type} is already registered in MP - {message}");
		}
	}
}
