using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Adamas.HospitalityCasino")]
public class HospitalityCasino
{
	public HospitalityCasino(ModContentPack mod)
	{
		InitializeGizmos("HospitalityCasino");
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			AccessTools.DeclaredMethod("HospitalityCasino.HospitalityCasinoMod:InitialiseTextures").Invoke(null, Array.Empty<object>());
		});
	}

	public static void InitializeGizmos(string targetNamespace)
	{
		Type type = AccessTools.TypeByName(targetNamespace + ".CompVendingMachine");
		MP.RegisterSyncMethod(type, "ToggleActive");
		MP.RegisterSyncMethod(type, "SetPricing");
		MP.RegisterSyncMethod(type, "Pricing");
	}
}
