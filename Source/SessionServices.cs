using System;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat;

internal static class SessionServices
{
	internal delegate void RegisterSettingsDelegate(string key, Type schema, Func<IExposable> capture, Func<IExposable> defaults, Action<IExposable> apply, Func<IExposable, bool> validate, Func<ModSettings> personal, string label);

	internal delegate bool BeginLocalOnlyDelegate(out long token);

	internal static RegisterSettingsDelegate RegisterSettings;

	internal static Func<string, Game, IExposable> SettingsForGame;

	internal static Action<IExposable> EnsureSettings;

	internal static Func<IExposable, IExposable, bool> ApplySettings;

	internal static Action<string, Action<int>, Action> RegisterLifecycle;

	internal static Func<int> ExecutingPlayer;

	internal static Func<int> LocalPlayer;

	internal static Func<IDisposable> EnterLocalOnly;

	internal static BeginLocalOnlyDelegate BeginLocalOnly;

	internal static Action<long> EndLocalOnly;

	internal static Func<IExposable> CreateOwnership;

	internal static Func<IExposable, int> GetOwner;

	internal static Func<IExposable, int> GetRevision;

	internal static Action<IExposable, int> SetOwner;

	internal static Action<IExposable, int> SetRevision;

	internal static Action<IExposable, int> Retire;

	internal static Func<IExposable, int, int, bool> Matches;

	internal static Func<IExposable, int, int, bool> CanChoose;

	internal static Func<IExposable, int, int, bool> TryClaim;

	private static bool installed;

	private static T Method<T>(string type, string method) where T : Delegate
	{
		return (T)(AccessTools.DeclaredMethod("Multiplayer.Client.Compatibility." + type + ":" + method) ?? throw new MissingMethodException("Update Multiplayer together with Compatibility: missing service " + type + "." + method)).CreateDelegate(typeof(T));
	}

	private static Func<int> Property(string name)
	{
		return (Func<int>)(AccessTools.PropertyGetter("Multiplayer.Client.Compatibility.SessionLifecycle:" + name) ?? throw new MissingMethodException("Missing Multiplayer lifecycle property " + name)).CreateDelegate(typeof(Func<int>));
	}

	internal static void Install()
	{
		if (!installed)
		{
			RegisterSettings = Method<RegisterSettingsDelegate>("RoomSettings", "Register");
			SettingsForGame = Method<Func<string, Game, IExposable>>("RoomSettings", "ForGame");
			EnsureSettings = Method<Action<IExposable>>("RoomSettings", "Ensure");
			ApplySettings = Method<Func<IExposable, IExposable, bool>>("RoomSettings", "Apply");
			RegisterLifecycle = Method<Action<string, Action<int>, Action>>("SessionLifecycle", "Register");
			ExecutingPlayer = Property("ExecutingPlayerId");
			LocalPlayer = Property("LocalPlayerId");
			EnterLocalOnly = Method<Func<IDisposable>>("Presentation", "EnterLocalOnly");
			BeginLocalOnly = Method<BeginLocalOnlyDelegate>("Presentation", "BeginLocalOnly");
			EndLocalOnly = Method<Action<long>>("Presentation", "EndLocalOnly");
			CreateOwnership = Method<Func<IExposable>>("DecisionOwnership", "Create");
			GetOwner = Method<Func<IExposable, int>>("DecisionOwnership", "GetOwner");
			GetRevision = Method<Func<IExposable, int>>("DecisionOwnership", "GetRevision");
			SetOwner = Method<Action<IExposable, int>>("DecisionOwnership", "SetOwner");
			SetRevision = Method<Action<IExposable, int>>("DecisionOwnership", "SetRevision");
			Retire = Method<Action<IExposable, int>>("DecisionOwnership", "Retire");
			Matches = Method<Func<IExposable, int, int, bool>>("DecisionOwnership", "Matches");
			CanChoose = Method<Func<IExposable, int, int, bool>>("DecisionOwnership", "CanChoose");
			TryClaim = Method<Func<IExposable, int, int, bool>>("DecisionOwnership", "TryClaim");
			installed = true;
		}
	}
}
