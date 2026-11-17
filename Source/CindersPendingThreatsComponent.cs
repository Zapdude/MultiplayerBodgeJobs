using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class CindersPendingThreatsComponent(Game game) : GameComponent
{
	internal int NextSequence;

	internal int NextBatch = 1;

	internal int WorldBatch = -1;

	internal int BattleEpoch;

	internal List<CindersPendingThreat> Pending = new List<CindersPendingThreat>();

	internal List<int> TimedOutPhases = new List<int>();

	public override void GameComponentTick()
	{
		if (MP.IsInMultiplayer)
		{
			Pending.RemoveAll((CindersPendingThreat p) => p.Map == null || p.Map.Disposed || !Find.Maps.Contains(p.Map));
		}
	}

	public override void ExposeData()
	{
		if (AccessTools.TypeByName("Embergarden.Storytelling.EmberCycleGame") == null)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			Pending.RemoveAll((CindersPendingThreat p) => p.Map == null || p.Map.Disposed || !Find.Maps.Contains(p.Map));
		}
		Scribe_Values.Look(ref NextSequence, "mpCindersNextThreatSequence", 0);
		Scribe_Values.Look(ref NextBatch, "mpCindersNextThreatBatch", 1);
		Scribe_Values.Look(ref WorldBatch, "mpCindersWorldThreatBatch", -1);
		Scribe_Values.Look(ref BattleEpoch, "mpCindersBattleEpoch", 0);
		Scribe_Collections.Look(ref Pending, "mpCindersPendingThreats", LookMode.Deep);
		Scribe_Collections.Look(ref TimedOutPhases, "mpCindersTimedOutPhases", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (Pending == null)
			{
				Pending = new List<CindersPendingThreat>();
			}
			if (TimedOutPhases == null)
			{
				TimedOutPhases = new List<int>();
			}
		}
	}
}
