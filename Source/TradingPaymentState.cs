using System.Collections.Generic;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class TradingPaymentState(Game game) : GameComponent
{
	public List<TradingPendingPayment> pending = new List<TradingPendingPayment>();

	public int nextId = 1;

	public int nextContractId = 1;

	public List<TradingContractIdentity> contracts = new List<TradingContractIdentity>();

	internal readonly Dictionary<object, int> liveContracts = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

	internal readonly Dictionary<object, string> contractOwners = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);

	internal bool identitiesBound;

	private bool restoreWindows;

	private bool claimUnowned;

	public override void ExposeData()
	{
		if (Scribe.mode == LoadSaveMode.Saving && MP.IsInMultiplayer)
		{
			VanillaTradingExpanded.RefreshContractIdentities(this);
		}
		Scribe_Values.Look(ref nextId, "vteNextPaymentId", 1);
		Scribe_Values.Look(ref nextContractId, "vteNextContractId", 1);
		Scribe_Collections.Look(ref contracts, "vteContractIdentities", LookMode.Deep);
		Scribe_Collections.Look(ref pending, "vtePendingPayments", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (pending == null)
			{
				pending = new List<TradingPendingPayment>();
			}
			if (contracts == null)
			{
				contracts = new List<TradingContractIdentity>();
			}
			identitiesBound = false;
			restoreWindows = true;
		}
	}

	public override void FinalizeInit()
	{
		if (MP.IsInMultiplayer)
		{
			VanillaTradingExpanded.RefreshContractIdentities(this);
		}
		restoreWindows = true;
	}

	public void RequestPresentation()
	{
		restoreWindows = true;
	}

	public void RequestUnownedPayments()
	{
		claimUnowned = true;
	}

	public override void GameComponentUpdate()
	{
		if (MP.IsInMultiplayer && Current.ProgramState == ProgramState.Playing && Find.WindowStack != null)
		{
			if (claimUnowned && MP.InInterface && !MP.IsExecutingSyncCommand)
			{
				claimUnowned = false;
				VanillaTradingExpanded.ClaimUnownedPayments(this);
			}
			if (restoreWindows)
			{
				restoreWindows = false;
				VanillaTradingExpanded.ReopenPayments(this);
			}
		}
	}
}
