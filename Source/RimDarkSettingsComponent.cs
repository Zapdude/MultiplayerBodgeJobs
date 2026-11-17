using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimDarkSettingsComponent : GameComponent
{
	private string core;

	private string genes;

	public RimDarkSettingsComponent(Game game)
	{
	}

	public override void ExposeData()
	{
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			core = RimDark.SettingsSnapshot(genes: false);
			genes = RimDark.SettingsSnapshot(genes: true);
		}
		Scribe_Values.Look(ref core, "mpRimDarkCoreSettings");
		Scribe_Values.Look(ref genes, "mpRimDarkGeneSettings");
		if (Scribe.mode == LoadSaveMode.LoadingVars && MP.IsInMultiplayer)
		{
			RimDark.ApplySettings(genes: false, core);
			RimDark.ApplySettings(genes: true, genes);
		}
	}
}
