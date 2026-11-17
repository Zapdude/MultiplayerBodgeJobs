using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VPersonaWeaponsE")]
public class VanillaPersonaWeaponsExpanded
{
	private static Type compGraphicCustomizationType;

	private static Type personaWeaponDialogType;

	private static AccessTools.FieldRef<Window, Thing> weaponCustomizationCurrentWeaponField;

	private static AccessTools.FieldRef<Window, WeaponTraitDef> weaponCustomizationCurrentWeaponTraitField;

	private static AccessTools.FieldRef<Window, Def> weaponCustomizationCurrentPsycastField;

	private static AccessTools.FieldRef<Window, ChoiceLetter> weaponCustomizationChoiceLetterField;

	private static AccessTools.FieldRef<object, ChoiceLetter> innerChoiceLetterThisField;

	public VanillaPersonaWeaponsExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		compGraphicCustomizationType = AccessTools.TypeByName("GraphicCustomization.CompGraphicCustomization");
		Type type = AccessTools.TypeByName("VanillaPersonaWeaponsExpanded.ChoiceLetter_ChoosePersonaWeapon");
		MP.RegisterSyncMethod(type, "RemoveAndResolveLetter");
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "OpenChooseDialog");
		MP.RegisterSyncDelegate(type, lambda.DeclaringType.Name, lambda.Name);
		innerChoiceLetterThisField = AccessTools.FieldRefAccess<ChoiceLetter>(lambda.DeclaringType, "<>4__this");
		VanillaPersonaWeaponsRuntime.Install();
	}

	private static void LatePatch()
	{
		MpCompatPatchLoader.LoadPatch<VanillaPersonaWeaponsExpanded>();
		Type type = (personaWeaponDialogType = AccessTools.TypeByName("VanillaPersonaWeaponsExpanded.Dialog_ChoosePersonaWeapon"));
		weaponCustomizationCurrentWeaponField = AccessTools.FieldRefAccess<Thing>(type, "currentWeapon");
		weaponCustomizationCurrentWeaponTraitField = AccessTools.FieldRefAccess<WeaponTraitDef>(type, "currentWeaponTrait");
		weaponCustomizationCurrentPsycastField = AccessTools.FieldRefAccess<Def>(type, "currentPsycast");
		weaponCustomizationChoiceLetterField = AccessTools.FieldRefAccess<ChoiceLetter>(type, "choiceLetter");
		MpCompat.RegisterLambdaMethod(type, "DoWindowContents", 3);
	}

	[MpCompatSyncWorker("VanillaPersonaWeaponsExpanded.Dialog_ChoosePersonaWeapon")]
	private static void SyncPersonaWeaponCustomizationDialog(SyncWorker sync, ref Window dialog)
	{
		ThingComp graphicCustomizationComp = null;
		if (sync.isWriting)
		{
			sync.Write(weaponCustomizationChoiceLetterField(dialog));
			sync.Write(weaponCustomizationCurrentWeaponField(dialog).def);
			sync.Write(weaponCustomizationCurrentWeaponTraitField(dialog));
			sync.Write(weaponCustomizationCurrentPsycastField(dialog));
		}
		else
		{
			dialog = (Window)RuntimeHelpers.GetUninitializedObject(personaWeaponDialogType);
			ChoiceLetter choiceLetter = sync.Read<ChoiceLetter>();
			weaponCustomizationChoiceLetterField(dialog) = choiceLetter;
			ThingDef thingDef = sync.Read<ThingDef>();
			if (choiceLetter != null && !choiceLetter.ArchivedOnly)
			{
				Thing thing = ThingMaker.MakeThing(thingDef, GenStuff.DefaultStuffFor(thingDef));
				weaponCustomizationCurrentWeaponField(dialog) = thing;
				if (thing is ThingWithComps thingWithComps)
				{
					foreach (ThingComp allComp in thingWithComps.AllComps)
					{
						if (compGraphicCustomizationType.IsInstanceOfType(allComp))
						{
							graphicCustomizationComp = (VanillaExpandedFramework.graphicCustomizationCompField(dialog) = allComp);
						}
					}
				}
			}
			weaponCustomizationCurrentWeaponTraitField(dialog) = sync.Read<WeaponTraitDef>();
			weaponCustomizationCurrentPsycastField(dialog) = sync.Read<Def>();
		}
		VanillaExpandedFramework.SyncGraphicCustomizationDialog(sync, ref dialog, graphicCustomizationComp);
	}

	[MpCompatPrefix("VanillaPersonaWeaponsExpanded.Dialog_ChoosePersonaWeapon", "DoWindowContents", MethodType.Normal)]
	private static void CloseDialogIfLetterArchived(Window __instance, ChoiceLetter ___choiceLetter)
	{
		if (MP.IsInMultiplayer && (___choiceLetter == null || ___choiceLetter.ArchivedOnly))
		{
			__instance.Close();
		}
	}

	[MpCompatPrefix("VanillaPersonaWeaponsExpanded.Dialog_ChoosePersonaWeapon", "DoWindowContents", 3, MethodType.Normal)]
	private static bool PreSyncAcceptPersonaWeapon(ChoiceLetter ___choiceLetter)
	{
		return CanAcceptPersonaWeapon(___choiceLetter);
	}

	[MpCompatPrefix("VanillaPersonaWeaponsExpanded.ChoiceLetter_ChoosePersonaWeapon", "OpenChooseDialog", 0, MethodType.Normal)]
	private static bool PreSyncAcceptNonCustomizablePersonaWeapon(object __instance)
	{
		return CanAcceptPersonaWeapon(innerChoiceLetterThisField(__instance));
	}

	private static bool CanAcceptPersonaWeapon(ChoiceLetter choiceLetter)
	{
		if (MP.IsInMultiplayer)
		{
			if (choiceLetter != null)
			{
				return !choiceLetter.ArchivedOnly;
			}
			return false;
		}
		return true;
	}

	[MpCompatPrefix("VanillaPersonaWeaponsExpanded.ChoiceLetter_ChoosePersonaWeapon", "Choices", 0, MethodType.Getter)]
	private static bool DontRemoveLetterOnPostpone()
	{
		return !MP.IsInMultiplayer;
	}
}
