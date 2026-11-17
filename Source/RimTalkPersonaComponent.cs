using System.Collections.Generic;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimTalkPersonaComponent : GameComponent
{
	private readonly RimTalkPersonaStore store;

	private List<RimTalkPersonaRecord> snapshot = new List<RimTalkPersonaRecord>();

	public RimTalkPersonaComponent(Game game)
	{
		store = RimTalk.StateStoreFor(game);
	}

	public override void ExposeData()
	{
		if (!RimTalk.StateAvailable)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			if (!MP.IsInMultiplayer)
			{
				snapshot = store.Capture();
			}
			else
			{
				if (RimTalk.MetadataInitialHostBaseline)
				{
					store.CommitInitialBaseline();
				}
				snapshot = store.CaptureCommitted();
			}
		}
		Scribe_Collections.Look(ref snapshot, "mpRimTalkAiPersonas", LookMode.Deep);
		if (snapshot == null)
		{
			snapshot = new List<RimTalkPersonaRecord>();
		}
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			store.Restore(snapshot);
		}
	}
}
