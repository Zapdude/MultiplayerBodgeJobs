using System;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimTalkReplyLedger(Game game) : GameComponent
{
	private const int Capacity = 8192;

	private List<string> ids = new List<string>();

	private HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	internal static bool ValidId(string id)
	{
		if (id != null && id.Length == 36 && Guid.TryParseExact(id, "D", out var result))
		{
			return result != Guid.Empty;
		}
		return false;
	}

	internal bool Contains(string id)
	{
		return seen.Contains(id);
	}

	internal bool Remember(string id)
	{
		if (!ValidId(id) || !seen.Add(id))
		{
			return false;
		}
		ids.Add(id);
		if (ids.Count > 8192)
		{
			seen.Remove(ids[0]);
			ids.RemoveAt(0);
		}
		return true;
	}

	public override void ExposeData()
	{
		if (!RimTalk.ReplayAvailable)
		{
			return;
		}
		Scribe_Collections.Look(ref ids, "mpRimTalkReplyIds", LookMode.Value);
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		if (ids == null)
		{
			ids = new List<string>();
		}
		seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> list = new List<string>(Math.Min(ids.Count, 8192));
		for (int i = Math.Max(0, ids.Count - 8192); i < ids.Count; i++)
		{
			if (ValidId(ids[i]) && seen.Add(ids[i]))
			{
				list.Add(ids[i]);
			}
		}
		ids = list;
	}
}
