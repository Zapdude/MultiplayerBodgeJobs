using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

internal static class VanillaGravshipExpanded2Workshop
{
	private sealed class Replacement
	{
		public MethodInfo Body;

		public Type InstanceFilter;
	}

	private static Assembly bodies;

	private static readonly Dictionary<MethodBase, Replacement> replacements = new Dictionary<MethodBase, Replacement>();

	private static bool installed;

	internal static MethodInfo ResolveTributeDecision => Helper("WorldComponent_GravshipCombat", "ResolveTributeDecision");

	internal static Type EscapeMenuBody => bodies.GetType("Multiplayer.Compat.Workshop17.Bodies.CompEscapePod_Body", throwOnError: true);

	private static MethodInfo Helper(string type, string method)
	{
		return AccessTools.DeclaredMethod(bodies.GetType("Multiplayer.Compat.Workshop17.Bodies." + type + "_Body", throwOnError: true), method);
	}

	internal static void Install()
	{
		if (installed)
		{
			return;
		}
		bodies = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault((Assembly a) => a.GetName().Name == "Multiplayer_Gravship2Overrides") ?? throw new InvalidOperationException("Ship2 Workshop compatibility body module was not loaded");
		Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().Single((Assembly a) => a.GetName().Name == "VanillaGravshipExpanded2");
		Dictionary<MethodBase, Replacement> dictionary = new Dictionary<MethodBase, Replacement>();
		MethodInfo methodInfo = null;
		using (StreamReader streamReader = new StreamReader(bodies.GetManifestResourceStream("Workshop17.replacements.txt") ?? throw new InvalidOperationException("Missing Ship2 compatibility manifest")))
		{
			string text;
			while ((text = streamReader.ReadLine()) != null)
			{
				string[] array = text.Split('\t');
				MethodInfo body = AccessTools.DeclaredMethod(bodies.GetType(array[3], throwOnError: true), array[4]);
				Type type = assembly.GetType(array[1], throwOnError: true);
				bool flag = array[5] == "1";
				MethodInfo methodInfo2 = ResolveOriginal(type, array[2], body, flag);
				if (type.FullName == "VanillaGravshipExpanded2.JobGiver_AIFightFires" && array[2] == "TryGiveJob" && AccessTools.DeclaredField(type, "FireDefs") == null)
				{
					methodInfo = methodInfo2;
					continue;
				}
				dictionary.Add(methodInfo2, new Replacement
				{
					Body = body,
					InstanceFilter = (flag ? type : null)
				});
			}
		}
		foreach (KeyValuePair<MethodBase, Replacement> item in dictionary)
		{
			replacements.Add(item.Key, item.Value);
		}
		List<MethodBase> list = new List<MethodBase>();
		HarmonyMethod harmonyMethod = new HarmonyMethod(typeof(VanillaGravshipExpanded2Workshop), "UseReviewedBody");
		HarmonyMethod harmonyMethod2 = new HarmonyMethod(typeof(VanillaGravshipExpanded2Workshop), "UseStableNativeFireOrder");
		try
		{
			foreach (MethodBase key in replacements.Keys)
			{
				MpCompat.harmony.Patch(key, null, null, harmonyMethod);
				list.Add(key);
			}
			if (methodInfo != null)
			{
				MpCompat.harmony.Patch(methodInfo, null, null, harmonyMethod2);
			}
			installed = true;
		}
		catch
		{
			foreach (MethodBase item2 in list)
			{
				MpCompat.harmony.Unpatch(item2, harmonyMethod.method);
			}
			if (methodInfo != null)
			{
				MpCompat.harmony.Unpatch(methodInfo, harmonyMethod2.method);
			}
			replacements.Clear();
			throw;
		}
	}

	private static IOrderedEnumerable<Thing> StableFireOrder(IOrderedEnumerable<Thing> order)
	{
		if (!MP.IsInMultiplayer)
		{
			return order;
		}
		return order.ThenBy((Thing thing) => thing.thingIDNumber);
	}

