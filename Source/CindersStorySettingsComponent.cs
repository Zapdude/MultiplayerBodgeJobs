using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

public sealed class CindersStorySettingsComponent : GameComponent
{
	internal int MinDays;

	internal int MaxDays;

	public CindersStorySettingsComponent(Game game)
	{
		MinDays = CindersOfTheEmbergarden.LocalCalmMin;
		MaxDays = CindersOfTheEmbergarden.LocalCalmMax;
	}

	public override void ExposeData()
	{
		if (!(AccessTools.TypeByName("Embergarden.Storytelling.CinderStorytellerMod") == null))
		{
			Scribe_Values.Look(ref MinDays, "mpCindersCalmMinDays", MinDays, forceSave: true);
			Scribe_Values.Look(ref MaxDays, "mpCindersCalmMaxDays", MaxDays, forceSave: true);
		}
	}
}
