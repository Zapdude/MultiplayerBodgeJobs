using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Tribals")]
public class VanillaFactionsTribal
{
	private static AccessTools.FieldRef<GameComponent> tribalsGameCompInstance;

	[MpCompatSyncField("VFETribals.GameComponent_Tribals", "ethos")]
	protected static ISyncField tribalsGameCompEthosField;

	[MpCompatSyncField("VFETribals.GameComponent_Tribals", "ethosLocked")]
	protected static ISyncField tribalsGameCompEthosLockedField;

	private static Type customizeCornerstonesWindowType;

	private static FastInvokeHandler fillCornerstoneDefsMethod;

	public VanillaFactionsTribal(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			MpCompatPatchLoader.LoadPatch(this);
		});
		MpCompat.RegisterLambdaDelegate("VFETribals.LargeFire", "GetGizmos", 0, 1, 2, 3);
		foreach (MethodInfo lambdum in MpMethodUtil.GetLambda(AccessTools.TypeByName("VFETribals.ChoiceLetter_WildMenJoin"), "Choices", MethodType.Getter, null, 0, 1))
		{
			MP.RegisterSyncMethod(lambdum);
		}
		fillCornerstoneDefsMethod = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(customizeCornerstonesWindowType = AccessTools.TypeByName("VFETribals.Window_CustomizeCornerstones"), "FillCornerstoneDefs"));
		Type type = AccessTools.TypeByName("VFETribals.GameComponent_Tribals");
		tribalsGameCompInstance = AccessTools.StaticFieldRefAccess<GameComponent>(AccessTools.DeclaredField(type, "Instance"));
		MP.RegisterSyncMethod(type, "AddCornerstone").SetPostInvoke(PostAddCornerstone);
	}

	[MpCompatPrefix("VFETribals.GameComponent_Tribals", "AddCornerstone", MethodType.Normal)]
	private static bool PreAddCornerstone(Def def, int ___availableCornerstonePoints, IList ___cornerstones)
	{
		if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
		{
			return true;
		}
		if (___availableCornerstonePoints > 0)
		{
			return !___cornerstones.Contains(def);
		}
		return false;
	}

	private static void PostAddCornerstone(object instance, object[] args)
	{
		foreach (Window window in Find.WindowStack.Windows)
		{
			if (customizeCornerstonesWindowType.IsInstanceOfType(window))
			{
				fillCornerstoneDefsMethod(window);
			}
		}
	}

	[MpCompatPrefix("VFETribals.Window_CustomizeCornerstones", "DoWindowContents", MethodType.Normal)]
	[MpCompatPrefix("VFETribals.Dialog_EditEthos", "ApplyChanges", MethodType.Normal)]
	private static void PreEthosFieldWatch()
	{
		if (MP.IsInMultiplayer)
		{
			GameComponent target = tribalsGameCompInstance();
			MP.WatchBegin();
			tribalsGameCompEthosField.Watch(target);
			tribalsGameCompEthosLockedField.Watch(target);
		}
	}

	[MpCompatPostfix("VFETribals.Window_CustomizeCornerstones", "DoWindowContents", MethodType.Normal)]
	[MpCompatPostfix("VFETribals.Dialog_EditEthos", "ApplyChanges", MethodType.Normal)]
	private static void PostEthosFieldWatch()
	{
		if (MP.IsInMultiplayer)
		{
			MP.WatchEnd();
		}
	}
}
