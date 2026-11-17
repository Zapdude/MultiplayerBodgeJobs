using Verse;

namespace Multiplayer.Compat;

public sealed class DeadMansSwitchIISettingsComponent : GameComponent
{
	internal bool ArtifuscleRots;

	public DeadMansSwitchIISettingsComponent(Game game)
	{
		ArtifuscleRots = DeadMansSwitchII.LocalArtifuscleRots;
	}

	public override void ExposeData()
	{
		if (DeadMansSwitchII.DmsSettings != null)
		{
			Scribe_Values.Look(ref ArtifuscleRots, "mpDmsArtifuscleRots", ArtifuscleRots, forceSave: true);
		}
	}
}
