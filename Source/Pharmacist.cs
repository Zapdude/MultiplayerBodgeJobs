using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Fluffy.Pharmacist")]
internal class Pharmacist
{
	private delegate void SetDefaults();

	private static SetDefaults setDefaultsMethod;

	private static AccessTools.FieldRef<object> medicalCareField;

	private static ISyncField diseaseMarginField;

	private static ISyncField minorWoundsThresholdField;

	private static ISyncField diseaseThresholdField;

	public Pharmacist(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("Pharmacist.PharmacistSettings");
		Type type2 = type;
		setDefaultsMethod = AccessTools.MethodDelegate<SetDefaults>(AccessTools.Method(type, "SetDefaults"));
		medicalCareField = AccessTools.StaticFieldRefAccess<object>(AccessTools.Field(type, "medicalCare"));
		type = AccessTools.Inner(type2, "MedicalCare");
		diseaseMarginField = MP.RegisterSyncField(type, "_diseaseMargin");
		minorWoundsThresholdField = MP.RegisterSyncField(type, "_minorWoundsThreshold");
		diseaseThresholdField = MP.RegisterSyncField(type, "_diseaseThreshold");
		MP.RegisterSyncWorker<object>(SyncMedicalCare, type);
		type = AccessTools.TypeByName("Pharmacist.MainTabWindow_Pharmacist");
		MpCompat.RegisterLambdaDelegate(type, "DrawCareSelectors", 0, 1, 2);
		MpCompat.harmony.Patch(AccessTools.Method(type, "DrawOptions"), new HarmonyMethod(typeof(Pharmacist), "PreDrawOptions"), new HarmonyMethod(typeof(Pharmacist), "PostDrawOptions"));
	}

	private static void PreDrawOptions()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchBegin();
			object target = medicalCareField();
			diseaseMarginField.Watch(target);
			minorWoundsThresholdField.Watch(target);
			diseaseThresholdField.Watch(target);
		}
	}

	private static void PostDrawOptions()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}

	private static void SyncMedicalCare(SyncWorker sync, ref object obj)
	{
		if (!sync.isWriting)
		{
			obj = medicalCareField();
			if (obj == null)
			{
				setDefaultsMethod();
				obj = medicalCareField();
			}
		}
	}
}
