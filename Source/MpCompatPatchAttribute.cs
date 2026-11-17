using System;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

namespace Multiplayer.Compat;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
[MeansImplicitUse(ImplicitUseTargetFlags.WithMembers)]
public abstract class MpCompatPatchAttribute : Attribute
{
	private Type type;

	private string typeName;

	private string methodName;

	private Type[] argTypes;

	private MethodType methodType;

	private int? lambdaOrdinal;

	private MethodBase method;

	public Type Type
	{
		get
		{
			if (type != null)
			{
				return type;
			}
			type = AccessTools.TypeByName(typeName);
			if (type == null)
			{
				throw new Exception("Couldn't find type " + typeName);
			}
			return type;
		}
	}

	public MethodBase Method
	{
		get
		{
			if (method != null)
			{
				return method;
			}
			if (lambdaOrdinal.HasValue)
			{
				return MpMethodUtil.GetLambda(Type, methodName, methodType, argTypes, lambdaOrdinal.Value);
			}
			method = MpMethodUtil.GetMethod(Type, methodName, methodType, argTypes);
			if (method == null)
			{
				throw new MissingMethodException($"Couldn't find method {methodName} in type {Type}");
			}
			return method;
		}
	}

	protected MpCompatPatchAttribute(Type type, string innerType, string methodName, MethodType methodType = MethodType.Normal)
		: this($"{type}+{innerType}", methodName)
	{
	}

	protected MpCompatPatchAttribute(string typeName, string methodName, MethodType methodType = MethodType.Normal)
	{
		this.typeName = typeName;
		this.methodName = methodName;
		this.methodType = methodType;
	}

	protected MpCompatPatchAttribute(Type type, string methodName, Type[] argTypes = null, MethodType methodType = MethodType.Normal)
	{
		this.type = type;
		this.methodName = methodName;
		this.argTypes = argTypes;
		this.methodType = methodType;
	}

	protected MpCompatPatchAttribute(Type type, MethodType methodType, Type[] argTypes = null)
	{
		this.type = type;
		this.methodType = methodType;
		this.argTypes = argTypes;
		this.methodType = methodType;
	}

	protected MpCompatPatchAttribute(Type type, string methodName, int lambdaOrdinal, MethodType methodType = MethodType.Normal)
	{
		this.type = type;
		this.methodName = methodName;
		this.lambdaOrdinal = lambdaOrdinal;
		this.methodType = methodType;
	}

	protected MpCompatPatchAttribute(string typeName, string methodName, int lambdaOrdinal, MethodType methodType = MethodType.Normal)
	{
		this.typeName = typeName;
		this.methodName = methodName;
		this.lambdaOrdinal = lambdaOrdinal;
		this.methodType = methodType;
	}
}
