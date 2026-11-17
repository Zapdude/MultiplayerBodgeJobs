namespace Multiplayer.Compat;

internal static class RimTalkExternalEffectPolicy
{
	internal static bool Allows(bool? coreCanExternal, bool? coreWorker, bool multiplayer, bool logicalHost)
	{
		if (coreCanExternal != false && coreWorker != true)
		{
			return !multiplayer | logicalHost;
		}
		return false;
	}
}
