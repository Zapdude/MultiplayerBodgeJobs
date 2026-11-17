using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal sealed class RimDarkMapScope : IDisposable
{
	private static readonly FieldInfo TickingMap = AccessTools.Field("Multiplayer.Client.AsyncTimeComp:tickingMap");

	private static readonly MethodInfo Getter = AccessTools.Method("Multiplayer.Client.Extensions:AsyncTime");

	private static readonly MethodInfo Pre = AccessTools.Method("Multiplayer.Client.AsyncTimeComp:PreContext");

	private static readonly MethodInfo Post = AccessTools.Method("Multiplayer.Client.AsyncTimeComp:PostContext");

	private readonly object owner;

	private readonly Map previous;

	private bool disposed;

	internal RimDarkMapScope(Map map)
	{
		if (MP.IsInMultiplayer && map != null)
		{
			previous = (Map)TickingMap.GetValue(null);
			owner = Getter.Invoke(null, new object[1] { map }) ?? throw new InvalidOperationException("Missing RimDark map owner");
			Pre.Invoke(owner, null);
			TickingMap.SetValue(null, map);
		}
	}

	public void Dispose()
	{
		if (disposed || owner == null)
		{
			return;
		}
		disposed = true;
		try
		{
			Post.Invoke(owner, null);
		}
		finally
		{
			TickingMap.SetValue(null, previous);
		}
	}
}
