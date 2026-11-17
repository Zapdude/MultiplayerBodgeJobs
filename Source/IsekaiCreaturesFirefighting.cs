using Verse;

namespace Multiplayer.Compat;

internal static class IsekaiCreaturesFirefighting
{
	internal static void Install()
	{
		LongEventHandler.ExecuteWhenFinished(ApplyRaceCapabilities);
	}

	private static void ApplyRaceCapabilities()
	{
		string[] array = new string[3] { "IsekaiImp_Lesser", "IsekaiImp_Common", "IsekaiImp_Greater" };
		for (int i = 0; i < array.Length; i++)
		{
			RaceProperties raceProperties = DefDatabase<ThingDef>.GetNamedSilentFail(array[i])?.race;
			if (raceProperties != null)
			{
				raceProperties.giveNonToolUserBeatFireVerb = true;
			}
		}
	}
}
