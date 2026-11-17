using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

public static class MpCompatPatchLoader
{
	private static readonly MethodInfo RegisterSyncWorker;

	private static readonly FastInvokeHandler RegisterSyncFieldWithInstancePath;

	static MpCompatPatchLoader()
	{
		RegisterSyncWorker = typeof(MP).GetMethods(AccessTools.allDeclared).FirstOrDefault((MethodInfo m) =>
		{
			if (m.Name != "RegisterSyncWorker")
			{
				return false;
			}
			if (!m.IsGenericMethod)
			{
				return false;
			}
			ParameterInfo[] parameters = m.GetParameters();
			if (parameters == null || parameters.Length != 4)
			{
				return false;
			}
			return parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(SyncWorkerDelegate<>) && parameters[1].ParameterType == typeof(Type) && parameters[2].ParameterType == typeof(bool) && parameters[3].ParameterType == typeof(bool);
		});
		if (RegisterSyncWorker == null)
		{
			Log.Error("Retrieved null method: MP.RegisterSyncWorker");
		}
		MethodInfo methodInfo = AccessTools.DeclaredMethod("Multiplayer.Client.Sync:Field", new Type[3]
		{
			typeof(Type),
			typeof(string),
			typeof(string)
		});
		if (methodInfo == null)
		{
			Log.Error("Retrieved null method: Multiplayer.Client.Sync:Field(Type, string, string)");
		}
		else if (!methodInfo.IsStatic)
		{
			Log.Error("Retrieved non-static method: Multiplayer.Client.Sync:Field(Type, string, string)");
		}
		else if (!typeof(ISyncField).IsAssignableFrom(methodInfo.ReturnType))
		{
			Log.Error($"Retrieved method has incorrect return argument (expected subtype of {"ISyncField"}, received {methodInfo.ReturnType}): {"Multiplayer.Client.Sync:Field(Type, string, string)"}");
		}
		else
		{
			RegisterSyncFieldWithInstancePath = HarmonyLib.MethodInvoker.GetHandler(methodInfo);
		}
	}

	public static void LoadPatch(object instance)
	{
		LoadPatch(instance?.GetType());
	}

	public static void LoadPatch<T>()
	{
		LoadPatch(typeof(T));
	}

