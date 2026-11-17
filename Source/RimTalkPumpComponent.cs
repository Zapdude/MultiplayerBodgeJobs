using Verse;

namespace Multiplayer.Compat;

public sealed class RimTalkPumpComponent : GameComponent
{
	public RimTalkPumpComponent(Game game)
	{
	}

	public override void GameComponentUpdate()
	{
		RimTalk.PipelineUpdate();
	}
}
