using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

internal static class NiceTabSync
{
	internal delegate bool BeginMapRead(Map map, out long token);

	internal const int Version = 32;

	internal static BeginMapRead BeginRead;

	internal static Action<long> EndRead;

	internal static void InstallClock()
	{
		if (BeginRead == null)
		{
			Type type = AccessTools.TypeByName("Multiplayer.Client.Compatibility.OwnerClock") ?? throw new TypeLoadException("Multiplayer owner clock is required by Nice Plants Menu");
			BeginRead = (BeginMapRead)Required(type, "BeginMapRead").CreateDelegate(typeof(BeginMapRead));
			EndRead = (Action<long>)Required(type, "EndRead").CreateDelegate(typeof(Action<long>));
		}
	}

	internal static MethodInfo Required(Type type, string name, Type[] args = null)
	{
		return AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type?.FullName, name);
	}

	internal static void Patch(MethodInfo target, Type owner, string prefix, string finalizer = null)
	{
		MpCompat.harmony.Patch(target, (prefix == null) ? null : new HarmonyMethod(owner, prefix), null, null, (finalizer == null) ? null : new HarmonyMethod(owner, finalizer));
	}

	internal static bool Table(Building_WorkTable table)
	{
		if (table != null && !table.Destroyed && table.Spawned && table.Map != null && table.Faction == Faction.OfPlayer)
		{
			return table.billStack != null;
		}
		return false;
	}

	internal static bool Bill(Bill bill)
	{
		if (bill != null && !bill.DeletedOrDereferenced && bill.billStack?.billGiver is Building_WorkTable building_WorkTable && Table(building_WorkTable))
		{
			return building_WorkTable.billStack.Bills.Contains(bill);
		}
		return false;
	}

	internal static bool Patient(Pawn pawn)
	{
		if (pawn != null && !pawn.Destroyed && !pawn.Dead)
		{
			if (!pawn.Spawned && (pawn.MapHeld == null || !Find.Maps.Contains(pawn.MapHeld)))
			{
				Game game = Current.Game;
				if (game == null || game.World?.worldPawns?.Contains(pawn) != true)
				{
					goto IL_00a4;
				}
			}
			if (pawn.Faction != Faction.OfPlayer)
			{
				return pawn.HostFaction == Faction.OfPlayer;
			}
			return true;
		}
		goto IL_00a4;
		IL_00a4:
		return false;
	}
}
