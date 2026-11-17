using Multiplayer.API;

namespace Multiplayer.Compat;

internal static class VanillaSeriesRegistration17
{
	internal static ISyncMethod[] Append(ISyncMethod[] handlers)
	{
		for (int i = 0; i < handlers.Length; i++)
		{
			handlers[i].SetVersion(17);
		}
		return handlers;
	}

	internal static ISyncDelegate[] Append(ISyncDelegate[] handlers)
	{
		for (int i = 0; i < handlers.Length; i++)
		{
			((ISyncMethod)handlers[i]).SetVersion(17);
		}
		return handlers;
	}
}
