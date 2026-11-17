using System;
using HarmonyLib;

namespace Multiplayer.Compat;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class MpCompatFinalizerAttribute : MpCompatPatchAttribute
{
	public MpCompatFinalizerAttribute(string typeName, string method, MethodType methodType = MethodType.Normal)
		: base(typeName, method, methodType)
	{
	}

	public MpCompatFinalizerAttribute(Type type, string method, Type[] argTypes = null, MethodType methodType = MethodType.Normal)
		: base(type, method, argTypes, methodType)
	{
	}

	public MpCompatFinalizerAttribute(Type type, string innerType, string method, MethodType methodType = MethodType.Normal)
		: base(type, innerType, method, methodType)
	{
	}

	public MpCompatFinalizerAttribute(Type parentType, string parentMethod, int lambdaOrdinal, MethodType methodType = MethodType.Normal)
		: base(parentType, parentMethod, lambdaOrdinal, methodType)
	{
	}

	public MpCompatFinalizerAttribute(string typeName, string parentMethod, int lambdaOrdinal, MethodType methodType = MethodType.Normal)
		: base(typeName, parentMethod, lambdaOrdinal, methodType)
	{
	}
}
