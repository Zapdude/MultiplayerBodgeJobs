using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Storyteller: Waves &amp; Rewards (Ruslan.WavesRewards) slave market.</summary>
    /// <remarks>
    /// The comms-console job opens Dialog_SlaveMarket on every PC at the same tick, but the window
    /// generated its five offers per kind while drawing, then paid and placed the order from the
    /// interface on the clicking PC only. In multiplayer the simulation now generates both offer
    /// lists when the window is created, so every PC has the same pawns; "order" sends
    /// (window, kind, count) as one command that prices, pays and places the order in the simulation
    /// and discards the rest; closing without ordering is a command too. Behaviour reference:
    /// Multiplayer Fix (MIT); this is a reimplementation.
    /// </remarks>
    [MpCompatFor("ruslan.wavesrewards")]
    class WavesRewardsSlaveMarket
    {
        private static Type dialogType;
        private static Type marketType;
        private static Type kindType;

        private static FieldInfo kindField;
        private static FieldInfo countField;
        private static FieldInfo offersField;

        private static MethodInfo generateMethod;
        private static MethodInfo discardMethod;
        private static MethodInfo placeOrderMethod;
        private static MethodInfo tryPayMethod;
        private static MethodInfo useVouchersMethod;
        private static MethodInfo priceOfMethod;
        private static PropertyInfo vouchersProperty;
        private static PropertyInfo canBuyNowProperty;

        private static readonly Dictionary<int, IDictionary> markets = new Dictionary<int, IDictionary>();
        private static readonly HashSet<int> orderSentHere = new HashSet<int>();
        private static Window current;

        public WavesRewardsSlaveMarket(ModContentPack mod)
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
                Log.Error("[MPCompat] Waves & Rewards slave market: install failed; module disabled.\n" + e);
            }
        }

        private static void Install()
        {
            dialogType = WavesRewardsShared.Type("Slaves.Dialog_SlaveMarket");
            marketType = WavesRewardsShared.Type("Slaves.SlaveMarket");
            kindType = WavesRewardsShared.Type("Slaves.SlaveOfferKind");
            if (dialogType == null || marketType == null || kindType == null)
                throw new TypeLoadException("Waves & Rewards slave market types missing");

            kindField = WavesRewardsShared.Field(dialogType, "kind");
            countField = WavesRewardsShared.Field(dialogType, "count");
            offersField = WavesRewardsShared.Field(dialogType, "offers");

            generateMethod = WavesRewardsShared.Method(marketType, "GenerateSlave", kindType);
            discardMethod = WavesRewardsShared.Method(marketType, "Discard", typeof(IEnumerable<Pawn>));
            placeOrderMethod = WavesRewardsShared.Method(marketType, "PlaceOrder", typeof(Map), typeof(List<Pawn>));
            tryPayMethod = WavesRewardsShared.Method(marketType, "TryPay", typeof(Map), typeof(int));
            useVouchersMethod = WavesRewardsShared.Method(marketType, "UseVouchers", typeof(int));
            priceOfMethod = WavesRewardsShared.Method(marketType, "PriceOf", typeof(Pawn), kindType);
            vouchersProperty = WavesRewardsShared.Property(marketType, "ReplacementVouchers");
            canBuyNowProperty = WavesRewardsShared.Property(marketType, "CanBuyNow");
            MethodInfo offerMethod = WavesRewardsShared.Method(dialogType, "Offer", kindType);

            MemberInfo[] required =
            {
                kindField, countField, offersField, generateMethod, discardMethod, placeOrderMethod,
                tryPayMethod, useVouchersMethod, priceOfMethod, vouchersProperty, canBuyNowProperty, offerMethod
            };
            if (required.Any(member => member == null))
                throw new MissingMemberException(dialogType.FullName, "slave market members");

            foreach (ConstructorInfo constructor in AccessTools.GetDeclaredConstructors(dialogType))
                if (!constructor.IsStatic)
                    Patch(constructor, postfix: nameof(CtorPostfix), priority: Priority.Low);

            Patch(offerMethod, prefix: nameof(OfferPrefix), priority: Priority.First);
            Patch(WavesRewardsShared.Method(dialogType, "DoWindowContents"),
                prefix: nameof(DrawPrefix), finalizer: nameof(DrawFinalizer));
            Patch(WavesRewardsShared.Method(dialogType, "PostClose"),
                prefix: nameof(PostClosePrefix), priority: Priority.First);
            Patch(tryPayMethod, prefix: nameof(TryPayPrefix), priority: Priority.First);
            Patch(placeOrderMethod, prefix: nameof(SkipInWindowPrefix), priority: Priority.First);
            Patch(useVouchersMethod, prefix: nameof(SkipInWindowPrefix), priority: Priority.First);

            MP.RegisterSyncMethod(typeof(WavesRewardsSlaveMarket), nameof(SyncedOrder));
            MP.RegisterSyncMethod(typeof(WavesRewardsSlaveMarket), nameof(SyncedClose));
        }

        private static void Patch(MethodBase method, string prefix = null, string postfix = null,
            string finalizer = null, int priority = Priority.Normal)
        {
            if (method == null)
                throw new MissingMethodException("Waves & Rewards slave market", "target");
            MpCompat.harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(WavesRewardsSlaveMarket), prefix) { priority = priority },
                postfix: postfix == null ? null : new HarmonyMethod(typeof(WavesRewardsSlaveMarket), postfix) { priority = priority },
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(WavesRewardsSlaveMarket), finalizer) { priority = priority });
        }

        private static void Clear()
        {
            markets.Clear();
            orderSentHere.Clear();
            current = null;
        }

        // ---- Window creation and display ----

        private static void CtorPostfix(Window __instance)
        {
            if (WavesRewardsShared.EnsureSameGame())
                Clear();
            if (!MP.IsInMultiplayer || MP.InInterface)
                return;

            int key = WavesRewardsShared.AssignKey(__instance);
            if (key < 0 || !(offersField.GetValue(__instance) is IDictionary offers))
                return;

            try
            {
                foreach (object kind in Enum.GetValues(kindType))
                {
                    if (offers.Contains(kind))
                        continue;
                    List<Pawn> list = new List<Pawn>();
                    for (int i = 0; i < 5; i++)
                        list.Add((Pawn)generateMethod.Invoke(null, new[] { kind }));
                    offers[kind] = list;
                }
                markets[key] = offers;
            }
            catch (Exception e)
            {
                Log.Error("[MPCompat] Waves & Rewards slave market: offer generation failed. " + e);
            }
        }

        private static bool OfferPrefix(Window __instance, object[] __args, ref List<Pawn> __result)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface)
                return true;
            if (offersField.GetValue(__instance) is IDictionary offers && offers.Contains(__args[0]))
                return true;
            // Never generate pawns in the interface: local IDs would differ on every PC.
            __result = new List<Pawn>();
            return false;
        }

        private static void DrawPrefix(Window __instance) => current = __instance;

        private static void DrawFinalizer() => current = null;

        private static bool TryPayPrefix(Map map, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || current == null)
                return true;

            int key = WavesRewardsShared.KeyOf(current);
            orderSentHere.Add(key);
            SyncedOrder(key, map, Convert.ToInt32(kindField.GetValue(current)), (int)countField.GetValue(current));
            // The window continues as if paid (sound, message, close); the command does the order.
            __result = true;
            return false;
        }

        private static bool SkipInWindowPrefix() =>
            !(MP.IsInMultiplayer && MP.InInterface && current != null);

        private static bool PostClosePrefix(Window __instance)
        {
            if (!MP.IsInMultiplayer)
                return true;

            int key = WavesRewardsShared.KeyOf(__instance);
            if (MP.InInterface && key >= 0 && !orderSentHere.Contains(key) && markets.ContainsKey(key))
                SyncedClose(key);

            // The commands discard the offers in the simulation; give the original an empty map.
            try
            {
                offersField.SetValue(__instance, Activator.CreateInstance(offersField.FieldType));
            }
            catch
            {
                // the window is closing anyway
            }
            return true;
        }

        // ---- Synced commands ----

        private static void SyncedOrder(int key, Map map, int kind, int count)
        {
            if (WavesRewardsShared.EnsureSameGame())
                Clear();
            if (!markets.TryGetValue(key, out IDictionary offers))
            {
                WavesRewardsShared.CloseKeyed(key);
                return;
            }
            markets.Remove(key);

            List<Pawn> bought = new List<Pawn>();
            try
            {
                object market = WavesRewardsShared.Instance(marketType);
                object kindValue = Enum.ToObject(kindType, kind);
                List<Pawn> list = offers.Contains(kindValue) ? offers[kindValue] as List<Pawn> : null;
                int canBuy = market == null ? 0 : (int)canBuyNowProperty.GetValue(market, null);
                if (market == null || list == null || map == null || canBuy <= 0)
                    return;

                count = Mathf.Clamp(count, 1, Mathf.Max(1, Mathf.Min(5, canBuy)));
                int vouchers = (int)vouchersProperty.GetValue(market, null);
                int total = 0;
                int used = 0;
                for (int i = 0; i < count && i < list.Count; i++)
                {
                    int price = (int)priceOfMethod.Invoke(null, new object[] { list[i], kindValue });
                    if (i < vouchers)
                    {
                        price = Mathf.RoundToInt(price * 0.5f);
                        used++;
                    }
                    total += price;
                    bought.Add(list[i]);
                }

                if ((bool)tryPayMethod.Invoke(null, new object[] { map, total }))
                {
                    placeOrderMethod.Invoke(market, new object[] { map, bought });
                    useVouchersMethod.Invoke(market, new object[] { used });
                }
                else
                {
                    bought.Clear();
                    Log.Warning("[MPCompat] Waves & Rewards: the slave order was not paid; no pawns were bought.");
                }
            }
            catch (Exception e)
            {
                Log.Error("[MPCompat] Waves & Rewards slave order failed. " + e);
            }
            finally
            {
                DiscardExcept(offers, bought);
                WavesRewardsShared.CloseKeyed(key);
            }
        }

        private static void SyncedClose(int key)
        {
            if (WavesRewardsShared.EnsureSameGame())
                Clear();
            if (markets.TryGetValue(key, out IDictionary offers))
            {
                markets.Remove(key);
                DiscardExcept(offers, new List<Pawn>());
            }
            WavesRewardsShared.CloseKeyed(key);
        }

        private static void DiscardExcept(IDictionary offers, List<Pawn> keep)
        {
            try
            {
                foreach (object value in offers.Values)
                    if (value is List<Pawn> list)
                        discardMethod.Invoke(null, new object[] { list.Where(pawn => !keep.Contains(pawn)).ToList() });
                offers.Clear();
            }
            catch (Exception e)
            {
                Log.Error("[MPCompat] Waves & Rewards slave market: discard failed. " + e);
            }
        }
    }
}
