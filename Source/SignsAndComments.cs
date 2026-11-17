using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Dark.Signs")]
public class SignsAndComments
{
	private static ConstructorInfo renameSignDialogCtor;

	private static AccessTools.FieldRef<object, Color> dialogColorField;

	private static AccessTools.FieldRef<object, ThingComp> dialogSignCompField;

	public SignsAndComments(ModContentPack mod)
	{
		MpCompat.RegisterLambdaDelegate(AccessTools.TypeByName("Dark.Signs.CommentContentClipboard"), "CopyPasteGizmosFor", 0, 1);
		Type type = AccessTools.TypeByName("Dark.Signs.Comp_Sign");
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", 1, 2, 4);
		MpCompat.harmony.Patch(AccessTools.PropertyGetter(type, "editOnPlacement"), null, new HarmonyMethod(typeof(SignsAndComments), "CancelAutoOpenNotTargetedAtMe"));
		Type type2 = AccessTools.TypeByName("Dark.Signs.Dialog_RenameSign");
		renameSignDialogCtor = AccessTools.DeclaredConstructor(type2, new Type[1] { type });
		dialogColorField = AccessTools.FieldRefAccess<Color>(type2, "curColor");
		dialogSignCompField = AccessTools.FieldRefAccess<ThingComp>(type2, "signComp");
		MP.RegisterSyncMethod(type2, "RenamableLabel");
		MP.RegisterSyncWorker<object>(SyncRenameSignDialog, type2);
	}

	private static void SyncRenameSignDialog(SyncWorker sync, ref object dialog)
	{
		if (sync.isWriting)
		{
			sync.Write(dialogSignCompField(dialog));
			sync.Write(dialogColorField(dialog));
			return;
		}
		ThingComp thingComp = sync.Read<ThingComp>();
		Color color = sync.Read<Color>();
		dialog = renameSignDialogCtor.Invoke(new object[1] { thingComp });
		dialogColorField(dialog) = color;
	}

	private static void CancelAutoOpenNotTargetedAtMe(ref bool __result)
	{
		if (__result && MP.IsInMultiplayer && !MP.IsExecutingSyncCommandIssuedBySelf)
		{
			__result = false;
		}
	}
}
