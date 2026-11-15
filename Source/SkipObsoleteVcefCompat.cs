using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MultiplayerBodgeJobs
{
    // MPCompat's VCEF compat class is written against the non-Odyssey build of Vanilla Fishing Expanded.
    // The Odyssey build deletes VCE_Fishing.Command_SetFishList, Zone_Fishing and JobDriver_Fish, so the
    // type lookup returns null and FieldRefAccess throws. MpCompatLoader catches the exception and logs
    // it, but the compat class stays half-initialized and the error repeats on every load.
    //
    // The patch is applied from a Mod constructor rather than a StaticConstructorOnStartup class:
    // MPCompat's own constructor runs during LoadedModManager.CreateModClasses, and static constructors
    // only run later, in StaticConstructorOnStartupUtility.CallAll. About.xml declares loadBefore on
    // rwmt.MultiplayerCompatibility so this constructor runs first.
    public class MultiplayerBodgeJobsEarlyPatch : Mod
    {
        public MultiplayerBodgeJobsEarlyPatch(ModContentPack content) : base(content)
        {
            SkipObsoleteVcefCompat.Apply();
        }
    }

    internal static class SkipObsoleteVcefCompat
    {
        private const string CompatTypeName = "Multiplayer.Compat.VanillaFishingExpanded";
        private const string MissingTypeName = "VCE_Fishing.Command_SetFishList";

        internal static void Apply()
        {
            if (!ModsConfig.IsActive("VanillaExpanded.VCEF"))
            {
                return;
            }

            // Testing for the missing type rather than for Odyssey keeps this correct if VCEF restores
            // the class, and covers any other build that drops it.
            if (AccessTools.TypeByName(MissingTypeName) != null)
            {
                return;
            }

            Type compatType = AccessTools.TypeByName(CompatTypeName);
            if (compatType == null)
            {
                return;
            }

            ConstructorInfo ctor = AccessTools.Constructor(compatType, new[] { typeof(ModContentPack) });
            if (ctor == null)
            {
                Log.Warning($"[MultiplayerBodgeJobs] {CompatTypeName}(ModContentPack) not found; the VCEF compat error will still be logged.");
                return;
            }

            new Harmony("zap.multiplayerbodgejobs.vcef").Patch(
                ctor,
                prefix: new HarmonyMethod(typeof(SkipObsoleteVcefCompat), nameof(SkipPrefix)));

            Log.Message($"[MultiplayerBodgeJobs] Skipping {CompatTypeName}; {MissingTypeName} is absent from this VCEF build.");
        }

        // Returning false skips the constructor, which is where the null type lookup throws.
        private static bool SkipPrefix()
        {
            return false;
        }
    }
}
