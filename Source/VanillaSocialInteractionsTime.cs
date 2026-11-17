using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class VanillaSocialInteractionsTime
{
	internal sealed class Cooldowns : IExposable
	{
		internal Dictionary<Pawn, int> Values = new Dictionary<Pawn, int>();

		internal bool Tagged;

		private List<Pawn> keys;

		private List<int> ticks;

		public void ExposeData()
		{
			Scribe_Collections.Look(ref Values, "ticksByPawn", LookMode.Reference, LookMode.Value, ref keys, ref ticks, logNullErrors: false);
			if (Scribe.mode == LoadSaveMode.PostLoadInit && Values == null)
			{
				Values = new Dictionary<Pawn, int>();
			}
		}
	}

	private const string SaveLabel = "mpCompatTraitCooldowns";

	private static readonly ConditionalWeakTable<GameComponent, Cooldowns> histories = new ConditionalWeakTable<GameComponent, Cooldowns>();

	private static AccessTools.FieldRef<Dictionary<Pawn, int>> checkedPawns;

	private static AccessTools.FieldRef<IDictionary> opinions;

	private static AccessTools.FieldRef<object, int> opinionTick;

	private static Type managerType;

	private static bool installed;

	internal static void Install()
	{
		if (installed)
		{
			return;
		}
		managerType = AccessTools.TypeByName("VanillaSocialInteractionsExpanded.SocialInteractionsManager") ?? throw new TypeLoadException("VanillaSocialInteractionsExpanded.SocialInteractionsManager");
		checkedPawns = AccessTools.StaticFieldRefAccess<Dictionary<Pawn, int>>(AccessTools.DeclaredField("VanillaSocialInteractionsExpanded.ImmunityRecord_ImmunityTickInterval_Patch:checkedPawns"));
		opinions = AccessTools.StaticFieldRefAccess<IDictionary>(AccessTools.DeclaredField("VanillaSocialInteractionsExpanded.VSIE_Utils:averageOpinionOfCache"));
		opinionTick = AccessTools.FieldRefAccess<int>(AccessTools.TypeByName("VanillaSocialInteractionsExpanded.FloatValueCache"), "updateTick");
		foreach (ConstructorInfo item in from c in AccessTools.GetDeclaredConstructors(managerType)
			where !c.IsStatic
			select c)
		{
			MpCompat.harmony.Patch(item, null, new HarmonyMethod(typeof(VanillaSocialInteractionsTime), "NewManager"));
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(managerType, "ExposeData"), null, new HarmonyMethod(typeof(VanillaSocialInteractionsTime), "ExposeHistory"));
		PatchingUtilities.RegisterTimestampFields(typeof(Pawn), RebasePawn);
		installed = true;
	}

	private static void NewManager(GameComponent __instance)
	{
		Cooldowns value = histories.GetValue(__instance, (GameComponent _) => new Cooldowns());
		if (MP.IsInMultiplayer)
		{
			checkedPawns() = value.Values;
		}
		else
		{
			value.Values = checkedPawns();
		}
	}

	private static void ExposeHistory(GameComponent __instance)
	{
		Cooldowns target = histories.GetValue(__instance, (GameComponent _) => new Cooldowns());
		bool flag = Scribe.mode == LoadSaveMode.LoadingVars && Scribe.loader.curXmlParent?["mpCompatTraitCooldowns"] != null;
		if (MP.IsInMultiplayer || target.Tagged || flag)
		{
			Scribe_Deep.Look(ref target, "mpCompatTraitCooldowns");
			if (target == null)
			{
				target = new Cooldowns();
			}
			target.Tagged = true;
			histories.Remove(__instance);
			histories.Add(__instance, target);
			Game game = Current.Game;
			if (game != null && game.components.Contains(__instance))
			{
				checkedPawns() = target.Values;
			}
		}
	}

	internal static bool BindCurrent()
	{
		if (!installed || Current.Game?.components == null)
		{
			return false;
		}
		GameComponent gameComponent = Current.Game.components.FirstOrDefault((GameComponent c) => managerType.IsInstanceOfType(c));
		if (gameComponent == null)
		{
			return false;
		}
		Cooldowns value = histories.GetValue(gameComponent, (GameComponent _) => new Cooldowns());
		if (!MP.IsInMultiplayer && !value.Tagged)
		{
			return false;
		}
		checkedPawns() = value.Values;
		return true;
	}

	private static void RebasePawn(object value, int offset)
	{
		if (MP.IsInMultiplayer)
		{
			Pawn key = (Pawn)value;
			Dictionary<Pawn, int> dictionary = checkedPawns();
			if (dictionary.TryGetValue(key, out var value2))
			{
				dictionary[key] = value2 + offset;
			}
			object obj = opinions()[key];
			if (obj != null)
			{
				opinionTick(obj) = opinionTick(obj) + offset;
			}
		}
	}
}
