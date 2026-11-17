using Verse;

namespace Multiplayer.Compat;

public sealed class AlphaGenesToolSelection : IExposable
{
	internal Pawn Worker;

	internal Pawn Target;

	internal Thing Tool;

	internal int Job;

	internal int Kind;

	private IExposable ownership;

	internal IExposable Ownership => ownership ?? (ownership = SessionServices.CreateOwnership());

	internal int Owner
	{
		get
		{
			return SessionServices.GetOwner(Ownership);
		}
		set
		{
			SessionServices.SetOwner(Ownership, value);
		}
	}

	internal int Revision
	{
		get
		{
			return SessionServices.GetRevision(Ownership);
		}
		set
		{
			SessionServices.SetRevision(Ownership, value);
		}
	}

	public void ExposeData()
	{
		Scribe_References.Look(ref Worker, "worker");
		Scribe_References.Look(ref Target, "target");
		Scribe_References.Look(ref Tool, "tool");
		Scribe_Values.Look(ref Job, "job", -1);
		Scribe_Values.Look(ref Kind, "kind", 0);
		Ownership.ExposeData();
	}
}
