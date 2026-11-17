using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Albion.SparklingWorlds.Full")]
[MpCompatFor("Albion.SparklingWorlds.Core")]
public class SparklingWorlds
{
	public SparklingWorlds(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			MP.RegisterSyncMethod(AccessTools.TypeByName("SparklingWorlds.CompOrbitalLaunchSW"), "TryLaunch");
		});
	}
}
