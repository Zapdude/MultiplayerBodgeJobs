using System.Collections.Generic;
using Verse;

namespace Multiplayer.Compat;

public sealed class DmsLifterLandingLetter : ChoiceLetter
{
	public int recordId;

	public override bool CanDismissWithRightClick => false;

	public override IEnumerable<DiaOption> Choices
	{
		get
		{
			yield return Option_Postpone;
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref recordId, "mpDmsLifterRecord", 0);
	}

	public override void OpenLetter()
	{
		Game game = Current.Game;
		DmsLifterLandingState state = DeadMansSwitchII.LifterState;
		if (!DeadMansSwitchII.LifterCanPresent)
		{
			return;
		}
		DmsLifterLandingState dmsLifterLandingState = state;
		if (dmsLifterLandingState == null || !dmsLifterLandingState.pending.Any((DmsPendingLifterLanding r) => r.id == recordId))
		{
			return;
		}
		int id = recordId;
		Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(DeadMansSwitchII.LifterText("Claim"), () =>
		{
			if (Current.Game == game && DeadMansSwitchII.LifterCanPresent)
			{
				state.Reopen(id);
			}
		}));
	}
}
