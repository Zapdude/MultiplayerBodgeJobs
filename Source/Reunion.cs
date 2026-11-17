using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Kyrun.Reunion")]
public class Reunion
{
	public Reunion(ModContentPack mod)
	{
		PatchingUtilities.PatchUnityRand(new string[3] { "Kyrun.Reunion.GameComponent:GetRandomAllyForSpawning", "Kyrun.Reunion.GameComponent:TryScheduleNextEvent", "Kyrun.Reunion.GameComponent:DecideAndDoEvent" });
	}
}
