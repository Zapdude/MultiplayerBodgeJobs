using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Multiplayer.Compat;

public static class MpMethodUtil
{
	private const string DisplayClassPrefix = "<>c__DisplayClass";

	private const string SharedDisplayClass = "<>c";

	private const string LambdaMethodInfix = "b__";

	private const string LocalFunctionInfix = "g__";

	private const string EnumerableStateMachineInfix = "d__";

	public static IEnumerable<MethodInfo> GetLambda(Type parentType, string parentMethod = null, MethodType parentMethodType = MethodType.Normal, Type[] parentArgs = null, params int[] lambdaOrdinals)
	{
		return GetLambdaGeneric(parentType, parentMethod, parentMethodType, parentArgs, null, null, lambdaOrdinals);
	}

	public static IEnumerable<MethodInfo> GetLambdaGeneric(Type parentType, string parentMethod = null, MethodType parentMethodType = MethodType.Normal, Type[] parentArgs = null, Type[] genericParentMethodArgs = null, Type[] genericNestedTypeArgs = null, params int[] lambdaOrdinals)
	{
		foreach (int lambdaOrdinal in lambdaOrdinals)
		{
			yield return GetLambdaGeneric(parentType, parentMethod, parentMethodType, parentArgs, genericParentMethodArgs, genericNestedTypeArgs, lambdaOrdinal);
		}
	}

	public static MethodInfo GetLambda(Type parentType, string parentMethod = null, MethodType parentMethodType = MethodType.Normal, Type[] parentArgs = null, int lambdaOrdinal = 0)
	{
		return GetLambdaGeneric(parentType, parentMethod, parentMethodType, parentArgs, null, null, lambdaOrdinal);
	}

	public static MethodInfo GetLambdaGeneric(Type parentType, string parentMethod = null, MethodType parentMethodType = MethodType.Normal, Type[] parentArgs = null, Type[] genericParentMethodArgs = null, Type[] genericNestedTypeArgs = null, int lambdaOrdinal = 0)
	{
		MethodBase methodBase = GetMethod(parentType, parentMethod, parentMethodType, parentArgs);
		if (methodBase == null)
		{
			throw new Exception($"Couldn't find parent method ({parentMethodType}) {parentType}::{parentMethod}");
		}
		if (genericParentMethodArgs != null && methodBase is MethodInfo methodInfo)
		{
			methodBase = methodInfo.MakeGenericMethod(genericParentMethodArgs);
		}
		int methodDebugId = GetMethodDebugId(methodBase);
		string displayClassPrefix = $"{"<>c__DisplayClass"}{methodDebugId}_";
		string lambdaNameShort = $"<{methodBase.Name}>{"b__"}{lambdaOrdinal}";
		MethodInfo methodInfo2 = (from t in parentType.GetNestedTypes(AccessTools.all).Where((Type t) =>
			{
				if (genericNestedTypeArgs != null && genericNestedTypeArgs.Length > 0)
				{
					if (!t.IsGenericType)
					{
						return false;
					}
					if (t.GetGenericArguments().Length != genericNestedTypeArgs.Length)
					{
						return false;
					}
				}
				return t.Name.StartsWith(displayClassPrefix);
			})
			select (genericNestedTypeArgs != null) ? t.MakeGenericType(genericNestedTypeArgs) : t).SelectMany(AccessTools.GetDeclaredMethods).FirstOrDefault((MethodInfo m) => m.Name == lambdaNameShort);
		string name = $"<{methodBase.Name}>{"b__"}{methodDebugId}_{lambdaOrdinal}";
		if ((object)methodInfo2 == null)
		{
			methodInfo2 = AccessTools.Method(parentType, name);
		}
		if (methodInfo2 == null)
		{
			Type type = AccessTools.Inner(parentType, "<>c");
			if ((object)type != null)
			{
				methodInfo2 = AccessTools.Method(type, name);
			}
		}
		if (methodInfo2 == null)
		{
			throw new Exception($"Couldn't find lambda {lambdaOrdinal} in parent method {parentType}::{methodBase.Name} (parent method id: {methodDebugId})");
		}
		return methodInfo2;
	}

	public static MethodInfo GetLocalFunc(Type parentType, string parentMethod = null, MethodType parentMethodType = MethodType.Normal, Type[] parentArgs = null, string localFunc = null)
	{
		return GetLocalFuncGeneric(parentType, parentMethod, parentMethodType, parentArgs, null, null, localFunc);
	}

	public static MethodInfo GetLocalFuncGeneric(Type parentType, string parentMethod = null, MethodType parentMethodType = MethodType.Normal, Type[] parentArgs = null, Type[] genericParentMethodArgs = null, Type[] genericNestedTypeArgs = null, string localFunc = null)
	{
		MethodBase methodBase = GetMethod(parentType, parentMethod, parentMethodType, parentArgs);
		if (methodBase == null)
		{
			throw new Exception($"Couldn't find parent method ({parentMethodType}) {parentType}::{parentMethod}");
		}
		if (genericParentMethodArgs != null && methodBase is MethodInfo methodInfo)
		{
			methodBase = methodInfo.MakeGenericMethod(genericParentMethodArgs);
		}
		int methodDebugId = GetMethodDebugId(methodBase);
		string displayClassPrefix = $"{"<>c__DisplayClass"}{methodDebugId}_";
		string localFuncPrefix = $"<{parentMethod}>{"g__"}{localFunc}|";
		string localFuncPrefixWithId = $"<{parentMethod}>{"g__"}{localFunc}|{methodDebugId}";
		MethodInfo[] array = (from m in (from t in parentType.GetNestedTypes(AccessTools.all).Where((Type t) =>
				{
					if (genericNestedTypeArgs != null && genericNestedTypeArgs.Length > 0)
					{
						if (!t.IsGenericType)
						{
							return false;
						}
						if (t.GetGenericArguments().Length != genericNestedTypeArgs.Length)
						{
							return false;
						}
					}
					return t.Name.StartsWith(displayClassPrefix);
				})
				select (genericNestedTypeArgs != null) ? t.MakeGenericType(genericNestedTypeArgs) : t).SelectMany(AccessTools.GetDeclaredMethods)
			where m.Name.StartsWith(localFuncPrefix)
			select m).Concat(from m in AccessTools.GetDeclaredMethods(parentType)
			where m.Name.StartsWith(localFuncPrefixWithId)
			select m).ToArray();
		if (array.Length == 0)
		{
			throw new Exception($"Couldn't find local function {localFunc} in parent method {parentType}::{methodBase.Name} (parent method id: {methodDebugId})");
		}
		if (array.Length > 1)
		{
			throw new Exception($"Ambiguous local function {localFunc} in parent method {parentType}::{methodBase.Name} (parent method id: {methodDebugId})");
		}
		return array[0];
	}

