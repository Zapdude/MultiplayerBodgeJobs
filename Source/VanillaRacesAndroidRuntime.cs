using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

internal static class VanillaRacesAndroidRuntime
{
	private static bool installed;

	private static Func<Pawn, bool> emotionless;

	private static Func<Pawn, GeneDef, bool> hasActiveGene;

	private static AccessTools.FieldRef<GeneDef> mentalBreaksDisabled;

	private static AccessTools.FieldRef<MentalBreaker, Pawn> breakerPawn;

	internal static void Install()
	{
		if (!installed)
		{
			Type type = AccessTools.TypeByName("VREAndroids.Utils");
			emotionless = (Func<Pawn, bool>)AccessTools.DeclaredMethod(type, "Emotionless", new Type[1] { typeof(Pawn) }).CreateDelegate(typeof(Func<Pawn, bool>));
			hasActiveGene = (Func<Pawn, GeneDef, bool>)AccessTools.DeclaredMethod(type, "HasActiveGene", new Type[2]
			{
				typeof(Pawn),
				typeof(GeneDef)
			}).CreateDelegate(typeof(Func<Pawn, GeneDef, bool>));
			mentalBreaksDisabled = AccessTools.StaticFieldRefAccess<GeneDef>(AccessTools.DeclaredField("VREAndroids.VREA_DefOf:VREA_MentalBreaksDisabled"));
			breakerPawn = AccessTools.FieldRefAccess<MentalBreaker, Pawn>("pawn");
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VREAndroids.SocialInteractionUtility_CanInitiateRandomInteraction_Patch:Postfix"), new HarmonyMethod(typeof(VanillaRacesAndroidRuntime), "ReadInteraction"));
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VREAndroids.MentalBreaker_CanDoRandomMentalBreaks_Patch:Postfix"), new HarmonyMethod(typeof(VanillaRacesAndroidRuntime), "ReadMentalBreaks"));
			Type type2 = AccessTools.TypeByName("VREAndroids.Gene_MemoryDecay");
			AccessTools.FieldRef<object, int> lastDecay = AccessTools.FieldRefAccess<int>(type2, "lastTickDecayCheck");
			PatchingUtilities.RegisterTimestampFields(type2, (object gene, int offset) =>
			{
				lastDecay(gene) = lastDecay(gene) + offset;
			});
			installed = true;
		}
	}

	private static bool ReadInteraction(Pawn __0, ref bool __1)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (__1 && emotionless(__0))
		{
			__1 = false;
		}
		return false;
	}

	private static bool ReadMentalBreaks(MentalBreaker __0, ref bool __1)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (__1 && hasActiveGene(breakerPawn(__0), mentalBreaksDisabled()))
		{
			__1 = false;
		}
		return false;
	}
}
