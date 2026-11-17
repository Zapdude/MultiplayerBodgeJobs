using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Fluffy.MedicalTab")]
[MpCompatFor("tofudriver.MedicalTabForked")]
[MpCompatFor("memegoddess.MedicalTab")]
internal class MedicalTab
{
	private delegate MainTabWindow_PawnTable GetMainTab();

	private static ISyncField syncMedCare;

	private static ISyncField[] syncDefaultCare;

	private static GetMainTab mainTabGetter;

	public MedicalTab(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("Multiplayer.Client.SyncFields");
		syncMedCare = (ISyncField)AccessTools.Field(type, "SyncMedCare").GetValue(null);
		syncDefaultCare = (ISyncField[])AccessTools.Field(type, "SyncDefaultCare").GetValue(null);
		type = AccessTools.TypeByName("Fluffy.MainTabWindow_Medical");
		mainTabGetter = AccessTools.MethodDelegate<GetMainTab>(AccessTools.PropertyGetter(type, "Instance"));
		type = AccessTools.TypeByName("Fluffy.PawnColumnWorker_MedicalCare");
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoHeader"), new HarmonyMethod(typeof(MedicalTab), "PreDoHeader"), new HarmonyMethod(typeof(MedicalTab), "StopWatch"));
		MpCompat.harmony.Patch(AccessTools.Method(type, "DoCell"), new HarmonyMethod(typeof(MedicalTab), "PreDoCell"), new HarmonyMethod(typeof(MedicalTab), "StopWatch"));
		type = AccessTools.TypeByName("Fluffy.PawnColumnWorker_SelfTend");
		MP.RegisterSyncMethod(type, "SetValue");
	}

	private static void PreDoHeader()
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		MP.WatchBegin();
		ISyncField[] array = syncDefaultCare;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Watch();
		}
		foreach (Pawn item in mainTabGetter().table.PawnsListForReading)
		{
			syncMedCare.Watch(item);
		}
	}

	private static void PreDoCell(Pawn pawn)
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			syncMedCare.Watch(pawn);
		}
	}

	private static void StopWatch()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}
}
