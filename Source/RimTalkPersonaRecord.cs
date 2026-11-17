using System;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimTalkPersonaRecord : IExposable
{
	public int PawnId;

	public long Revision;

	public string Personality;

	public float Weight = 1f;

	public Dictionary<string, int> Spoken = new Dictionary<string, int>(StringComparer.Ordinal);

	public List<IExposable> Memories = new List<IExposable>();

	public void ExposeData()
	{
		Scribe_Values.Look(ref PawnId, "pawnId", 0);
		Scribe_Values.Look(ref Revision, "mpRevision", 0L);
		Scribe_Values.Look(ref Personality, "personality");
		Scribe_Values.Look(ref Weight, "weight", 1f);
		Scribe_Collections.Look(ref Spoken, "spoken", LookMode.Value, LookMode.Value);
		Scribe_Collections.Look(ref Memories, "memories", LookMode.Deep);
		if (Spoken == null)
		{
			Spoken = new Dictionary<string, int>(StringComparer.Ordinal);
		}
		if (Memories == null)
		{
			Memories = new List<IExposable>();
		}
	}
}
