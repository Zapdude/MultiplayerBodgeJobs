using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;

namespace Multiplayer.Compat;

internal sealed class RimDarkGenesWorldScope : IDisposable
{
	private static readonly FieldInfo Game = AccessTools.Field("Multiplayer.Client.Multiplayer:game");

	private static readonly FieldInfo World = AccessTools.Field("Multiplayer.Client.MultiplayerGame:asyncWorldTimeComp");

	private static readonly MethodInfo Pre = AccessTools.Method("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp:PreContextForCallback");

	private static readonly MethodInfo Post = AccessTools.Method("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp:PostContext");

	private readonly object owner;

	internal static object Owner
	{
		get
		{
			object value = Game.GetValue(null);
			if (value == null)
			{
				return null;
			}
			return World.GetValue(value);
		}
	}

	internal RimDarkGenesWorldScope()
	{
		if (MP.IsInMultiplayer)
		{
			owner = Owner ?? throw new InvalidOperationException("Missing Genes world clock");
			Pre.Invoke(owner, null);
		}
	}

	public void Dispose()
	{
		if (owner != null)
		{
			Post.Invoke(owner, null);
		}
	}
}
