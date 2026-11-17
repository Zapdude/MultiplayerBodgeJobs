using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public static class CommandAuthorityBridge
{
	private sealed class EmptyScope : IDisposable
	{
		public static readonly EmptyScope Instance = new EmptyScope();

		public void Dispose()
		{
		}
	}

	private static Func<Faction, bool> faction;

	private static Func<Pawn, bool> pawn;

	private static Func<Faction, IDisposable> enterFaction;

	public static void Install()
	{
		if (faction == null)
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.Compatibility.CommandAuthority") ?? throw new MissingMemberException("Update Multiplayer together with Compatibility: CommandAuthority is missing");
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type, "CanActForFaction", new Type[1] { typeof(Faction) }) ?? throw new MissingMethodException(type.FullName, "CanActForFaction");
			MethodInfo methodInfo2 = AccessTools.DeclaredMethod(type, "CanActForPawn", new Type[1] { typeof(Pawn) }) ?? throw new MissingMethodException(type.FullName, "CanActForPawn");
			Func<Faction, bool> func = (Func<Faction, bool>)methodInfo.CreateDelegate(typeof(Func<Faction, bool>));
			Func<Pawn, bool> func2 = (Func<Pawn, bool>)methodInfo2.CreateDelegate(typeof(Func<Pawn, bool>));
			Type type2 = AccessTools.TypeByName("Multiplayer.Client.Util.MpScope") ?? throw new MissingMemberException("Multiplayer faction scope is missing");
			MethodInfo methodInfo3 = AccessTools.DeclaredMethod(type2, "PushFaction", new Type[1] { typeof(Faction) }) ?? throw new MissingMethodException(type2.FullName, "PushFaction");
			if (!typeof(IDisposable).IsAssignableFrom(methodInfo3.ReturnType))
			{
				throw new MissingMethodException("Multiplayer faction scope must implement IDisposable");
			}
			enterFaction = (Func<Faction, IDisposable>)methodInfo3.CreateDelegate(typeof(Func<Faction, IDisposable>));
			pawn = func2;
			faction = func;
		}
	}

	public static bool CanActForFaction(Faction owner)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Install();
		return faction(owner);
	}

	public static bool CanActForPawn(Pawn actor)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		Install();
		return pawn(actor);
	}

	public static IDisposable EnterFaction(Faction owner)
	{
		if (!MP.IsInMultiplayer)
		{
			return EmptyScope.Instance;
		}
		Install();
		return enterFaction(owner);
	}
}
