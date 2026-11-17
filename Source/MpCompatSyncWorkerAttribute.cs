using System;
using HarmonyLib;
using JetBrains.Annotations;

namespace Multiplayer.Compat;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
[MeansImplicitUse(ImplicitUseTargetFlags.WithMembers)]
public class MpCompatSyncWorkerAttribute : Attribute
{
	public bool isImplicit;

	public bool shouldConstruct;

	private Type type;

	private string typeName;

	public Type Type
	{
		get
		{
			if (type != null)
			{
				return type;
			}
			if (typeName == null)
			{
				return null;
			}
			type = AccessTools.TypeByName(typeName);
			if (type == null)
			{
				throw new Exception("Couldn't find type " + typeName);
			}
			return type;
		}
	}

	public MpCompatSyncWorkerAttribute()
	{
	}

	public MpCompatSyncWorkerAttribute(Type type)
	{
		this.type = type;
	}

	public MpCompatSyncWorkerAttribute(string typeName)
	{
		this.typeName = typeName;
	}
}
