using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Forces global thing IDs while VGE2 generates the landing crew and the live landing contents.
    /// </summary>
    /// <remarks>
    /// The 1.6 VGE2 version generates the raid crew only inside CaptureGravshipCoroutine through
    /// GeneratePawns, and Impact merely respawns the stored pawnPositions on the real map. The
    /// preview coroutine is interface work, so UniqueIdsPatch hands out local negative IDs, which
    /// the crew keeps into the live map. Suppressing the preview (the 1.7 approach) would remove
    /// the crew here, so keep the preview and instead force global IDs around GeneratePawns and
    /// Impact. The temp-map station props stay on local IDs and are discarded with the temp map;
    /// the Graphic_Random guard handles their preview render.
    /// </remarks>
    internal static class VanillaGravshipGlobalIds
    {
        [ThreadStatic] private static int depth;

        private static FieldInfo forceGlobalIds;

        internal static void Install(params string[] typeNames)
        {
            Type flagType = AccessTools.TypeByName("Multiplayer.Client.Patches.UniqueIdsPatch");
            forceGlobalIds = flagType == null ? null : AccessTools.Field(flagType, "forceGlobalIds");
            if (forceGlobalIds == null)
            {
                Log.Error("[MPCompat] VGE2 landing: UniqueIdsPatch.forceGlobalIds not found. " +
                          "Update Multiplayer Bodged; the landing crew will keep negative IDs.");
                return;
            }

            foreach (string typeName in typeNames)
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    Log.Warning($"[MPCompat] VGE2 landing: type {typeName} was not loaded; global ID scope skipped.");
                    continue;
                }

                MethodInfo generatePawns = AccessTools.Method(type, "GeneratePawns");
                if (generatePawns != null && generatePawns.GetParameters().Length == 3)
                    Patch(generatePawns, typeName + ".GeneratePawns");
                else
                    Log.Warning($"[MPCompat] VGE2 landing: {typeName}.GeneratePawns not found; the crew may keep negative IDs.");

                MethodInfo impact = AccessTools.Method(type, "Impact");
                if (impact != null)
                    Patch(impact, typeName + ".Impact");
                else
                    Log.Warning($"[MPCompat] VGE2 landing: {typeName}.Impact not found; the live contents may keep negative IDs.");
            }
        }

        private static void Patch(MethodInfo method, string label)
        {
            MpCompat.harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(VanillaGravshipGlobalIds), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(VanillaGravshipGlobalIds), nameof(Finalizer)));
            Log.Message($"[MPCompat] Forcing global thing IDs during VGE2 {label}.");
        }

        private static void Prefix(ref bool __state)
        {
            if (depth++ > 0)
                return;

            __state = (bool)forceGlobalIds.GetValue(null);
            forceGlobalIds.SetValue(null, true);
        }

        private static void Finalizer(bool __state)
        {
            if (--depth > 0)
                return;

            forceGlobalIds.SetValue(null, __state);
        }
    }
}
