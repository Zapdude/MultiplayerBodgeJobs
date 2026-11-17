using System;
using HarmonyLib;

namespace Multiplayer.Compat;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class MpCompatPostfixAttribute : MpCompatPatchAttribute
{
	public MpCompatPostfixAttribute(string typeName, string method, MethodType methodType = MethodType.Normal)
		: base(typeName, method, methodType)
	{
	}

	public MpCompatPostfixAttribute(Type type, string method, Type[] argTypes = null, MethodType methodType = MethodType.Normal)
		: base(type, method, argTypes, methodType)
	{
	}

	public MpCompatPostfixAttribute(Type type, string innerType, string method, MethodType methodType = MethodType.Normal)
		: base(type, innerType, method, methodType)
	{
	}

	public MpCompatPostfixAttribute(Type parentType, string parentMethod, int lambdaOrdinal, MethodType methodType = MethodType.Normal)
		: base(parentType, parentMethod, lambdaOrdinal, methodType)
	{
	}

	public MpCompatPostfixAttribute(string typeName, string parentMethod, int lambdaOrdinal, MethodType methodType = MethodType.Normal)
		: base(typeName, parentMethod, lambdaOrdinal, methodType)
	{
	}
}
