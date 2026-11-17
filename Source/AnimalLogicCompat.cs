using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Oblitus.AnimalsLogic")]
public class AnimalLogicCompat
{
	public AnimalLogicCompat(ModContentPack mod)
	{
		MpCompat.harmony.Patch(AccessTools.Method(typeof(PawnComponentsUtility), "CreateInitialComponents"), null, new HarmonyMethod(typeof(AnimalLogicCompat), "CreateInitialComponentsPostfix"));
	}

	private static void CreateInitialComponentsPostfix(Pawn pawn)
	{
		if (pawn.RaceProps.Animal && pawn.ownership == null)
		{
			pawn.ownership = new Pawn_Ownership(pawn);
		}
	}
}
