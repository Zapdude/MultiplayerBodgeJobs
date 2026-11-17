using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Mlie.MoreFactionInteraction")]
internal class MoreFactionInteraction
{
	public MoreFactionInteraction(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("MoreFactionInteraction.ChoiceLetter_ReverseTradeRequest");
		MethodInfo[] array = MpMethodUtil.GetLambda(type, "Choices", MethodType.Getter, null, 0, 3).ToArray();
		MP.RegisterSyncDelegate(type, array[0].DeclaringType.Name, array[0].Name);
		MP.RegisterSyncMethod(array[1]);
		MP.RegisterDefaultLetterChoice(array[1], type);
		string[] array2 = new string[3] { "MoreFactionInteraction.ChoiceLetter_DiplomaticMarriage", "MoreFactionInteraction.ChoiceLetter_ExtortionDemand", "MoreFactionInteraction.ChoiceLetter_MysticalShaman" };
		for (int i = 0; i < array2.Length; i++)
		{
			type = AccessTools.TypeByName(array2[i]);
			array = MpMethodUtil.GetLambda(type, "Choices", MethodType.Getter, null, 0, 1).ToArray();
			MP.RegisterSyncMethod(array[0]);
			MP.RegisterSyncMethod(array[1]);
			MP.RegisterDefaultLetterChoice(array[1], type);
		}
		array2 = new string[2] { "MoreFactionInteraction.FactionWarPeaceTalks", "MoreFactionInteraction.More_Flavour.AnnualExpo" };
		for (int i = 0; i < array2.Length; i++)
		{
			type = AccessTools.TypeByName(array2[i]);
			MP.RegisterSyncDialogNodeTree(type, "Notify_CaravanArrived");
		}
		type = AccessTools.TypeByName("MoreFactionInteraction.IncidentWorker_MysticalShaman");
		MP.RegisterSyncDialogNodeTree(type, "TryExecuteWorker");
		array2 = new string[3] { "MoreFactionInteraction.IncidentWorker_MysticalShaman", "MoreFactionInteraction.IncidentWorker_ReverseTradeRequest", "MoreFactionInteraction.IncidentWorker_RoadWorks" };
		for (int i = 0; i < array2.Length; i++)
		{
			type = AccessTools.TypeByName(array2[i]);
			MP.RegisterSyncDialogNodeTree(type, "TryExecuteWorker");
		}
		MP.RegisterSyncMethod(AccessTools.Method("MoreFactionInteraction.World_Incidents.WorldObjectComp_SettlementBumperCropComp:NotifyCaravanArrived"));
	}
}
