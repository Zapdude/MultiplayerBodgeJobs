using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("RimEffect.Core")]
public class RimEffectCore
{
	private static ConstructorInfo doorValueCtor;

	private static FieldInfo doorValueKeyField;

	public RimEffectCore(ModContentPack mod)
	{
		MpCompat.RegisterLambdaMethod("RimEffect.AmmoBelt", "GetWornGizmos", 1);
		Type type = AccessTools.TypeByName("RimEffect.Building_AutoDoorLockable");
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "GetGizmos", MethodType.Normal, null, 1);
		Type fieldType = AccessTools.Field(lambda.DeclaringType, "doorState").FieldType;
		Type[] genericArguments = fieldType.GetGenericArguments();
		doorValueCtor = AccessTools.DeclaredConstructor(fieldType, new Type[2]
		{
			genericArguments[0],
			genericArguments[1]
		});
		doorValueKeyField = AccessTools.Field(fieldType, "key");
		MP.RegisterSyncDelegate(type, lambda.DeclaringType.Name, lambda.Name);
		MP.RegisterSyncWorker<object>(SyncDoorValue, fieldType);
	}

	private static void SyncDoorValue(SyncWorker sync, ref object doorValue)
	{
		if (sync.isWriting)
		{
			sync.Write((int)doorValueKeyField.GetValue(doorValue));
			return;
		}
		doorValue = doorValueCtor.Invoke(new object[2]
		{
			sync.Read<int>(),
			string.Empty
		});
	}
}
