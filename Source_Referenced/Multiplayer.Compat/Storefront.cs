using System;
using CashRegister;
using HarmonyLib;
using Multiplayer.API;
using Storefront.Store;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Adamas.Storefront")]
public class Storefront
{
	private record StoreWrapper(StoreController Controller) : ThingFilterContext
	{
		public override ThingFilter Filter => Controller.GetStoreFilter();

		public override ThingFilter ParentFilter => null;

		public StoreController Controller { get; } = Controller;
	}

	private static ISyncField openForBusinessField;

	private static ISyncField guestPricePercentageField;

	public Storefront(ModContentPack mod)
	{
		Type typeFromHandle = typeof(StoreController);
		openForBusinessField = MP.RegisterSyncField(typeFromHandle, "openForBusiness");
		guestPricePercentageField = MP.RegisterSyncField(typeFromHandle, "guestPricePercentage");
		MP.RegisterSyncMethod(typeFromHandle, "Name");
		MP.RegisterSyncMethod(typeFromHandle, "LinkRegister");
		MP.RegisterSyncWorker<StoreController>(SyncStoreController, typeFromHandle);
		typeFromHandle = typeof(ITab_Register_Store);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeFromHandle, "FillTab"), new HarmonyMethod(typeof(Storefront), "PreFillTab"), null, null, new HarmonyMethod(typeof(Storefront), "PostFillTab"));
	}

	private static void SyncStoreController(SyncWorker sync, ref StoreController controller)
	{
		if (sync.isWriting)
		{
			StoresManager val = controller.Map?.GetComponent<StoresManager>();
			int num = val?.Stores.IndexOf(controller) ?? (-1);
			sync.Write(num);
			if (num >= 0)
			{
				sync.Write<StoresManager>(val);
			}
			return;
		}
		int num2 = sync.Read<int>();
		if (num2 >= 0)
		{
			StoresManager val2 = sync.Read<StoresManager>();
			if (val2.Stores.Count > num2)
			{
				controller = val2.Stores[num2];
			}
			else
			{
				Log.Error($"Received out-of-range store index, received={num2}, count={val2.Stores.Count}");
			}
		}
	}

	private static void PreFillTab(ITab_Register_Store __instance, out bool __state)
	{
		if (!MP.IsInMultiplayer)
		{
			__state = false;
			return;
		}
		__instance.store = StoreUtility.GetStore(((ITab_Register)__instance).Register);
		MP.SetThingFilterContext(new StoreWrapper(__instance.store));
		MP.WatchBegin();
		openForBusinessField.Watch(__instance.store);
		guestPricePercentageField.Watch(__instance.store);
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
}
