using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimDarkGenesColorsComponent : GameComponent
{
	private string settings;

	private string custom;

	private string selected;

	public RimDarkGenesColorsComponent(Game game)
	{
	}

	public override void ExposeData()
	{
		if (AccessTools.TypeByName("Genes40k.Genes40kMod") == null)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			string[] array = RimDarkGenes.CaptureColors();
			if (array != null)
			{
				settings = array[0];
				custom = array[1];
				selected = array[2];
			}
		}
		Scribe_Values.Look(ref settings, "mpGenesDefaultColors");
		Scribe_Values.Look(ref custom, "mpGenesCustomPreset");
		Scribe_Values.Look(ref selected, "mpGenesSelectedPreset");
		if (Scribe.mode == LoadSaveMode.LoadingVars && MP.IsInMultiplayer && settings != null && custom != null && selected != null)
		{
			RimDarkGenes.ApplyColors(new string[3] { settings, custom, selected });
		}
	}
}
