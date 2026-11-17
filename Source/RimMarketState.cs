using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimMarketState : GameComponent
{
	private readonly Game game;

	private List<RimMarketAccount> accounts = new List<RimMarketAccount>();

	internal Dictionary<int, Map> innMaps = new Dictionary<int, Map>();

	internal Dictionary<int, int> jobPlayers = new Dictionary<int, int>();

	internal Dictionary<int, int> caravanPlayers = new Dictionary<int, int>();

	internal Dictionary<string, Faction> recordOwners = new Dictionary<string, Faction>();

	internal Dictionary<string, Map> recordMaps = new Dictionary<string, Map>();

	internal Dictionary<int, int> orderArrivalTicks = new Dictionary<int, int>();

	private List<int> innMapKeys;

	private List<Map> innMapValues;

	private List<string> recordOwnerKeys;

	private List<string> recordMapKeys;

	private List<Faction> recordOwnerValues;

	private List<Map> recordMapValues;

	private int schema = 1;

	internal RimMarketAccount Primary => accounts.FirstOrDefault((RimMarketAccount a) => a.primary);

	public RimMarketState(Game game)
	{
		this.game = game;
	}

	internal static Faction PrimaryOwner(Faction proposed)
	{
		return Find.FactionManager?.AllFactionsListForReading.Where((Faction f) => f.IsPlayer).OrderBy((Faction f) => f.loadID).FirstOrDefault() ?? proposed;
	}

	internal void EnsurePrimary(Faction proposed, object manager)
	{
		if (Primary == null)
		{
			RimMarketAccount rimMarketAccount = new RimMarketAccount(PrimaryOwner(proposed), primary: true);
			rimMarketAccount.Capture(manager);
			accounts.Add(rimMarketAccount);
		}
	}

	internal RimMarketAccount Account(Faction owner)
	{
		return Account(owner, !MP.InInterface || MP.IsExecutingSyncCommand);
	}

	internal RimMarketAccount Account(Faction owner, bool create)
	{
		RimMarketAccount rimMarketAccount = accounts.FirstOrDefault((RimMarketAccount a) => a.owner == owner);
		if (rimMarketAccount != null)
		{
			return rimMarketAccount;
		}
		rimMarketAccount = new RimMarketAccount(owner, primary: false);
		if (create)
		{
			accounts.Add(rimMarketAccount);
		}
		return rimMarketAccount;
	}

	internal void EnsureAccounts(object manager)
	{
		EnsurePrimary(null, manager);
		foreach (Faction item in from f in Find.FactionManager.AllFactionsListForReading
			where f.IsPlayer
			orderby f.loadID
			select f)
		{
			Account(item);
		}
	}

	public override void FinalizeInit()
	{
		if (RimMarket.Installed && MP.IsInMultiplayer)
		{
			object manager = RimMarket.Manager;
			if (manager != null)
			{
				EnsureAccounts(manager);
			}
		}
	}

	public override void ExposeData()
	{
		if (!RimMarket.Installed)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			string[] array = (from p in recordMaps
				where p.Value == null || !Find.Maps.Contains(p.Value)
				select p.Key).ToArray();
			foreach (string key in array)
			{
				recordMaps.Remove(key);
			}
			int[] array2 = (from p in innMaps
				where p.Value == null || !Find.Maps.Contains(p.Value)
				select p.Key).ToArray();
			foreach (int key2 in array2)
			{
				innMaps.Remove(key2);
			}
		}
		Scribe_Values.Look(ref schema, "schema", 1);
		Scribe_Collections.Look(ref accounts, "factionAccounts", LookMode.Deep);
		Scribe_Collections.Look(ref innMaps, "innMaps", LookMode.Value, LookMode.Reference, ref innMapKeys, ref innMapValues);
		Scribe_Collections.Look(ref jobPlayers, "wantedJobPlayers", LookMode.Value, LookMode.Value);
		Scribe_Collections.Look(ref caravanPlayers, "caravanPlayers", LookMode.Value, LookMode.Value);
		Scribe_Collections.Look(ref recordOwners, "recordOwners", LookMode.Value, LookMode.Reference, ref recordOwnerKeys, ref recordOwnerValues);
		Scribe_Collections.Look(ref recordMaps, "recordMaps", LookMode.Value, LookMode.Reference, ref recordMapKeys, ref recordMapValues);
		Scribe_Collections.Look(ref orderArrivalTicks, "orderArrivalTicks", LookMode.Value, LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (accounts == null)
			{
				accounts = new List<RimMarketAccount>();
			}
			if (innMaps == null)
			{
				innMaps = new Dictionary<int, Map>();
			}
			if (jobPlayers == null)
			{
				jobPlayers = new Dictionary<int, int>();
			}
			if (caravanPlayers == null)
			{
				caravanPlayers = new Dictionary<int, int>();
			}
			if (recordOwners == null)
			{
				recordOwners = new Dictionary<string, Faction>();
			}
			if (orderArrivalTicks == null)
			{
				orderArrivalTicks = new Dictionary<int, int>();
			}
			if (recordMaps == null)
			{
				recordMaps = new Dictionary<string, Map>();
			}
			accounts.RemoveAll((RimMarketAccount a) => a?.owner == null);
		}
	}
}
