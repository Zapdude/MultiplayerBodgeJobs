using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("azuraal.choiceofpsycasts")]
public class ChoiceOfPsycastsCompat
{
	private static Type learnPsycastsType;

	private static AccessTools.FieldRef<object, int> levelField;

	private static AccessTools.FieldRef<object, Pawn> parentField;

	public ChoiceOfPsycastsCompat(ModContentPack mod)
	{
		Type type = (learnPsycastsType = AccessTools.TypeByName("ChoiceOfPsycasts.LearnPsycasts"));
		MpCompat.RegisterLambdaDelegate(type, "Choice", 1);
		MpCompat.RegisterLambdaDelegate(type, "ChoiceCustom", 1);
		levelField = AccessTools.FieldRefAccess<int>(type, "Level");
		parentField = AccessTools.FieldRefAccess<Pawn>(type, "Parent");
		MP.RegisterSyncWorker<Command_Action>(SyncLearnPsycasts, type);
	}

	private static void SyncLearnPsycasts(SyncWorker sync, ref Command_Action command)
	{
		if (sync.isWriting)
		{
			sync.Write(levelField(command));
			sync.Write(parentField(command));
		}
		else
		{
			command = (Command_Action)Activator.CreateInstance(learnPsycastsType, sync.Read<int>(), sync.Read<Pawn>());
		}
	}
}
