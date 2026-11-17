using Verse;

namespace Multiplayer.Compat;

public sealed class AscensionDeliveryOwner : IExposable
{
	public int id;

	public string owner;

	public void ExposeData()
	{
		Scribe_Values.Look(ref id, "id", -1);
		Scribe_Values.Look(ref owner, "owner");
	}
}
