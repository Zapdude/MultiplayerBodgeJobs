using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class SquishyMeleeCombatComponent(Game game) : GameComponent
{
	internal sealed class Guard : IExposable
	{
		internal Pawn Pawn;

		internal Thing Opponent;

		internal Map Map;

		internal int From;

		internal int To;

		internal bool Valid
		{
			get
			{
				if (Pawn != null && Opponent != null && Map != null && !Pawn.Destroyed && !Opponent.Destroyed && Pawn.Map == Map && Opponent.MapHeld == Map)
				{
					return To >= From;
				}
				return false;
			}
		}

		public void ExposeData()
		{
			Scribe_References.Look(ref Pawn, "pawn");
			Scribe_References.Look(ref Opponent, "opponent");
			Scribe_References.Look(ref Map, "map");
			Scribe_Values.Look(ref From, "from", 0);
			Scribe_Values.Look(ref To, "to", 0);
		}
	}

	internal SquishyMeleeCombatSettings Combat;

	private List<Guard> saved;

	private readonly Dictionary<int, Guard> guards = new Dictionary<int, Guard>();

	internal void Begin(Pawn pawn, Thing opponent, int from, int to)
	{
		Guard guard = new Guard
		{
			Pawn = pawn,
			Opponent = opponent,
			Map = pawn.Map,
			From = from,
			To = to
		};
		if (guard.Valid)
		{
			guards[pawn.thingIDNumber] = guard;
		}
	}

	internal bool TryGet(Pawn pawn, out Guard guard)
	{
		if (!guards.TryGetValue(pawn.thingIDNumber, out guard))
		{
			return false;
		}
		if (guard.Pawn == pawn)
		{
			return guard.Valid;
		}
		return false;
	}

	public override void GameComponentTick()
	{
		if (SquishyMelee.Installed && MP.IsInMultiplayer && guards.Count != 0 && Find.TickManager.TicksGame % 250 == 0)
		{
			Guard[] array = guards.Values.Where((Guard g) => !g.Valid || SquishyMelee.MapTick(g.Map) > g.To).ToArray();
			foreach (Guard guard in array)
			{
				guards.Remove(guard.Pawn.thingIDNumber);
			}
		}
	}

	public override void ExposeData()
	{
		if (!SquishyMelee.Installed)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			if (MP.IsInMultiplayer && MP.IsHosting && Combat == null)
			{
				Combat = SquishyMelee.CaptureSettings();
			}
			saved = (from g in guards.Values
				where g.Valid && SquishyMelee.MapTick(g.Map) <= g.To
				orderby g.Pawn.thingIDNumber
				select g).ToList();
		}
		Scribe_Deep.Look(ref Combat, "mpSquishyMeleeCombat");
		Scribe_Collections.Look(ref saved, "mpSquishyMeleeGuards", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			saved = null;
		}
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		guards.Clear();
		SquishyMeleeCombatSettings combat = Combat;
		if (combat == null || !combat.Valid)
		{
			Combat = null;
		}
		if (MP.IsInMultiplayer && saved != null)
		{
			foreach (Guard item in saved)
			{
				if (item != null && item.Valid)
				{
					guards[item.Pawn.thingIDNumber] = item;
				}
			}
		}
		saved = null;
	}
}
