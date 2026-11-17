using System;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;

namespace Multiplayer.Compat;

[AttributeUsage(AttributeTargets.Field)]
[MeansImplicitUse(ImplicitUseKindFlags.Assign)]
public class MpCompatSyncFieldAttribute : Attribute
{
	public bool cancelIfValueNull;

	public bool inGameLoop;

	public bool bufferChanges = true;

	public bool debugOnly;

	public bool hostOnly;

	public int version;

	private Type type;

	private string typeName;

	public string fieldName;

	public string instancePath;

	private FieldInfo field;

	public Type Type
	{
		get
		{
			if (type != null)
			{
				return type;
			}
			if (!string.IsNullOrWhiteSpace(instancePath) && string.IsNullOrWhiteSpace(typeName))
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

	public FieldInfo Field
	{
		get
		{
			if (this.field != null)
			{
				return this.field;
			}
			this.field = AccessTools.Field(Type, fieldName);
			if (this.field == null)
			{
				throw new MissingFieldException($"Couldn't find field {fieldName} in type {Type}");
			}
			return this.field;
		}
	}

	public MpCompatSyncFieldAttribute(string typeName, string fieldName)
	{
		this.typeName = typeName;
		this.fieldName = fieldName;
	}

	public MpCompatSyncFieldAttribute(Type type, string fieldName)
	{
		this.type = type;
		this.fieldName = fieldName;
	}

	public MpCompatSyncFieldAttribute(string typeName, string instancePath, string fieldName)
		: this(typeName, fieldName)
	{
		this.instancePath = instancePath;
	}

	public MpCompatSyncFieldAttribute(Type type, string instancePath, string fieldName)
		: this(type, fieldName)
	{
		this.instancePath = instancePath;
	}
}
