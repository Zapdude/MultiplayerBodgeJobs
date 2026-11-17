using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VBrewE")]
internal class VanillaBrewingExpanded
{
	public VanillaBrewingExpanded(ModContentPack mod)
	{
		PatchingUtilities.PatchSystemRandCtor("VanillaBrewingExpanded.Hediff_ConsumedCocktail");
	}
}
