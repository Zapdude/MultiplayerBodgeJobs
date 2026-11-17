using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class AscensionMultiplayerState(Game game) : GameComponent
{
	private List<AscensionDeliveryOwner> owners = new List<AscensionDeliveryOwner>();

	internal List<AscensionRepayment> PendingRepayments = new List<AscensionRepayment>();

	internal void SetRepayment(string id, int cycle, Map map)
	{
		AscensionRepayment ascensionRepayment = PendingRepayments.FirstOrDefault((AscensionRepayment p) => p.contractId == id);
		if (ascensionRepayment == null)
		{
			PendingRepayments.Add(new AscensionRepayment
			{
				contractId = id,
				cycle = cycle,
				map = map
			});
		}
		else
		{
			ascensionRepayment.cycle = cycle;
			ascensionRepayment.map = map;
		}
	}

	internal string Owner(int id)
	{
		return owners.FirstOrDefault((AscensionDeliveryOwner o) => o.id == id)?.owner;
	}

	internal void SetOwner(int id, string owner)
	{
		AscensionDeliveryOwner ascensionDeliveryOwner = owners.FirstOrDefault((AscensionDeliveryOwner o) => o.id == id);
		if (ascensionDeliveryOwner == null)
		{
			owners.Add(new AscensionDeliveryOwner
			{
				id = id,
				owner = owner
			});
		}
		else
		{
			ascensionDeliveryOwner.owner = owner;
		}
	}

	internal void RemoveOwner(int id)
	{
		owners.RemoveAll((AscensionDeliveryOwner o) => o.id == id);
	}

	public override void ExposeData()
	{
		if (AscensionMegacorp.DeliveryInstalled)
		{
			Scribe_Collections.Look(ref owners, "mpUsacDeliveryOwners", LookMode.Deep);
			Scribe_Collections.Look(ref PendingRepayments, "mpUsacRepayments", LookMode.Deep);
			if (owners == null)
			{
				owners = new List<AscensionDeliveryOwner>();
			}
			if (PendingRepayments == null)
			{
				PendingRepayments = new List<AscensionRepayment>();
			}
		}
	}

	public override void GameComponentUpdate()
	{
		if (AscensionMegacorp.DeliveryInstalled)
		{
			AscensionMegacorp.DebtDialogUpdate();
		}
	}
}
