using System.Reflection;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Stops Graphic_Random from indexing with a negative thing ID.
    /// </summary>
    /// <remarks>
    /// SubGraphicFor uses <c>subGraphics[num % subGraphics.Length]</c> with
    /// <c>num = OverrideGraphicIndex ?? thingIDNumber</c>. In multiplayer, interface-created things
    /// can reach the map with negative local IDs, and a negative modulo is a negative index. This is
    /// a render-only guard; the real fix is to keep negative IDs off the map. Kept in its own commit
    /// so it can be reverted independently if it causes trouble.
    /// </remarks>
    [StaticConstructorOnStartup]
    internal static class GraphicRandomNegativeIndexGuard
    {
        private static readonly FieldInfo SubGraphicsField =
            AccessTools.Field(typeof(Graphic_Collection), "subGraphics");

        static GraphicRandomNegativeIndexGuard()
        {
            MpCompat.harmony.Patch(
                AccessTools.Method(typeof(Graphic_Random), nameof(Graphic_Random.SubGraphicFor)),
                prefix: new HarmonyMethod(typeof(GraphicRandomNegativeIndexGuard), nameof(Prefix)));
        }

        private static bool Prefix(Graphic_Random __instance, Thing thing, ref Graphic __result)
        {
            Graphic[] subGraphics = (Graphic[])SubGraphicsField.GetValue(__instance);
            if (subGraphics == null || subGraphics.Length == 0)
            {
                __result = BaseContent.BadGraphic;
                return false;
            }

            int index = thing?.OverrideGraphicIndex ?? thing?.thingIDNumber ?? 0;
            __result = subGraphics[((index % subGraphics.Length) + subGraphics.Length) % subGraphics.Length];
            return false;
        }
    }
}
