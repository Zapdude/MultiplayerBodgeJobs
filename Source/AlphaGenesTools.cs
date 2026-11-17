using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Multiplayer.Compat;

internal static class AlphaGenesTools
{
	private sealed class JobOwner
	{
		internal int Player = -1;

		internal System.WeakReference<Game> Game;
	}

	private sealed class Binding
	{
		internal Pawn Worker;

		internal Pawn Target;

		internal Thing Tool;

		internal int Job;

		internal int Revision;

		internal int Owner;

		internal int Kind;

		internal System.WeakReference<Game> Game;
	}

	private sealed class LocalState
	{
		internal readonly HashSet<(int worker, int tool, int job, int revision)> Presented = new HashSet<(int, int, int, int)>();

		internal readonly Dictionary<(int worker, int tool, int job), (int revision, int owner, Window window)> Windows = new Dictionary<(int, int, int), (int, int, Window)>();

		internal readonly List<(int worker, int tool, int job)> Remove = new List<(int, int, int)>();
	}

	private static readonly string[] Effects = new string[3] { "ChooseAndRemoveGene", "ArchoXenogenefier", "ArchoEndogenefier" };

	private static readonly string[] Jobs = new string[3] { "AG_UseArchoGeneRemover", "AG_UseArchoXenogenefier", "AG_UseArchoEndogenefier" };

	private static readonly string[] Dialogs = new string[3] { "ChooseAndRemoveGene", "EndogeneToXenogene", "XenogeneToEndogene" };

	private static readonly string[] Labels = new string[3] { "AG_ChooseGeneToRemove", "AG_ChooseEndogeneToConvert", "AG_ChooseXenogeneToConvert" };

	private static readonly string[] EmptyLabels = new string[3] { "AG_NoGenesAtAll", "AG_NoEndogenes", "AG_NoXenogenes" };

	private static Type[] effectTypes;

	private static Type[] dialogTypes;

	private static FieldInfo[] genesFields;

	private static Func<bool> replay;

	private static Func<bool> simulating;

	private static Func<bool> arbiter;

	private static FieldInfo settingsField;

	private static FieldInfo comaSetting;

	private static HediffDef coma;

	private static FastInvokeHandler targetParameters;

	private static readonly ConditionalWeakTable<Job, JobOwner> jobOwners = new ConditionalWeakTable<Job, JobOwner>();

	private static readonly ConditionalWeakTable<Window, Binding> windowBindings = new ConditionalWeakTable<Window, Binding>();

	private static readonly ConditionalWeakTable<Game, LocalState> local = new ConditionalWeakTable<Game, LocalState>();

	internal static bool Installed { get; private set; }

	private static List<AlphaGenesToolSelection> Records => Current.Game.GetComponent<AlphaGenesToolsComponent>().Pending;

	private static int LocalPlayer => SessionServices.LocalPlayer();

