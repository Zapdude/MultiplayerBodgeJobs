using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public sealed class VanillaFactionsInsectoid2SettingsComponent : GameComponent
{
	private VanillaFactionsInsectoid2.InsectoidSettingsSnapshot snapshot;

	private bool sessionInitialized;

	internal bool WorldBossClock;

	public VanillaFactionsInsectoid2SettingsComponent(Game game)
	{
	}

	public override void ExposeData()
	{
		if (VanillaFactionsInsectoid2.SettingsInstalled)
		{
			Scribe_Values.Look(ref WorldBossClock, "mpVfeInsectoids2WorldBossClock", defaultValue: false);
			if (Scribe.mode == LoadSaveMode.Saving && MP.IsInMultiplayer)
			{
				Restore();
				snapshot = VanillaFactionsInsectoid2.CaptureSettings();
			}
			Scribe_Deep.Look(ref snapshot, "mpVfeInsectoids2Settings");
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				sessionInitialized = false;
				Restore();
			}
		}
	}

	public override void StartedNewGame()
	{
		Restore();
	}

	public override void LoadedGame()
	{
		Restore();
	}

	private void Restore()
	{
		if (VanillaFactionsInsectoid2.SettingsInstalled && MP.IsInMultiplayer && !sessionInitialized)
		{
			VanillaFactionsInsectoid2.RestoreSessionSettings(snapshot);
			sessionInitialized = true;
		}
	}

	internal void RememberSessionBeforeExit()
	{
		if (VanillaFactionsInsectoid2.SettingsInstalled)
		{
			snapshot = VanillaFactionsInsectoid2.CaptureSettings();
			sessionInitialized = false;
		}
	}
}
