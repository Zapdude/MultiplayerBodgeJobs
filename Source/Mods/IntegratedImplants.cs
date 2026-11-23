using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Integrated Implants (lts.i): the devourer's early-release roll.</summary>
    /// <remarks>
    /// The mod postfixes the Anomaly devourer's tick with
    /// <c>new System.Random().Next(0, 1800) == 1799</c>. An unseeded Random takes its seed from the
    /// clock, so every PC rolled its own numbers and the devourer could release its prey on one PC
    /// and keep it on another. Only that generator is replaced: in multiplayer it is seeded from the
    /// devourer's id and the game tick, so the roll is identical on every PC and the chance is
    /// unchanged. Single player keeps the mod's own generator. Behaviour reference: Multiplayer Fix
    /// (MIT); this is a reimplementation.
    /// </remarks>
    [MpCompatFor("lts.i")]
    class IntegratedImplants
    {
        private static ConstructorInfo randomCtor;

        public IntegratedImplants(ModContentPack mod)
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
                Log.Error("[MPCompat] Integrated Implants: install failed; module disabled.\n" + e);
            }
        }

        private static void Install()
        {
            Type patch = AccessTools.TypeByName("LTS_Implants.CompDevourer_CompTick_Patch");
            if (patch == null)
            {
                Log.Message("[MPCompat] Integrated Implants: not loaded; skipped.");
                return;
            }

            MethodInfo postfix = AccessTools.Method(patch, "CompTickPostfix");
            if (postfix == null || !postfix.IsStatic || postfix.GetParameters().Length != 1
                || !typeof(ThingComp).IsAssignableFrom(postfix.GetParameters()[0].ParameterType))
            {
                Log.Error("[MPCompat] Integrated Implants: CompTickPostfix changed; module disabled.");
                return;
            }

            randomCtor = AccessTools.Constructor(typeof(Random), Type.EmptyTypes);
            int found = 0;
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(postfix))
                if (IsRandom(instruction))
                    found++;
            if (found != 1)
            {
                Log.Error("[MPCompat] Integrated Implants: expected one new Random() in CompTickPostfix,"
                    + " found " + found + "; module disabled.");
                return;
            }

            MpCompat.harmony.Patch(postfix,
                transpiler: new HarmonyMethod(typeof(IntegratedImplants), nameof(Transpiler)));
            Log.Message("[MPCompat] Integrated Implants: devourer roll seeded from the devourer and the tick.");
        }

        private static bool IsRandom(CodeInstruction instruction) =>
            instruction.opcode == OpCodes.Newobj
            && instruction.operand is ConstructorInfo constructor
            && constructor == randomCtor;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo seeded = AccessTools.Method(typeof(IntegratedImplants), nameof(RandomFor));
            foreach (CodeInstruction instruction in instructions)
            {
                if (!IsRandom(instruction))
                {
                    yield return instruction;
                    continue;
                }

                // new Random() -> RandomFor(__instance); the postfix's only argument is the comp.
                CodeInstruction load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels);
                load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, seeded);
            }
        }

        /// <summary>Single player: the mod's own generator. Multiplayer: seeded from the devourer and the tick.</summary>
        public static Random RandomFor(ThingComp comp)
        {
            if (!MP.IsInMultiplayer)
                return new Random();

            int id = comp?.parent?.thingIDNumber ?? 0;
            int tick = Find.TickManager?.TicksGame ?? 0;
            return new Random(Mix(Gen.HashCombineInt(id, tick)));
        }

        /// <summary>Spreads neighbouring seeds (the next tick) far apart (murmur3 finalizer).</summary>
        internal static int Mix(int hash)
        {
            uint value = (uint)hash;
            value ^= value >> 16;
            value *= 0x85EBCA6Bu;
            value ^= value >> 13;
            value *= 0xC2B2AE35u;
            value ^= value >> 16;
            return (int)(value & 0x7FFFFFFF);
        }
    }
}
