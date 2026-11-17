using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Owlchemist.Tacticowl")]
public class Tacticowl
{
	public Tacticowl(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate("Tacticowl.Patch_GetGizmos", "CreateGizmo_SearchAndDestroy_Melee", 1);
		MpCompat.RegisterLambdaDelegate("Tacticowl.Patch_GetGizmos", "CreateGizmo_SearchAndDestroy_Ranged", 1);
		MpCompat.RegisterLambdaDelegate("Tacticowl.Patch_PawnGetGizmos", "Postfix", 1);
		PatchingUtilities.PatchSystemRand("Tacticowl.Patch_TryStartMentalState:Postfix", patchPushPop: false);
	}
}