	public static int GetMethodDebugId(MethodBase method)
	{
		string text = null;
		try
		{
			foreach (CodeInstruction originalInstruction in PatchProcessor.GetOriginalInstructions(method))
			{
				if (originalInstruction.opcode == OpCodes.Newobj && originalInstruction.operand is MethodBase methodBase && (text = methodBase.DeclaringType.Name) != null)
				{
					if (text.Contains('`'))
					{
						text = text.Until('`');
					}
					if (text.StartsWith("<>c__DisplayClass"))
					{
						return int.Parse(text.Substring("<>c__DisplayClass".Length).Until('_'));
					}
					if (text.Contains("d__"))
					{
						return int.Parse(text.After('>').Substring("d__".Length));
					}
				}
				else
				{
					if ((!(originalInstruction.opcode == OpCodes.Ldftn) && !(originalInstruction.opcode == OpCodes.Call)) || !(originalInstruction.operand is MethodBase { Name: var name }))
					{
						continue;
					}
					text = name;
					if (name != null && text.StartsWith("<") && text.After('>').CharacterCount('_') == 3)
					{
						if (text.Contains('`'))
						{
							text = text.Until('`');
						}
						if (text.Contains("b__"))
						{
							return int.Parse(text.After('>').Substring("b__".Length).Until('_'));
						}
						if (text.Contains("g__"))
						{
							return int.Parse(text.After('|').Until('_'));
						}
					}
				}
			}
		}
		catch (Exception value)
		{
			throw new Exception($"Extracting debug id for {method.DeclaringType}::{method.Name} failed at {text} with: {value}");
		}
		throw new Exception($"Couldn't determine debug id for parent method {method.DeclaringType}::{method.Name}");
	}

	public static MethodBase GetMethod(Type type, string methodName, MethodType methodType, Type[] args)
	{
		if (type == null)
		{
			return null;
		}
		switch (methodType)
		{
		case MethodType.Normal:
			if (methodName == null)
			{
				return null;
			}
			return AccessTools.DeclaredMethod(type, methodName, args);
		case MethodType.Getter:
			if (methodName == null)
			{
				return AccessTools.DeclaredIndexer(type, args).GetGetMethod(nonPublic: true);
			}
			return AccessTools.DeclaredProperty(type, methodName).GetGetMethod(nonPublic: true);
		case MethodType.Setter:
			if (methodName == null)
			{
				return AccessTools.DeclaredIndexer(type, args).GetSetMethod(nonPublic: true);
			}
			return AccessTools.DeclaredProperty(type, methodName).GetSetMethod(nonPublic: true);
		case MethodType.Constructor:
			return AccessTools.DeclaredConstructor(type, args);
		case MethodType.StaticConstructor:
			return AccessTools.GetDeclaredConstructors(type).FirstOrDefault((ConstructorInfo c) => c.IsStatic);
		case MethodType.Enumerator:
			if (methodName == null)
			{
				return null;
			}
			return AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(type, methodName, args));
		case MethodType.Async:
			if (methodName == null)
			{
				return null;
			}
			return AccessTools.AsyncMoveNext(AccessTools.DeclaredMethod(type, methodName, args));
		default:
			return null;
		}
	}

	public static MethodInfo GetFirstMethodBySignature(Type type, Type[] paramsType)
	{
		foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
		{
			List<Type> list = new List<Type>();
			if (declaredMethod.GetParameters().Length != 0)
			{
				ParameterInfo[] parameters = declaredMethod.GetParameters();
				foreach (ParameterInfo parameterInfo in parameters)
				{
					list.Add(parameterInfo.ParameterType);
				}
			}
			if (paramsType.All(list.Contains) && paramsType.Count() == list.Count)
			{
				return declaredMethod;
			}
		}
		return null;
	}

	public static MethodInfo GetFirstMethodBySignature(Type type, Type[] paramsType, Type returnType)
	{
		foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
		{
			List<Type> list = new List<Type>();
			if (declaredMethod.GetParameters().Length != 0)
			{
				ParameterInfo[] parameters = declaredMethod.GetParameters();
				foreach (ParameterInfo parameterInfo in parameters)
				{
					list.Add(parameterInfo.ParameterType);
				}
			}
			if (paramsType.All(list.Contains) && paramsType.Count() == list.Count && returnType == declaredMethod.ReturnType)
			{
				return declaredMethod;
			}
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static MethodInfo MethodOf(Delegate method)
	{
		return method.Method;
	}
}
