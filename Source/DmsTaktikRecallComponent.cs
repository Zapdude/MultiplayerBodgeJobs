using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsTaktikRecallComponent(Game game) : GameComponent
{
	internal List<DmsTaktikRecall> Pending = new List<DmsTaktikRecall>();

	private List<Pawn> queuedDowned = new List<Pawn>();

	private List<Corpse> queuedCorpses = new List<Corpse>();

	public override void ExposeData()
	{
		if (!DeadMansSwitchTaktikmagier.Installed)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			IList downed = DeadMansSwitchTaktikmagier.Downed;
			queuedDowned = ((downed != null) ? downed.Cast<Pawn>().ToList() : null) ?? new List<Pawn>();
			IList corpses = DeadMansSwitchTaktikmagier.Corpses;
			queuedCorpses = ((corpses != null) ? corpses.Cast<Corpse>().ToList() : null) ?? new List<Corpse>();
		}
		Scribe_Collections.Look(ref queuedDowned, "mpTaktikDowned", LookMode.Reference);
		Scribe_Collections.Look(ref queuedCorpses, "mpTaktikCorpses", LookMode.Reference);
		Scribe_Collections.Look(ref Pending, "mpTaktikRecall", LookMode.Deep);
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		IList downed2 = DeadMansSwitchTaktikmagier.Downed;
		IList corpses2 = DeadMansSwitchTaktikmagier.Corpses;
		if (downed2 != null)
		{
			downed2.Clear();
			foreach (Pawn item in queuedDowned ?? new List<Pawn>())
			{
				if (item != null)
				{
					downed2.Add(item);
				}
			}
		}
		if (corpses2 != null)
		{
			corpses2.Clear();
			foreach (Corpse item2 in queuedCorpses ?? new List<Corpse>())
			{
				if (item2 != null)
				{
					corpses2.Add(item2);
				}
			}
		}
		if (Pending == null)
		{
			Pending = new List<DmsTaktikRecall>();
		}
	}
}
