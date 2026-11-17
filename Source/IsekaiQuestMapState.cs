using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class IsekaiQuestMapState(Game game) : GameComponent
{
	private Dictionary<int, int> lastMaps = new Dictionary<int, int>();

	private Dictionary<int, long> revisions = new Dictionary<int, long>();

	internal int LastMap(int faction)
	{
		if (!lastMaps.TryGetValue(faction, out var value))
		{
			return -1;
		}
		return value;
	}

	internal long Revision(int faction)
	{
		if (!revisions.TryGetValue(faction, out var value))
		{
			return 0L;
		}
		return value;
	}

	internal void Commit(int faction, int map, long expectedRevision)
	{
		if (Revision(faction) == expectedRevision)
		{
			lastMaps[faction] = map;
			revisions[faction] = expectedRevision + 1;
		}
	}

	public override void ExposeData()
	{
		Scribe_Collections.Look(ref lastMaps, "mpIsekaiQuestLastHomeMaps", LookMode.Value, LookMode.Value);
		Scribe_Collections.Look(ref revisions, "mpIsekaiQuestMapRevisions", LookMode.Value, LookMode.Value);
		if (lastMaps == null)
		{
			lastMaps = new Dictionary<int, int>();
		}
		if (revisions == null)
		{
			revisions = new Dictionary<int, long>();
		}
	}
}
