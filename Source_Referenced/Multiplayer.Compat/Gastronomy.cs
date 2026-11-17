using System;
using System.Collections.Generic;
using System.Linq;
using CashRegister;
using Gastronomy.Dining;
using Gastronomy.Restaurant;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Orion.Gastronomy")]
internal class Gastronomy
{
	public record RestaurantWrapper(RestaurantController Controller) : ThingFilterContext
	{
		public override ThingFilter Filter => Controller.Menu.menuFilter;

		public override ThingFilter ParentFilter => Controller.Menu.menuGlobalFilter;

		public override IEnumerable<SpecialThingFilterDef> HiddenFilters
		{
			get
			{
				yield return SpecialThingFilterDefOf.AllowFresh;
			}
		}

		public RestaurantController Controller { get; } = Controller;
	}

	private static ISyncField openForBusinessField;

	private static ISyncField allowGuestsField;

	private static ISyncField allowColonistsField;

	private static ISyncField allowPrisonersField;

	private static ISyncField allowSlavesField;

	private static ISyncField guestPricePercentageField;

	public Gastronomy(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type typeFromHandle = typeof(RestaurantController);
		openForBusinessField = MP.RegisterSyncField(typeFromHandle, "openForBusiness");
		allowGuestsField = MP.RegisterSyncField(typeFromHandle, "allowGuests");
		allowColonistsField = MP.RegisterSyncField(typeFromHandle, "allowColonists");
		allowPrisonersField = MP.RegisterSyncField(typeFromHandle, "allowPrisoners");
		allowSlavesField = MP.RegisterSyncField(typeFromHandle, "allowSlaves");
		guestPricePercentageField = MP.RegisterSyncField(typeFromHandle, "guestPricePercentage");
		MP.RegisterSyncMethod(typeFromHandle, "Name");
		MP.RegisterSyncMethod(typeFromHandle, "LinkRegister");
		MP.RegisterSyncWorker<RestaurantController>(SyncRestaurantController, typeFromHandle);
		typeFromHandle = typeof(RestaurantsManager);
		MP.RegisterSyncMethod(typeFromHandle, "AddRestaurant").SetContext(SyncContext.MapSelected);
		MP.RegisterSyncMethod(typeFromHandle, "DeleteRestaurant");
		MpCompat.harmony.Patch(AccessTools.Method(typeFromHandle, "AddRestaurant"), new HarmonyMethod(typeof(Gastronomy), "PreAddRestaurant"), new HarmonyMethod(typeof(Gastronomy), "PostAddRestaurant"));
		typeFromHandle = typeof(ITab_Register_Restaurant);
		MpCompat.harmony.Patch(AccessTools.Method(typeFromHandle, "FillTab"), new HarmonyMethod(typeof(Gastronomy), "PreFillTab"), null, null, new HarmonyMethod(typeof(Gastronomy), "PostFillTab"));
		MpCompat.harmony.Patch(AccessTools.Method(typeFromHandle, "SetRestaurant"), new HarmonyMethod(typeof(Gastronomy), "PreSetRestaurant"));
		typeFromHandle = typeof(CompCanDineAt);
		MP.RegisterSyncMethod(typeFromHandle, "ToggleDining");
		MP.RegisterSyncMethod(typeFromHandle, "ChangeDeco");
		MpCompat.harmony.Patch(AccessTools.Constructor(typeof(RestaurantMenu)), null, new HarmonyMethod(typeof(Gastronomy), "PostRestaurantMenuConstructor"));
	}

	private static void SyncRestaurantController(SyncWorker sync, ref RestaurantController controller)
	{
		if (sync.isWriting)
		{
			RestaurantsManager val = controller.Map?.GetComponent<RestaurantsManager>();
			int num = val?.restaurants.IndexOf(controller) ?? (-1);
			sync.Write(num);
			if (num >= 0)
			{
				sync.Write<RestaurantsManager>(val);
			}
			return;
		}
		int num2 = sync.Read<int>();
		if (num2 >= 0)
		{
			RestaurantsManager val2 = sync.Read<RestaurantsManager>();
			if (val2.restaurants.Count > num2)
			{
				controller = val2.restaurants[num2];
			}
			else
			{
				Log.Error($"Received out-of-range store index, received={num2}, count={val2.restaurants.Count}");
			}
		}
	}

	private static void PreFillTab(ITab_Register_Restaurant __instance, out bool __state)
	{
		if (!MP.IsInMultiplayer)
		{
			__state = false;
			return;
		}
		__instance.restaurant = RestaurantUtility.GetRestaurant(((ITab_Register)__instance).Register);
		if (__instance.restaurant == null)
		{
			__instance.restaurant = RestaurantUtility.GetAllRestaurants((Thing)(object)((ITab_Register)__instance).Register).First();
		}
		MP.SetThingFilterContext(new RestaurantWrapper(__instance.restaurant));
		MP.WatchBegin();
		openForBusinessField.Watch(__instance.restaurant);
		allowGuestsField.Watch(__instance.restaurant);
		allowColonistsField.Watch(__instance.restaurant);
		allowPrisonersField.Watch(__instance.restaurant);
		allowSlavesField.Watch(__instance.restaurant);
		guestPricePercentageField.Watch(__instance.restaurant);
		__state = true;
	}

	private static void PostFillTab(bool __state)
	{
		if (__state)
		{
			MP.SetThingFilterContext(null);
			MP.WatchEnd();
		}
	}

	private static void PreAddRestaurant(RestaurantsManager __instance, ref bool __state)
	{
		__state = MP.IsInMultiplayer && __instance.restaurants.Any();
	}

	private static void PostAddRestaurant(ref bool __state, RestaurantController __result)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected Obj, but got Unknown
		if (!__state || __result == null)
		{
			return;
		}
		__result.LinkRegister((Building_CashRegister)Find.Selector.SingleSelectedThing);
		if (MP.IsExecutingSyncCommandIssuedBySelf && Find.MainTabsRoot.OpenTab == MainButtonDefOf.Inspect)
		{
			IEnumerable<InspectTabBase> curTabs = ((MainTabWindow_Inspect)Find.MainTabsRoot.OpenTab.TabWindow).CurTabs;
			ITab_Register_Restaurant val = ((curTabs != null) ? curTabs.OfType<ITab_Register_Restaurant>().FirstOrDefault() : null);
			if (val != null)
			{
				val.restaurant = __result;
			}
		}
	}

	private static bool PreSetRestaurant(object newRestaurant)
	{
		return newRestaurant != null;
	}

	private static void PostRestaurantMenuConstructor(RestaurantMenu __instance)
	{
		ThingFilter thingFilter = default;
		ThingFilter thingFilter2 = default;
		__instance.GetMenuFilters(ref thingFilter, ref thingFilter2);
	}
}
