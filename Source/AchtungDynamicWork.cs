using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

internal static class AchtungDynamicWork
{
	private sealed class SchemaScope : IDisposable
	{
		internal readonly WorkTypeDef PreviousDefinition;

		private readonly bool previousDontSync;

		internal SchemaScope()
		{
			PreviousDefinition = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Rescuing");
			previousDontSync = (bool)dontSyncField.GetValue(null);
			dontSyncField.SetValue(null, true);
		}

		public void Dispose()
		{
			dontSyncField.SetValue(null, previousDontSync);
		}
	}

	private sealed class LoadState
	{
		internal object Settings;

		internal bool PreviousRescueEnabled;

		internal bool PreviousRescuePresent;
	}

	private const string RescueName = "Rescuing";

	private static FieldInfo settingsField;

	private static FieldInfo rescueEnabledField;

	private static FieldInfo dontSyncField;

	private static MethodInfo toggleRescue;

	private static MethodInfo giveShortHash;

	private static Dictionary<Type, HashSet<ushort>> takenHashes;

	private static Dictionary<ushort, WorkTypeDef> shortHashLookup;

	private static ushort reservedHash;

	private static int addingRescue;

	private static int removingRescue;

	private static bool installed;

	public static void Install()
	{
		if (installed)
		{
			return;
		}
		Type type = AccessTools.TypeByName("AchtungMod.Achtung");
		Type type2 = AccessTools.TypeByName("AchtungMod.AchtungSettings");
		Type type3 = AccessTools.TypeByName("Brrainz.DynamicWorkTypes");
		settingsField = AccessTools.DeclaredField(type, "Settings");
		rescueEnabledField = AccessTools.DeclaredField(type2, "rescueEnabled");
		toggleRescue = AccessTools.DeclaredMethod(type2, "ToggleRescue");
		giveShortHash = AccessTools.DeclaredMethod(typeof(ShortHashGiver), "GiveShortHash");
		takenHashes = (Dictionary<Type, HashSet<ushort>>)AccessTools.DeclaredField(typeof(ShortHashGiver), "takenHashesPerDeftype").GetValue(null);
		shortHashLookup = (Dictionary<ushort, WorkTypeDef>)AccessTools.DeclaredField(typeof(DefDatabase<WorkTypeDef>), "defsByShortHash").GetValue(null);
		dontSyncField = AccessTools.DeclaredField(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "dontSync");
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type3, "AddWorkTypeDef"), new HarmonyMethod(typeof(AchtungDynamicWork), "AddBefore"), null, new HarmonyMethod(typeof(AchtungDynamicWork), "AddTranspiler"), new HarmonyMethod(typeof(AchtungDynamicWork), "AddAfter"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type3, "RemoveWorkTypeDef"), new HarmonyMethod(typeof(AchtungDynamicWork), "RemoveBefore"), null, null, new HarmonyMethod(typeof(AchtungDynamicWork), "RemoveAfter"));
		MethodInfo removeValue = AccessTools.DeclaredMethod(typeof(List<int>), "Remove");
		MethodInfo original = (from m in type3.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(AccessTools.GetDeclaredMethods)
			where m.Name.StartsWith("<UpdatePawns>", StringComparison.Ordinal) && m.GetMethodBody() != null
			where PatchProcessor.GetOriginalInstructions(m).Any((CodeInstruction i) => i.Calls(removeValue))
			select m).Single();
		MpCompat.harmony.Patch(original, null, null, new HarmonyMethod(typeof(AchtungDynamicWork), "RemovePriorityTranspiler"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(World), "ExposeData"), new HarmonyMethod(typeof(AchtungDynamicWork), "WorldBefore")
		{
			priority = 800
		}, null, null, new HarmonyMethod(typeof(AchtungDynamicWork), "WorldAfter"));
		installed = true;
	}

	private static bool IsRescue(WorkTypeDef def)
	{
		return def?.defName == "Rescuing";
	}

	private static void AddBefore(WorkTypeDef def, out SchemaScope __state)
	{
		__state = ((MP.IsInMultiplayer && IsRescue(def)) ? new SchemaScope() : null);
		if (__state != null)
		{
			addingRescue++;
		}
	}

	private static Exception AddAfter(Exception __exception, SchemaScope __state)
	{
		if (__state != null)
		{
			addingRescue--;
			__state.Dispose();
		}
		return __exception;
	}

	private static void RegisterWorkType(WorkTypeDef def)
	{
		DefDatabase<WorkTypeDef>.Add(def);
		if (addingRescue > 0 && IsRescue(def))
		{
			CompleteIdentity(def);
		}
	}

	private static IEnumerable<CodeInstruction> AddTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo add = AccessTools.DeclaredMethod(typeof(DefDatabase<WorkTypeDef>), "Add", new Type[1] { typeof(WorkTypeDef) });
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(add))
			{
				yield return instruction;
				continue;
			}
			count++;
			yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(AchtungDynamicWork), "RegisterWorkType")).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Achtung dynamic work registration changed: " + count);
		}
	}

	private static void RemoveBefore(ref WorkTypeDef def, out SchemaScope __state)
	{
		__state = ((MP.IsInMultiplayer && IsRescue(def)) ? new SchemaScope() : null);
		if (__state != null)
		{
			if (__state.PreviousDefinition != null)
			{
				def = __state.PreviousDefinition;
			}
			removingRescue++;
		}
	}

	private static bool RemovePriorityAt(List<int> values, int index)
	{
		if (removingRescue == 0)
		{
			return values.Remove(index);
		}
		values.RemoveAt(index);
		return true;
	}

	private static IEnumerable<CodeInstruction> RemovePriorityTranspiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo remove = AccessTools.DeclaredMethod(typeof(List<int>), "Remove");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (!instruction.Calls(remove))
			{
				yield return instruction;
				continue;
			}
			count++;
			yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(AchtungDynamicWork), "RemovePriorityAt")).WithLabels(instruction.labels).WithBlocks(instruction.blocks);
		}
		if (count != 1)
		{
			throw new InvalidOperationException("Achtung work priority column removal changed: " + count);
		}
	}

	private static Exception RemoveAfter(Exception __exception, SchemaScope __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		try
		{
			WorkTypeDef previousDefinition = __state.PreviousDefinition;
			if (previousDefinition != null && DefDatabase<WorkTypeDef>.GetNamedSilentFail("Rescuing") != previousDefinition && shortHashLookup.TryGetValue(previousDefinition.shortHash, out var value) && value == previousDefinition)
			{
				shortHashLookup.Remove(previousDefinition.shortHash);
			}
		}
		finally
		{
			removingRescue--;
			__state.Dispose();
		}
		return __exception;
	}

	private static void CompleteIdentity(WorkTypeDef def)
	{
		if (DefDatabase<WorkTypeDef>.GetNamedSilentFail("Rescuing") != def || !takenHashes.TryGetValue(typeof(WorkTypeDef), out var value))
		{
			return;
		}
		if (def.shortHash == 0)
		{
			if (reservedHash != 0)
			{
				if (DefDatabase<WorkTypeDef>.AllDefsListForReading.Any((WorkTypeDef d) => d != def && d.shortHash == reservedHash))
				{
					throw new InvalidOperationException("Achtung Rescuing short hash is already used by another work type");
				}
				def.shortHash = reservedHash;
				value.Add(reservedHash);
			}
			else
			{
				giveShortHash.Invoke(null, new object[3]
				{
					def,
					typeof(WorkTypeDef),
					value
				});
				reservedHash = def.shortHash;
			}
		}
		else if (reservedHash == 0)
		{
			reservedHash = def.shortHash;
		}
		if (shortHashLookup.TryGetValue(def.shortHash, out var value2) && value2 != def && !IsRescue(value2))
		{
			throw new InvalidOperationException("Achtung Rescuing short hash lookup conflicts with another work type");
		}
		shortHashLookup[def.shortHash] = def;
		if (shortHashLookup.TryGetValue(0, out var value3) && value3 == def)
		{
			shortHashLookup.Remove(0);
		}
	}

	private static bool TrySharedRescue(XmlNode worldNode, out bool enabled)
	{
		enabled = false;
		XmlNode xmlNode = worldNode?.SelectSingleNode("components/li[@Class='AchtungMod.AchtungMultiplayerState']");
		if (bool.TryParse(xmlNode?["initialized"]?.InnerText, out var result) & result)
		{
			return bool.TryParse(xmlNode?["rescueEnabled"]?.InnerText, out enabled);
		}
		return false;
	}

	private static void WorldBefore(out LoadState __state)
	{
		__state = null;
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		if (Scribe.mode == LoadSaveMode.LoadingVars && TrySharedRescue(Scribe.loader.curXmlParent, out var enabled))
		{
			object value = settingsField.GetValue(null);
			__state = new LoadState
			{
				Settings = value,
				PreviousRescueEnabled = (bool)rescueEnabledField.GetValue(value),
				PreviousRescuePresent = (DefDatabase<WorkTypeDef>.GetNamedSilentFail("Rescuing") != null)
			};
			rescueEnabledField.SetValue(value, enabled);
			using (new SchemaScope())
			{
				toggleRescue.Invoke(null, new object[1] { enabled });
			}
		}
		LoadSaveMode mode = Scribe.mode;
		if (mode - 1 <= LoadSaveMode.Saving)
		{
			WorkTypeDef namedSilentFail = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Rescuing");
			if (namedSilentFail != null)
			{
				CompleteIdentity(namedSilentFail);
			}
		}
	}

	private static Exception WorldAfter(Exception __exception, LoadState __state)
	{
		if (__exception != null && __state != null)
		{
			try
			{
				using (new SchemaScope())
				{
					toggleRescue.Invoke(null, new object[1] { __state.PreviousRescuePresent });
				}
			}
			catch (Exception ex)
			{
				return new AggregateException("Achtung world loading and rescue declaration rollback failed", __exception, ex);
			}
			finally
			{
				rescueEnabledField.SetValue(__state.Settings, __state.PreviousRescueEnabled);
			}
		}
		return __exception;
	}
}
