using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPE.Puppeteer")]
public class VanillaPsycastsExpandedPuppeteer
{
	private static AccessTools.FieldRef<IDictionary> canDoRandomBreaksCache;

	private static Func<Pawn, bool> isPuppet;

	private static AccessTools.FieldRef<MentalBreaker, Pawn> breakerPawn;

	public VanillaPsycastsExpandedPuppeteer(ModContentPack mod)
	{
		PatchingUtilities.PatchPushPopRand("VPEPuppeteer.Hediff_BrainLeech:ThrowFleck");
		canDoRandomBreaksCache = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField("VPEPuppeteer.MentalBreaker_CanDoRandomMentalBreaks_Patch:cachedResults"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(GameComponentUtility), "FinalizeInit"), null, new HarmonyMethod(typeof(VanillaPsycastsExpandedPuppeteer), "ClearCache"));
		Type type = AccessTools.TypeByName("VPEPuppeteer.Hediff_BrainLeech");
		AccessTools.FieldRef<object, int> progression = AccessTools.FieldRefAccess<int>(type, "lastTickCheck");
		AccessTools.FieldRef<object, int> fleck = AccessTools.FieldRefAccess<int>(type, "nextFleckSpawnTick");
		PatchingUtilities.RegisterTimestampFields(type, (object hediff, int offset) =>
		{
			progression(hediff) = progression(hediff) + offset;
			fleck(hediff) = fleck(hediff) + offset;
		});
		isPuppet = (Func<Pawn, bool>)AccessTools.DeclaredMethod(AccessTools.TypeByName("VPEPuppeteer.VPEPUtils"), "IsPuppet", new Type[1] { typeof(Pawn) }).CreateDelegate(typeof(Func<Pawn, bool>));
		breakerPawn = AccessTools.FieldRefAccess<MentalBreaker, Pawn>("pawn");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VPEPuppeteer.MentalBreaker_CanDoRandomMentalBreaks_Patch:Postfix"), new HarmonyMethod(typeof(VanillaPsycastsExpandedPuppeteer), "ReadCurrentPuppetState"));
	}

	private static bool ReadCurrentPuppetState(MentalBreaker __0, ref bool __1)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (__1 && isPuppet(breakerPawn(__0)))
		{
			__1 = false;
		}
		return false;
	}

	private static void ClearCache()
	{
		canDoRandomBreaksCache().Clear();
	}
}
