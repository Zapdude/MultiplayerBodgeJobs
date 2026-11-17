using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("UnlimitedHugs.HugsLib")]
public class HugsLib
{
	public HugsLib(ModContentPack mod)
	{
		PatchingUtilities.CancelCallIfLongEventsAreUnsafe("HugsLib.HugsLibController:OnMapInitFinalized");
	}
}
