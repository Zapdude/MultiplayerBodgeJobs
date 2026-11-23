using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Gore Perfected (tiepi.goreperfected) in multiplayer.</summary>
    /// <remarks>
    /// Several effects are gated on the map the player is looking at (Find.CurrentMap) and, in
    /// "potato mode", on the camera view. Some of those checks sit in the simulation: oil drops,
    /// burn embers, splash clouds and drips all draw the game's random numbers. Each player looks at
    /// a different map or a different part of it, so one PC produced the effect and took the random
    /// numbers while the other did not.
    ///
    /// Inside the synced simulation the checks now answer as if every player looked at the map
    /// being simulated, the whole of it, so every PC makes the same effect. Purely visual effects
    /// (casings, debris, ricochets, footprints) keep their per-player look but run on a temporary
    /// random state that is thrown away, so they no longer consume the game's numbers. Behaviour
    /// reference: Multiplayer Fix (MIT); this is a reimplementation.
    /// </remarks>
    [MpCompatFor("tiepi.goreperfected")]
    class GorePerfected
    {
        private static readonly CellRect Everything = new CellRect(-1000000, -1000000, 2000000, 2000000);

        private static readonly HashSet<string> SkipTypes = new HashSet<string>
        {
            "GP_BattleTools", "GP_DebugTools", "CombatFxPatches", "FootprintPatch"
        };

        private static readonly HashSet<string> SkipMethods = new HashSet<string>
        {
            "GP_Effects.OnPartDestroyed", "Footprints.Step"
        };

        private static readonly string[] IsolatedMethods =
        {
            "CombatFxPatches.TryCastShot_Postfix",
            "CombatFxPatches.DropFilthDueToDamage_Postfix",
            "CombatFxPatches.Impact_Prefix",
            "Footprints.Step"
        };

        private static MethodInfo findCurrentMap;
        private static MethodInfo gameCurrentMap;
        private static MethodInfo cameraViewRect;
        private static Func<Map> mapContext;

        private static readonly List<string> transpiled = new List<string>();
        private static readonly List<string> isolated = new List<string>();

        public GorePerfected(ModContentPack mod)
        {
            LongEventHandler.ExecuteWhenFinished(InstallSafely);
        }

        private static void InstallSafely()
        {
            try
            {
                Install();
            }
            catch (Exception e)
            {
                Log.Error("[MPCompat] Gore Perfected: install failed; module disabled.\n" + e);
            }
        }

        private static void Install()
        {
            Type manager = AccessTools.TypeByName("GorePerfected.GoreChunkManager");
            if (manager == null)
            {
                Log.Message("[MPCompat] Gore Perfected: not loaded; skipped.");
                return;
            }

            findCurrentMap = AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap));
            gameCurrentMap = AccessTools.PropertyGetter(typeof(Game), nameof(Game.CurrentMap));
            cameraViewRect = AccessTools.PropertyGetter(typeof(CameraDriver), nameof(CameraDriver.CurrentViewRect));
            Type mp = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
            MethodInfo context = mp == null ? null : AccessTools.PropertyGetter(mp, "MapContext");
            if (context != null && context.IsStatic && context.ReturnType == typeof(Map))
                mapContext = (Func<Map>)Delegate.CreateDelegate(typeof(Func<Map>), context);

            if (findCurrentMap == null || cameraViewRect == null || mapContext == null)
            {
                Log.Error("[MPCompat] Gore Perfected: Multiplayer.MapContext or the game's view getters"
                    + " are missing; module disabled.");
                return;
            }

            transpiled.Clear();
            isolated.Clear();
            HarmonyMethod transpiler = new HarmonyMethod(typeof(GorePerfected), nameof(ViewTranspiler));

            foreach (MethodBase method in FindViewChecks(manager.Assembly))
            {
                try
                {
                    MpCompat.harmony.Patch(method, transpiler: transpiler);
                    transpiled.Add(Describe(method));
                }
                catch (Exception e)
                {
                    Log.Warning("[MPCompat] Gore Perfected: could not patch " + Describe(method) + ": " + e.Message);
                }
            }

            HarmonyMethod prefix = new HarmonyMethod(typeof(GorePerfected), nameof(IsolatePrefix))
            {
                priority = Priority.First
            };
            HarmonyMethod finalizer = new HarmonyMethod(typeof(GorePerfected), nameof(IsolateFinalizer))
            {
                priority = Priority.Last
            };

            foreach (string entry in IsolatedMethods)
            {
                int dot = entry.IndexOf('.');
                Type type = AccessTools.TypeByName("GorePerfected." + entry.Substring(0, dot));
                MethodInfo method = type == null ? null : AccessTools.Method(type, entry.Substring(dot + 1));
                if (method == null)
                {
                    Log.Warning("[MPCompat] Gore Perfected: " + entry + " not found; its random state is not isolated.");
                    continue;
                }
                MpCompat.harmony.Patch(method, prefix: prefix, finalizer: finalizer);
                isolated.Add(entry);
            }

            Log.Message("[MPCompat] Gore Perfected: " + transpiled.Count + " simulation view checks made"
                + " deterministic, " + isolated.Count + " visual effects isolated.");
        }

        /// <summary>Declared methods of the mod that read the current map or the camera view.</summary>
        private static List<MethodBase> FindViewChecks(Assembly assembly)
        {
            List<MethodBase> found = new List<MethodBase>();
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = Array.FindAll(e.Types, type => type != null);
            }

            foreach (Type type in types)
            {
                if (type.IsInterface || type.ContainsGenericParameters || Skipped(type))
                    continue;

                foreach (MethodBase method in AccessTools.GetDeclaredMethods(type))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null)
                        continue;

                    string full = Describe(method);
                    string own = method.Name;
                    string outer = full.Substring(0, full.IndexOf('.') + 1) + OuterName(own);
                    if (SkipMethods.Contains(full) || SkipMethods.Contains(outer)
                        || own == "MapComponentUpdate" || own == "MapComponentOnGUI"
                        || own.StartsWith("Draw", StringComparison.Ordinal))
                        continue;

                    if (ReadsView(method))
                        found.Add(method);
                }
            }
            return found;
        }

        private static string OuterName(string name)
        {
            int end = name.IndexOf('>');
            return name.StartsWith("<", StringComparison.Ordinal) && end > 1
                ? name.Substring(1, end - 1)
                : name;
        }

        private static bool Skipped(Type type)
        {
            for (Type current = type; current != null; current = current.DeclaringType)
                if (SkipTypes.Contains(current.Name))
                    return true;
            return false;
        }

        private static bool ReadsView(MethodBase method)
        {
            try
            {
                foreach (KeyValuePair<OpCode, object> instruction in PatchProcessor.ReadMethodBody(method))
                    if (instruction.Value is MethodInfo called
                        && (called == findCurrentMap || called == gameCurrentMap || called == cameraViewRect))
                        return true;
            }
            catch
            {
                // a method without readable IL cannot be a target
            }
            return false;
        }

        private static string Describe(MethodBase method)
        {
            Type type = method.DeclaringType;
            while (type != null && type.DeclaringType != null && type.Name.StartsWith("<", StringComparison.Ordinal))
                type = type.DeclaringType;
            return (type == null ? "?" : type.Name) + "." + method.Name;
        }

        private static IEnumerable<CodeInstruction> ViewTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo safeMap = AccessTools.Method(typeof(GorePerfected), nameof(SimSafeCurrentMap));
            MethodInfo safeGameMap = AccessTools.Method(typeof(GorePerfected), nameof(SimSafeGameCurrentMap));
            MethodInfo safeView = AccessTools.Method(typeof(GorePerfected), nameof(SimSafeViewRect));

            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo called)
                {
                    if (called == findCurrentMap)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = safeMap;
                    }
                    else if (called == gameCurrentMap)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = safeGameMap;
                    }
                    else if (called == cameraViewRect)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = safeView;
                    }
                }
                yield return instruction;
            }
        }

        private static Map SimulatedMap()
        {
            try
            {
                return mapContext == null ? null : mapContext();
            }
            catch
            {
                return null;
            }
        }

        public static Map SimSafeCurrentMap()
        {
            Map simulated = MP.IsInMultiplayer ? SimulatedMap() : null;
            return simulated ?? Find.CurrentMap;
        }

        public static Map SimSafeGameCurrentMap(Game game)
        {
            Map simulated = MP.IsInMultiplayer ? SimulatedMap() : null;
            return simulated ?? game.CurrentMap;
        }

        public static CellRect SimSafeViewRect(CameraDriver driver)
        {
            return MP.IsInMultiplayer && SimulatedMap() != null ? Everything : driver.CurrentViewRect;
        }

        private static void IsolatePrefix(out bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || !UnityData.IsInMainThread)
                return;
            Rand.PushState();
            __state = true;
        }

        private static void IsolateFinalizer(bool __state)
        {
            if (__state)
                Rand.PopState();
        }
    }
}
