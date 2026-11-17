using System;
using System.Collections.Generic;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

public sealed class WorldDominationDecisionComponent(Game game) : GameComponent
{
	public List<WorldDominationConquestDecision> Conquests = new List<WorldDominationConquestDecision>();

	public List<WorldDominationDefenseDecision> Defenses = new List<WorldDominationDefenseDecision>();

	private int nextDefenseId = 1;

	private bool restoreWindows;

	internal void RememberDefense(WorldObject traveler, WorldObject outpost, bool skirmish)
	{
		if (!Defenses.Any((WorldDominationDefenseDecision d) => d.outpost == outpost && d.skirmish == skirmish))
		{
			Defenses.Add(new WorldDominationDefenseDecision
			{
				id = nextDefenseId++,
				traveler = traveler,
				outpost = outpost,
				skirmish = skirmish
			});
		}
	}

	internal void RememberConquest(object context)
	{
		int tile = (int)WorldDomination.NativeGet(context, "tile");
		if (!Conquests.Any((WorldDominationConquestDecision c) => c.tile == tile))
		{
			Conquests.Add(new WorldDominationConquestDecision
			{
				tile = tile,
				tier = Convert.ToInt32(WorldDomination.NativeGet(context, "tier")),
				name = (string)WorldDomination.NativeGet(context, "originalName"),
				ruinsId = (int)WorldDomination.NativeGet(context, "ruinsId"),
				caravanId = (int)WorldDomination.NativeGet(context, "conqueringCaravanId"),
				conqueredFaction = (Faction)WorldDomination.NativeGet(context, "conqueredFaction"),
				bought = (bool)WorldDomination.NativeGet(context, "fromSettlementBuy")
			});
		}
	}

	public override void ExposeData()
	{
		if (!WorldDomination.HasSettings)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			if (!MP.IsInMultiplayer)
			{
				Conquests.Clear();
				Defenses.Clear();
				nextDefenseId = 1;
			}
			WorldDomination.CaptureOpenDecisions(this);
		}
		Scribe_Collections.Look(ref Conquests, "mpWdConquests", LookMode.Deep);
		Scribe_Collections.Look(ref Defenses, "mpWdDefenses", LookMode.Deep);
		Scribe_Values.Look(ref nextDefenseId, "mpWdNextDefenseId", 1);
		if (Conquests == null)
		{
			Conquests = new List<WorldDominationConquestDecision>();
		}
		if (Defenses == null)
		{
			Defenses = new List<WorldDominationDefenseDecision>();
		}
		if (Scribe.mode == LoadSaveMode.PostLoadInit && !MP.IsInMultiplayer)
		{
			Conquests.Clear();
			Defenses.Clear();
		}
	}

	public override void FinalizeInit()
	{
		restoreWindows = true;
	}

	public override void GameComponentUpdate()
	{
		if (restoreWindows && MP.IsInMultiplayer && WorldDomination.HasSettings && Current.ProgramState == ProgramState.Playing && Find.WindowStack != null)
		{
			restoreWindows = false;
			WorldDomination.ReopenDecisions(this);
		}
	}
}
