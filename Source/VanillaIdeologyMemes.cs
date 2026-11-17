using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VMemesE")]
internal class VanillaIdeologyMemes
{
	private static Type disableFirstDefeatThoughtType;

	public VanillaIdeologyMemes(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRand(new string[2] { "VanillaMemesExpanded.CompAbilityHarvestBodyParts:Apply", "VanillaMemesExpanded.RitualOutcomeEffectWorker_ViolentConversion:Apply" }, patchPushPop: false);
		Type type = AccessTools.TypeByName("VanillaMemesExpanded.CompDeconstructJunk");
		MP.RegisterSyncMethod(type, "SetObjectForDeconstruction");
		MP.RegisterSyncMethod(type, "CancelObjectForDeconstruction");
		disableFirstDefeatThoughtType = AccessTools.TypeByName("VanillaMemesExpanded.Thought_DisableFirstDefeatThought");
		if (disableFirstDefeatThoughtType != null)
		{
			PatchingUtilities.PatchTryGainMemory(TryGainDisableFirstDefeatThought);
		}
		else
		{
			Log.Error("Trying to patch `VanillaMemesExpanded.Thought_DisableFirstDefeatThought`, but the type is null. Did it get moved, renamed, or removed?");
		}
		PatchingUtilities.PatchCancelInInterface("VanillaMemesExpanded.VanillaMemesExpanded_SettlementAbandonUtility_Abandon_Patch:SetAbandonedTimeToZero");
	}

	private static bool TryGainDisableFirstDefeatThought(Thought_Memory thought)
	{
		if (!disableFirstDefeatThoughtType.IsInstanceOfType(thought))
		{
			return false;
		}
		thought.MoodOffset();
		return true;
	}
}