	internal static void Install()
	{
		effectTypes = Effects.Select((string text) => AccessTools.TypeByName("AlphaGenes.CompTargetEffect_" + text)).ToArray();
		dialogTypes = Dialogs.Select((string text) => AccessTools.TypeByName("AlphaMemes.Dialog_" + text)).ToArray();
		genesFields = dialogTypes.Select((Type type4) => AccessTools.DeclaredField(type4, "genes")).ToArray();
		Type type = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
		SessionServices.Install();
		replay = (Func<bool>)AccessTools.PropertyGetter(type, "IsReplay").CreateDelegate(typeof(Func<bool>));
		simulating = (Func<bool>)AccessTools.PropertyGetter("Multiplayer.Client.TickPatch:Simulating").CreateDelegate(typeof(Func<bool>));
		FieldInfo fieldInfo = AccessTools.DeclaredField(type, "arbiterInstance");
		AccessTools.FieldRef<bool> arbiterRef = AccessTools.StaticFieldRefAccess<bool>(fieldInfo);
		arbiter = () => arbiterRef();
		settingsField = AccessTools.DeclaredField("AlphaGenes.AlphaGenes_Mod:settings");
		comaSetting = AccessTools.DeclaredField(settingsField.FieldType, "AG_GeneRemovalComa");
		coma = DefDatabase<HediffDef>.GetNamed("AG_GeneRemovalComa");
		targetParameters = HarmonyLib.MethodInvoker.GetHandler(AccessTools.DeclaredMethod("AlphaGenes.CompTargetableHuman:GetTargetingParameters"));
		MP.RegisterSyncMethod(typeof(AlphaGenesTools), "SyncChoose").SetVersion(16).CancelIfAnyArgNull();
		MP.RegisterSyncMethod(typeof(AlphaGenesTools), "SyncClaim").SetVersion(16).CancelIfAnyArgNull();
		Type[] array = effectTypes;
		foreach (Type type2 in array)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "DoEffectOn"), new HarmonyMethod(typeof(AlphaGenesTools), "PrepareSelection"));
		}
		array = dialogTypes;
		foreach (Type type3 in array)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type3, "DoWindowContents"), null, null, new HarmonyMethod(typeof(AlphaGenesTools), "ChoiceButton"));
		}
		string[] array2 = new string[2] { "TryTakeOrderedJob", "TryTakeOrderedJobPrioritizedWork" };
		foreach (string name in array2)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), name), new HarmonyMethod(typeof(AlphaGenesTools), "RememberIssuer")
			{
				priority = 0
			});
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Job), "ExposeData"), null, new HarmonyMethod(typeof(AlphaGenesTools), "ExposeJobOwner"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn), "GetGizmos"), null, new HarmonyMethod(typeof(AlphaGenesTools), "SelectionGizmos"));
		SessionServices.RegisterLifecycle("sarg.alphagenes.tools", ForgetPlayer, StopUi);
		Installed = true;
	}

	private static bool ToolJob(Job job)
	{
		if (job?.def != null)
		{
			return Array.IndexOf(Jobs, job.def.defName) >= 0;
		}
		return false;
	}

	private static JobOwner Owner(Job job)
	{
		JobOwner value = jobOwners.GetValue(job, (Job _) => new JobOwner());
		if (value.Game == null || !value.Game.TryGetTarget(out var target) || target != Current.Game)
		{
			value.Game = new System.WeakReference<Game>(Current.Game);
			value.Player = -1;
		}
		return value;
	}

	private static void RememberIssuer(Job job)
	{
		if (MP.IsInMultiplayer && MP.IsExecutingSyncCommand && SessionServices.ExecutingPlayer() >= 0 && ToolJob(job))
		{
			Owner(job).Player = SessionServices.ExecutingPlayer();
		}
	}

	private static void ExposeJobOwner(Job __instance)
	{
		if (ToolJob(__instance))
		{
			bool flag = Scribe.mode == LoadSaveMode.LoadingVars && Scribe.loader.curXmlParent?["mpAlphaGenesToolIssuer"] != null;
			if (MP.IsInMultiplayer || flag || jobOwners.TryGetValue(__instance, out var _))
			{
				Scribe_Values.Look(ref Owner(__instance).Player, "mpAlphaGenesToolIssuer", -1);
			}
		}
	}

	private static int Kind(ThingComp effect)
	{
		return Array.IndexOf(effectTypes, effect.GetType());
	}

	private static List<Gene> Genes(Pawn target, int kind)
	{
		return kind switch
		{
			0 => target.genes.GenesListForReading, 
			1 => target.genes.Endogenes, 
			2 => target.genes.Xenogenes, 
			_ => null, 
		};
	}

	private static bool TargetAllowed(ThingWithComps tool, Pawn target)
	{
		if (target?.genes == null)
		{
			return false;
		}
		CompTargetable compTargetable = tool.AllComps.OfType<CompTargetable>().FirstOrDefault();
		if (compTargetable != null)
		{
			return ((TargetingParameters)targetParameters(compTargetable)).validator(target);
		}
		return false;
	}

	private static bool Available(Pawn worker, Thing tool, Pawn target, int kind)
	{
		bool flag = ((kind < 0 || kind > 2) ? true : false);
		if (flag || worker == null || worker.Dead || worker.Downed || !worker.Spawned || !worker.IsColonistPlayerControlled || tool == null || tool.Destroyed || tool.stackCount != 1 || target == null || target.Destroyed || target.Dead || !target.Spawned || target.Map != worker.Map || !(tool is ThingWithComps thingWithComps) || !TargetAllowed(thingWithComps, target) || !thingWithComps.AllComps.Any((ThingComp comp) => comp.GetType() == effectTypes[kind]))
		{
			return false;
		}
		if (worker.carryTracker?.CarriedThing == tool || (tool.Spawned && tool.Map == worker.Map && worker.CanReserveAndReach(tool, PathEndMode.Touch, Danger.Deadly)))
		{
			return worker.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly);
		}
		return false;
	}

	private static bool PrepareSelection(ThingComp __instance, Pawn user, Thing target)
	{
		if (!MP.IsInMultiplayer)
		{
			return true;
		}
		if (!MP.InInterface && target is Pawn pawn)
		{
			Job job = user?.CurJob;
			if (job != null && ToolJob(job) && job.loadID >= 0)
			{
				int num = Kind(__instance);
				if (!Available(user, __instance.parent, pawn, num))
				{
					return false;
				}
				if (Genes(pawn, num).Count == 0)
				{
					Messages.Message(EmptyLabels[num].Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
					return false;
				}
				if (Records.FirstOrDefault((AlphaGenesToolSelection record) => record.Worker == user && record.Tool == __instance.parent && record.Job == job.loadID) != null)
				{
					return false;
				}
				Records.RemoveAll((AlphaGenesToolSelection record) => record.Tool == __instance.parent);
				Records.Add(new AlphaGenesToolSelection
				{
					Worker = user,
					Target = pawn,
					Tool = __instance.parent,
					Job = job.loadID,
					Kind = num,
					Owner = Owner(job).Player
				});
				return false;
			}
		}
		return false;
	}

	private static AlphaGenesToolSelection Pending(Pawn worker, Thing tool, Pawn target, int job, int revision, int owner, int kind)
	{
		if (Current.Game != null)
		{
			return Records.FirstOrDefault((AlphaGenesToolSelection record) => record.Worker == worker && record.Tool == tool && record.Target == target && record.Job == job && SessionServices.Matches(record.Ownership, owner, revision) && record.Kind == kind);
		}
		return null;
	}

	private static bool CanIssue(AlphaGenesToolSelection record)
	{
		if (record != null && MP.IsInMultiplayer && MP.IsExecutingSyncCommand && SessionServices.ExecutingPlayer() >= 0 && record.Worker?.Faction == Faction.OfPlayer)
		{
			return Available(record.Worker, record.Tool, record.Target, record.Kind);
		}
		return false;
	}

	private static void SyncChoose(Pawn worker, Thing tool, Pawn target, int jobId, int revision, int expectedOwner, int kind, GeneDef gene)
	{
		AlphaGenesToolSelection alphaGenesToolSelection = Pending(worker, tool, target, jobId, revision, expectedOwner, kind);
		if (!CanIssue(alphaGenesToolSelection) || !SessionServices.CanChoose(alphaGenesToolSelection.Ownership, expectedOwner, revision) || gene == null)
		{
			return;
		}
		Gene gene2 = Genes(target, kind).FirstOrDefault((Gene item) => item.def == gene);
		if (gene2 == null || target.health.hediffSet.HasHediff(coma))
		{
			return;
		}
		Records.Remove(alphaGenesToolSelection);
		PatchingUtilities.TimeSnapshot? andSetFromMap = PatchingUtilities.TimeSnapshot.GetAndSetFromMap(worker.Map);
		try
		{
			for (int num = 0; num < 20; num++)
			{
				CellFinder.TryFindRandomReachableCellNearPosition(target.Position, target.Position, target.Map, 2f, TraverseParms.For(TraverseMode.NoPassClosedDoors), null, null, out var result);
				FilthMaker.TryMakeFilth(result, target.Map, ThingDefOf.Filth_Blood);
			}
			if ((bool)comaSetting.GetValue(settingsField.GetValue(null)))
			{
				target.health.AddHediff(coma);
			}
			target.genes.RemoveGene(gene2);
			if (kind != 0)
			{
				target.genes.AddGene(gene, kind == 1);
			}
			tool.Destroy();
		}
		finally
		{
			andSetFromMap?.Set();
		}
	}

	private static void SyncClaim(Pawn worker, Thing tool, Pawn target, int jobId, int revision, int expectedOwner, int kind)
	{
		AlphaGenesToolSelection alphaGenesToolSelection = Pending(worker, tool, target, jobId, revision, expectedOwner, kind);
		if (CanIssue(alphaGenesToolSelection))
		{
			SessionServices.TryClaim(alphaGenesToolSelection.Ownership, expectedOwner, revision);
		}
	}

	private static void ForgetPlayer(int playerId)
	{
		if (!MP.IsInMultiplayer || Current.Game == null)
		{
			return;
		}
		foreach (KeyValuePair<Job, JobOwner> item in (IEnumerable<KeyValuePair<Job, JobOwner>>)jobOwners)
		{
			if (item.Value.Player == playerId && item.Value.Game != null && item.Value.Game.TryGetTarget(out var target) && target == Current.Game)
			{
				item.Value.Player = -1;
			}
		}
		foreach (AlphaGenesToolSelection record in Records)
		{
			SessionServices.Retire(record.Ownership, playerId);
		}
	}

	private static bool MayPresent(AlphaGenesToolSelection record)
	{
		if (record.Owner >= 0 && record.Owner == LocalPlayer && record.Worker?.Faction == MP.RealPlayerFaction && record.Target?.genes != null)
		{
			Thing tool = record.Tool;
			if (tool == null)
			{
				return false;
			}
			return !tool.Destroyed;
		}
		return false;
	}

	internal static void Pump()
	{
		if (!Installed || !MP.IsInMultiplayer || !MP.InInterface || Current.Game == null || replay() || simulating() || arbiter())
		{
			return;
		}
		List<AlphaGenesToolSelection> records = Records;
		if (records.Count == 0 && !local.TryGetValue(Current.Game, out var _))
		{
			return;
		}
		LocalState orCreateValue = local.GetOrCreateValue(Current.Game);
		orCreateValue.Remove.Clear();
		foreach (KeyValuePair<(int, int, int), (int, int, Window)> pair in orCreateValue.Windows)
		{
			AlphaGenesToolSelection alphaGenesToolSelection = records.FirstOrDefault((AlphaGenesToolSelection item) => item.Worker?.thingIDNumber == pair.Key.Item1 && item.Tool?.thingIDNumber == pair.Key.Item2 && item.Job == pair.Key.Item3);
			if (alphaGenesToolSelection != null && alphaGenesToolSelection.Revision == pair.Value.Item1 && alphaGenesToolSelection.Owner == pair.Value.Item2 && MayPresent(alphaGenesToolSelection) && Find.WindowStack.Windows.Contains(pair.Value.Item3))
			{
				continue;
			}
			using (SessionServices.EnterLocalOnly())
			{
				if (Find.WindowStack.Windows.Contains(pair.Value.Item3))
				{
					pair.Value.Item3.Close();
				}
			}
			orCreateValue.Remove.Add(pair.Key);
		}
		foreach (var item in orCreateValue.Remove)
		{
			orCreateValue.Windows.Remove(item);
		}
		foreach (AlphaGenesToolSelection item2 in records)
		{
			if (!MayPresent(item2))
			{
				continue;
			}
			(int, int, int) key = (item2.Worker.thingIDNumber, item2.Tool.thingIDNumber, item2.Job);
			if (!orCreateValue.Presented.Add((key.Item1, key.Item2, key.Item3, item2.Revision)))
			{
				continue;
			}
			using (SessionServices.EnterLocalOnly())
			{
				try
				{
					Window window = (Window)Activator.CreateInstance(dialogTypes[item2.Kind], item2.Target, item2.Tool);
					windowBindings.Add(window, new Binding
					{
						Worker = item2.Worker,
						Target = item2.Target,
						Tool = item2.Tool,
						Job = item2.Job,
						Revision = item2.Revision,
						Owner = item2.Owner,
						Kind = item2.Kind,
						Game = new System.WeakReference<Game>(Current.Game)
					});
					orCreateValue.Windows[key] = (item2.Revision, item2.Owner, window);
					Find.WindowStack.Add(window);
				}
				catch (Exception value2)
				{
					Log.Error($"Multiplayer: Alpha Genes tool window failed: {value2}");
				}
			}
		}
	}

	private static IEnumerable<Gizmo> SelectionGizmos(IEnumerable<Gizmo> __result, Pawn __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		if (!MP.IsInMultiplayer || !MP.InInterface || Current.Game == null || __instance.Faction != MP.RealPlayerFaction)
		{
			yield break;
		}
		foreach (AlphaGenesToolSelection record in Records)
		{
			if (record.Worker == __instance && !(record.Tool?.Destroyed ?? true) && record.Target?.genes != null)
			{
				Binding binding = new Binding
				{
					Worker = record.Worker,
					Tool = record.Tool,
					Target = record.Target,
					Job = record.Job,
					Revision = record.Revision,
					Owner = record.Owner,
					Kind = record.Kind
				};
				yield return new Command_Action
				{
					defaultLabel = Labels[record.Kind].Translate(),
					defaultDesc = record.Tool.LabelCap,
					action = () =>
					{
						SyncClaim(binding.Worker, binding.Tool, binding.Target, binding.Job, binding.Revision, binding.Owner, binding.Kind);
					}
				};
			}
		}
	}

	private static bool PickButton(Rect rect, bool sound, Window window, GeneDef gene)
	{
		bool flag = Widgets.ButtonInvisible(rect, sound);
		bool flag2 = windowBindings.TryGetValue(window, out var value);
		if (!MP.IsInMultiplayer && !flag2)
		{
			return flag;
		}
		if (flag & flag2)
		{
			if (MP.IsInMultiplayer && value.Game != null && value.Game.TryGetTarget(out var target) && target == Current.Game)
			{
				SyncChoose(value.Worker, value.Tool, value.Target, value.Job, value.Revision, value.Owner, value.Kind, gene);
			}
			window.Close();
		}
		return false;
	}

	private static void StopUi()
	{
		foreach (KeyValuePair<Game, LocalState> item in (IEnumerable<KeyValuePair<Game, LocalState>>)local)
		{
			foreach (var value2 in item.Value.Windows.Values)
			{
				using (SessionServices.EnterLocalOnly())
				{
					try
					{
						if (Find.WindowStack.Windows.Contains(value2.window))
						{
							value2.window.Close();
						}
					}
					catch (Exception value)
					{
						Log.Error($"Multiplayer: Alpha Genes tool window cleanup failed: {value}");
					}
				}
			}
		}
		local.Clear();
	}

	private static IEnumerable<CodeInstruction> ChoiceButton(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		List<CodeInstruction> list = instructions.ToList();
		FieldInfo genes = genesFields[Array.IndexOf(dialogTypes, __originalMethod.DeclaringType)];
		MethodInfo item = AccessTools.PropertyGetter(typeof(List<GeneDef>), "Item");
		MethodInfo method = AccessTools.PropertyGetter(typeof(GeneDef), "Icon");
		MethodInfo button = AccessTools.DeclaredMethod(typeof(Widgets), "ButtonInvisible", new Type[2]
		{
			typeof(Rect),
			typeof(bool)
		});
		CodeInstruction index = null;
		for (int i = 2; i < list.Count; i++)
		{
			if (list[i].Calls(method) && list[i - 1].Calls(item) && list[i - 2].IsLdloc())
			{
				index = list[i - 2];
			}
		}
		if (index == null)
		{
			throw new InvalidOperationException("Alpha Genes tool gene-index contract changed: " + __originalMethod);
		}
		int count = 0;
		foreach (CodeInstruction item2 in list)
		{
			if (!item2.Calls(button))
			{
				yield return item2;
				continue;
			}
			count++;
			yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(item2).MoveBlocksFrom(item2);
			yield return new CodeInstruction(OpCodes.Ldarg_0);
			yield return new CodeInstruction(OpCodes.Ldfld, genes);
			yield return new CodeInstruction(index.opcode, index.operand);
			yield return new CodeInstruction(OpCodes.Callvirt, item);
			yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(AlphaGenesTools), "PickButton"));
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"Alpha Genes tool choice button contract changed: {count}");
		}
	}
}
