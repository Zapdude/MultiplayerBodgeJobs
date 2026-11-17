using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaracesexpanded.hussar")]
public class VanillaRacesHussar
{
	public VanillaRacesHussar(ModContentPack mod)
	{
		PatchingUtilities.PatchCancelInInterface("VREHussars.Pawn_DraftController_Drafted_Patch:Postfix");
	}
}
