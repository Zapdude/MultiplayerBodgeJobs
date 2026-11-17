using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sakurata.ideotickopt")]
public sealed class IdeologyAsyncClock
{
	public IdeologyAsyncClock(ModContentPack mod)
	{
		OtherModsAsyncTime.InstallIdeologyClock();
	}
}