	public static void LoadPatch(Type type)
	{
		foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type))
		{
			Attribute[] customAttributes = Attribute.GetCustomAttributes(declaredMethod);
			foreach (Attribute attribute in customAttributes)
			{
				try
				{
					if (!(attribute is MpCompatPatchAttribute mpCompatPatchAttribute))
					{
						if (!(attribute is MpCompatSyncMethodAttribute mpCompatSyncMethodAttribute))
						{
							if (attribute is MpCompatSyncWorkerAttribute mpCompatSyncWorkerAttribute && !(RegisterSyncWorker == null))
							{
								if (!declaredMethod.IsStatic)
								{
									throw new Exception("Cannot register sync worker: method must be static");
								}
								if (declaredMethod.ReturnType != typeof(void))
								{
									throw new Exception("Cannot register sync worker: return type must be null");
								}
								ParameterInfo[] parameters = declaredMethod.GetParameters();
								if (parameters == null || parameters.Length != 2)
								{
									throw new Exception("Cannot register sync worker: delegate doesn't have 2 parameters");
								}
								if (parameters[0].ParameterType != typeof(SyncWorker))
								{
									throw new Exception("Cannot register sync worker: the first argument isn't of type SyncWorker");
								}
								Type elementType = parameters[1].ParameterType.GetElementType();
								Delegate obj = Delegate.CreateDelegate(typeof(SyncWorkerDelegate<>).MakeGenericType(elementType), declaredMethod);
								RegisterSyncWorker.MakeGenericMethod(elementType).Invoke(null, new object[4] { obj, mpCompatSyncWorkerAttribute.Type, mpCompatSyncWorkerAttribute.isImplicit, mpCompatSyncWorkerAttribute.shouldConstruct });
							}
							continue;
						}
						ISyncMethod syncMethod = MP.RegisterSyncMethod(declaredMethod).SetContext(mpCompatSyncMethodAttribute.context);
						if (mpCompatSyncMethodAttribute.cancelIfAnyArgNull)
						{
							syncMethod.CancelIfAnyArgNull();
						}
						if (mpCompatSyncMethodAttribute.cancelIfNoSelectedMapObjects)
						{
							syncMethod.CancelIfNoSelectedMapObjects();
						}
						if (mpCompatSyncMethodAttribute.cancelIfNoSelectedWorldObjects)
						{
							syncMethod.CancelIfNoSelectedWorldObjects();
						}
						if (mpCompatSyncMethodAttribute.debugOnly)
						{
							syncMethod.SetDebugOnly();
						}
						if (mpCompatSyncMethodAttribute.hostOnly)
						{
							syncMethod.SetHostOnly();
						}
						if (mpCompatSyncMethodAttribute.version > 0)
						{
							syncMethod.SetVersion(mpCompatSyncMethodAttribute.version);
						}
						if (mpCompatSyncMethodAttribute.exposeParameters != null)
						{
							int[] exposeParameters = mpCompatSyncMethodAttribute.exposeParameters;
							foreach (int index in exposeParameters)
							{
								syncMethod.ExposeParameter(index);
							}
						}
					}
					else
					{
						if (!declaredMethod.IsStatic)
						{
							throw new Exception("Cannot register harmony patch: method must be static");
						}
						HarmonyMethod harmonyMethod = new HarmonyMethod(declaredMethod);
						MpCompat.harmony.Patch(mpCompatPatchAttribute.Method, (mpCompatPatchAttribute is MpCompatPrefixAttribute) ? harmonyMethod : null, (mpCompatPatchAttribute is MpCompatPostfixAttribute) ? harmonyMethod : null, (mpCompatPatchAttribute is MpCompatTranspilerAttribute) ? harmonyMethod : null, (mpCompatPatchAttribute is MpCompatFinalizerAttribute) ? harmonyMethod : null);
					}
				}
				catch (Exception value)
				{
					Log.Error($"MpCompatPatch {declaredMethod.DeclaringType}.{declaredMethod.Name} failed with exception: {value}");
				}
			}
		}
		foreach (FieldInfo declaredField in AccessTools.GetDeclaredFields(type))
		{
			try
			{
				ISyncField syncField;
				if (declaredField.TryGetAttribute<MpCompatSyncFieldAttribute>(out var customAttribute))
				{
					if (!declaredField.IsStatic)
					{
						throw new Exception("Cannot register sync field: field must be static");
					}
					if (!typeof(ISyncField).IsAssignableFrom(declaredField.FieldType))
					{
						throw new Exception($"{"Cannot register sync field:"} cannot assign object of type {"ISyncField"} to the field (field type: {declaredField.FieldType})");
					}
					if (string.IsNullOrWhiteSpace(customAttribute.instancePath))
					{
						syncField = MP.RegisterSyncField(customAttribute.Field);
						goto IL_0433;
					}
					if (RegisterSyncFieldWithInstancePath != null)
					{
						syncField = (ISyncField)RegisterSyncFieldWithInstancePath(null, customAttribute.Type, customAttribute.instancePath, customAttribute.fieldName);
						goto IL_0433;
					}
				}
				continue;
				IL_0433:
				if (customAttribute.cancelIfValueNull)
				{
					syncField.CancelIfValueNull();
				}
				if (customAttribute.inGameLoop)
				{
					syncField.InGameLoop();
				}
				if (customAttribute.bufferChanges)
				{
					syncField.SetBufferChanges();
				}
				if (customAttribute.debugOnly)
				{
					syncField.SetDebugOnly();
				}
				if (customAttribute.hostOnly)
				{
					syncField.SetHostOnly();
				}
				if (customAttribute.version > 0)
				{
					syncField.SetVersion(customAttribute.version);
				}
				declaredField.SetValue(null, syncField);
			}
			catch (Exception value2)
			{
				Log.Error($"MpCompatPatch {declaredField.DeclaringType}.{declaredField.Name} failed with exception: {value2}");
			}
		}
	}
}
