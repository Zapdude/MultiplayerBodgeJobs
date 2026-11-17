using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("hatti.qualitybuilder")]
internal class QualityBuilder
{
	public QualityBuilder(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("QualityBuilder.CompQualityBuilder");
		Type type2 = AccessTools.TypeByName("QualityBuilder.CompQualityBuilder+ToggleCommand+<>c");
		Type type3 = AccessTools.TypeByName("QualityBuilder._Designator_SkilledBuilder+<>c");
		Type[] paramsType = new Type[1] { typeof(QualityCategory) };
		MethodInfo firstMethodBySignature = MpMethodUtil.GetFirstMethodBySignature(type2, paramsType);
		MethodInfo firstMethodBySignature2 = MpMethodUtil.GetFirstMethodBySignature(type3, paramsType);
		MP.RegisterSyncWorker<object>(SyncTypes, type2);
		MP.RegisterSyncWorker<object>(SyncTypes, type3);
		MP.RegisterSyncMethod(type, "ToggleSkilled");
		MP.RegisterSyncMethod(firstMethodBySignature).SetContext(SyncContext.MapSelected);
		MP.RegisterSyncMethod(firstMethodBySignature2);
	}

	private void SyncTypes(SyncWorker sync, ref object c)
	{
		if (sync.isWriting)
		{
			sync.Write(c.GetType());
		}
		else
		{
			c = Activator.CreateInstance(sync.Read<Type>());
		}
	}
}
