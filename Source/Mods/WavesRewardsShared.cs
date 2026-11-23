using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Shared reflection and window-key helpers for Storyteller: Waves &amp; Rewards.</summary>
    internal static class WavesRewardsShared
    {
        internal const string Namespace = "WavesRewards.";

        private static readonly Dictionary<Window, int> windowKeys = new Dictionary<Window, int>();
        private static readonly Dictionary<Type, Func<object>> instanceGetters = new Dictionary<Type, Func<object>>();
        private static Game keysGame;
        private static int keyTick = -1;
        private static int keySeq;

        internal static Type Type(string name) => AccessTools.TypeByName(Namespace + name);

        internal static FieldInfo Field(Type type, string name) =>
            type == null ? null : AccessTools.Field(type, name);

        internal static PropertyInfo Property(Type type, string name) =>
            type == null ? null : AccessTools.Property(type, name);

        internal static MethodInfo Method(Type type, string name, params Type[] arguments)
        {
            if (type == null)
                return null;
            try
            {
                return arguments == null || arguments.Length == 0
                    ? AccessTools.Method(type, name)
                    : AccessTools.Method(type, name, arguments);
            }
            catch (AmbiguousMatchException)
            {
                return null;
            }
        }

        internal static object Instance(Type type)
        {
            if (type == null)
                return null;
            if (!instanceGetters.TryGetValue(type, out Func<object> getter))
            {
                PropertyInfo property = AccessTools.Property(type, "Instance");
                FieldInfo field = property == null ? AccessTools.Field(type, "Instance") : null;
                if (property != null && property.GetGetMethod(true) is { IsStatic: true })
                    getter = () => property.GetValue(null, null);
                else if (field != null && field.IsStatic)
                    getter = () => field.GetValue(null);
                else
                    getter = () => null;
                instanceGetters[type] = getter;
            }
            return getter();
        }

        /// <summary>True when the game changed; clears the window keys so old windows cannot match.</summary>
        internal static bool EnsureSameGame()
        {
            Game game = Current.Game;
            if (game == keysGame)
                return false;
            keysGame = game;
            windowKeys.Clear();
            keyTick = -1;
            keySeq = 0;
            return true;
        }

        /// <summary>
        /// Key a window by the tick it was created on plus its order within that tick. Windows created by
        /// the same synced command get the same key on every client.
        /// </summary>
        internal static int AssignKey(Window window)
        {
            if (window == null)
                return -1;
            if (windowKeys.TryGetValue(window, out int existing))
                return existing;

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick != keyTick)
            {
                keyTick = tick;
                keySeq = 0;
            }
            int key = unchecked(tick * 1000 + keySeq++);
            windowKeys[window] = key;
            return key;
        }

        internal static int KeyOf(Window window) =>
            window != null && windowKeys.TryGetValue(window, out int key) ? key : -1;

        internal static void CloseKeyed(int key)
        {
            if (key < 0 || Find.WindowStack == null)
                return;
            foreach (Window window in Find.WindowStack.Windows)
                if (KeyOf(window) == key)
                    window.Close(false);
        }
    }
}
