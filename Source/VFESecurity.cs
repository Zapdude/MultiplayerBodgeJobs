using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.Sound;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VFESecurity")]
internal class VFESecurity
{
	private static AccessTools.FieldRef<object, int> concealedTransitionDurationField;

	private static AccessTools.FieldRef<object, int> concealedTransitionTicksField;

	private static AccessTools.FieldRef<object, Effecter> concealedProgressBarField;

	private static FastInvokeHandler concealedIsPowerOff;

	private static FastInvokeHandler concealedGetProps;

	private static AccessTools.FieldRef<object, int> propsSubmergeSeconds;

	private static AccessTools.FieldRef<object, int> propsDeploySeconds;

	public VFESecurity(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		PatchingUtilities.PatchPushPopRand("VFESecurity.Verb_BurnerFireSpew:TryCastShot");
		PatchingUtilities.PatchPushPopRand("VFESecurity.Verb_Dazzle:SearchlightEffect");
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("VFESecurity.CompConcealed");
		Type type2 = AccessTools.TypeByName("VFESecurity.CompProperties_Concealed");
		concealedTransitionDurationField = AccessTools.FieldRefAccess<int>(type, "transitionDuration");
		concealedTransitionTicksField = AccessTools.FieldRefAccess<int>(type, "transitionTicks");
		concealedProgressBarField = AccessTools.FieldRefAccess<Effecter>(type, "progressBar");
		concealedIsPowerOff = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod(type, "IsPowerOff"));
		concealedGetProps = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredPropertyGetter(type, "Props"));
		propsSubmergeSeconds = AccessTools.FieldRefAccess<int>(type2, "submergeSeconds");
		propsDeploySeconds = AccessTools.FieldRefAccess<int>(type2, "deploySeconds");
		MP.RegisterSyncMethod(type, "StartTransition");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "StartTransition"), new HarmonyMethod(typeof(VFESecurity), "PreStartTransition"));
		Type type3 = AccessTools.TypeByName("VFESecurity.CompWorldArtillery");
		MethodInfo methodInfo = AccessTools.DeclaredMethod(type3, "StartAttack");
		MP.RegisterSyncMethod(methodInfo).SetContext(SyncContext.MapSelected);
		MpCompat.harmony.Patch(methodInfo, new HarmonyMethod(typeof(VFESecurity), "PreStartAttack"));
		MP.RegisterSyncMethod(type3, "Reset");
		PatchingUtilities.PatchCancelInInterface("VFESecurity.Building_TurretGun_OrderAttack_Patch:Prefix");
		PatchingUtilities.PatchCancelInInterface("VFESecurity.Building_TurretGun_ResetForcedTarget_Patch:Prefix");
		PatchingUtilities.PatchPushPopRand("VFESecurity.ArtilleryUtils:SpawnArtilleryProjectile");
	}

	private static bool PreStartTransition(object __instance, bool submerge)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if ((bool)concealedIsPowerOff(__instance))
		{
			return false;
		}
		object instance = concealedGetProps(__instance);
		int num = (submerge ? (propsSubmergeSeconds(instance) * 60) : (propsDeploySeconds(instance) * 60));
		concealedTransitionDurationField(__instance) = num;
		concealedTransitionTicksField(__instance) = num;
		concealedProgressBarField(__instance) = EffecterDefOf.ProgressBar.Spawn();
		if (submerge)
		{
			ThingWithComps parent = ((ThingComp)__instance).parent;
			SoundDefOf.Door_OpenPowered.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
		}
		return false;
	}

	private static void PreStartAttack()
	{
		if (!MP.IsExecutingSyncCommand)
		{
			CameraJumper.TryHideWorld();
		}
	}
}