	private static IEnumerable<CodeInstruction> UseStableNativeFireOrder(IEnumerable<CodeInstruction> instructions)
	{
		int orders = 0;
		int customDefs = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			yield return instruction;
			if (instruction.operand is MethodInfo methodInfo)
			{
				if (methodInfo.Name == "get_AllDefsListForReading" && methodInfo.DeclaringType.IsGenericType && methodInfo.DeclaringType.GetGenericTypeDefinition() == typeof(DefDatabase<>) && methodInfo.DeclaringType.GetGenericArguments()[0].FullName == "VEF.Things.CustomFireDef")
				{
					customDefs++;
				}
				if (methodInfo.DeclaringType == typeof(Enumerable) && methodInfo.Name == "OrderBy" && methodInfo.IsGenericMethod && methodInfo.GetGenericArguments().SequenceEqual(new Type[2]
				{
					typeof(Thing),
					typeof(int)
				}, null))
				{
					orders++;
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VanillaGravshipExpanded2Workshop), "StableFireOrder"));
				}
			}
		}
		if (orders != 2 || customDefs != 1)
		{
			throw new InvalidOperationException($"Unsupported Ship2 CustomFire job shape: OrderBy={orders}, CustomFireDefs={customDefs}");
		}
	}

	private static MethodInfo ResolveOriginal(Type nativeType, string name, MethodInfo body, bool inherited)
	{
		if (body == null || !body.IsStatic || body.ContainsGenericParameters)
		{
			throw new InvalidOperationException("Invalid Ship2 compatibility body: " + body);
		}
		Type[] second = (from p in body.GetParameters()
			select p.ParameterType).ToArray();
		List<MethodInfo> list = new List<MethodInfo>();
		Type type = nativeType;
		while (type != null)
		{
			MethodInfo[] methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo in methods)
			{
				if (!(methodInfo.Name != name) && !methodInfo.ContainsGenericParameters && !(methodInfo.ReturnType != body.ReturnType))
				{
					List<Type> list2 = (from p in methodInfo.GetParameters()
						select p.ParameterType).ToList();
					if (!methodInfo.IsStatic)
					{
						list2.Insert(0, nativeType);
					}
					if (list2.SequenceEqual(second))
					{
						list.Add(methodInfo);
					}
				}
			}
			type = (inherited ? type.BaseType : null);
		}
		if (list.Count != 1)
		{
			throw new MissingMethodException($"Ship2 Workshop signature changed: {nativeType}:{name} ({list.Count} matches)");
		}
		return list[0];
	}

	private static IEnumerable<CodeInstruction> UseReviewedBody(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
	{
		Replacement replacement = replacements[__originalMethod];
		List<CodeInstruction> original = instructions.ToList();
		Label nativePath = generator.DefineLabel();
		original[0].labels.Add(nativePath);
		yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(MP), "IsInMultiplayer"));
		yield return new CodeInstruction(OpCodes.Brfalse, nativePath);
		if (replacement.InstanceFilter != null)
		{
			yield return new CodeInstruction(OpCodes.Ldarg_0);
			yield return new CodeInstruction(OpCodes.Isinst, replacement.InstanceFilter);
			yield return new CodeInstruction(OpCodes.Brfalse, nativePath);
		}
		int count = __originalMethod.GetParameters().Length + ((!__originalMethod.IsStatic) ? 1 : 0);
		for (int i = 0; i < count; i++)
		{
			yield return new CodeInstruction(OpCodes.Ldarg, i);
			if (i == 0 && replacement.InstanceFilter != null)
			{
				yield return new CodeInstruction(OpCodes.Castclass, replacement.InstanceFilter);
			}
		}
		yield return new CodeInstruction(OpCodes.Call, replacement.Body);
		yield return new CodeInstruction(OpCodes.Ret);
		foreach (CodeInstruction item in original)
		{
			yield return item;
		}
	}
}
