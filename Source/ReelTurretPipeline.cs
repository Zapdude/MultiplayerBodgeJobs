using System;
using System.IO;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("reel.turretpipeline")]
public class ReelTurretPipeline
{
	public ReelTurretPipeline(ModContentPack mod)
	{
		CorrectLegacyLoadFolders(mod);
		InstallStorageConstructionFix();
	}

	internal static void InstallStorageConstructionFix()
	{
		if (VersionControl.CurrentMajor == 1 && VersionControl.CurrentMinor == 7)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingDefGenerator_Buildings), "NewBlueprintDef_Thing"), new HarmonyMethod(typeof(ReelTurretPipeline), "BeforeCreateBlueprint"));
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Frame), "CompleteConstruction"), new HarmonyMethod(typeof(ReelTurretPipeline), "BeforeCompleteConstruction"));
		}
	}

	internal static bool IsReelStorage(ThingDef def)
	{
		string text = def?.defName;
		bool flag = ((text == "Reel_AmmoDrain" || text == "Reel_AmmoOutput") ? true : false);
		if (flag && def.thingClass != null)
		{
			return typeof(Building_Storage).IsAssignableFrom(def.thingClass);
		}
		return false;
	}

	private static void BeforeCreateBlueprint(ThingDef def, bool isInstallBlueprint)
	{
		if (!isInstallBlueprint && IsReelStorage(def) && def.building != null)
		{
			def.building.blueprintClass = typeof(Blueprint_Storage);
		}
	}

	internal static void BeforeCompleteConstruction(Frame __instance)
	{
		if (__instance.storageSettings == null && IsReelStorage(__instance.BuildDef))
		{
			StorageSettings storageSettings = __instance.storageGroup?.GetStoreSettings() ?? __instance.BuildDef.building?.defaultStorageSettings ?? __instance.BuildDef.building?.fixedStorageSettings;
			StorageSettings storageSettings2 = new StorageSettings();
			if (storageSettings != null)
			{
				storageSettings2.CopyFrom(storageSettings);
			}
			__instance.storageSettings = storageSettings2;
		}
	}

	internal static bool CorrectLegacyLoadFolders(ModContentPack mod)
	{
		if (VersionControl.CurrentMajor != 1 || VersionControl.CurrentMinor != 7)
		{
			return false;
		}
		string b = Path.Combine(mod.RootDir, "1.6");
		string text = Path.Combine(mod.RootDir, "1.7");
		if (!Directory.Exists(text))
		{
			return false;
		}
		bool flag = false;
		for (int i = 0; i < mod.foldersToLoadDescendingOrder.Count; i++)
		{
			if (string.Equals(Path.GetFullPath(mod.foldersToLoadDescendingOrder[i]), b, StringComparison.OrdinalIgnoreCase))
			{
				mod.foldersToLoadDescendingOrder[i] = text;
				flag = true;
			}
		}
		if (flag)
		{
			Log.Message("MPCompat :: Reel's Turret Pipeline: corrected legacy LoadFolders selection to " + text + " before XML loading.");
		}
		return flag;
	}
}
