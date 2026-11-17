using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Verse;

namespace Multiplayer.Compat;

public static class MpCompatLoader
{
	internal static void Load(ModContentPack content)
	{
		LoadConditional(content);
		foreach (Assembly loadedAssembly in content.assemblies.loadedAssemblies)
		{
			InitCompatInAsm(loadedAssembly);
		}
		ClearCaches();
	}

	private static void LoadConditional(ModContentPack content)
	{
		if (LoadedModManager.RunningMods.Any((ModContentPack m) => m.PackageId.NoModIdSuffix() == "vanillaexpanded.gravship2"))
		{
			Assembly item = Assembly.LoadFrom((ModContentPack.GetAllFilesForModPreserveOrder(content, "Referenced/", (string f) => f.ToLower() == ".dll").FirstOrDefault((Tuple<string, FileInfo> f) => f.Item2.Name == "Multiplayer_Gravship2Overrides.dll")?.Item2 ?? throw new FileNotFoundException("Missing Ship2 Workshop compatibility body module")).FullName);
			if (!content.assemblies.loadedAssemblies.Contains(item))
			{
				content.assemblies.loadedAssemblies.Add(item);
			}
		}
		FileInfo fileInfo = ModContentPack.GetAllFilesForModPreserveOrder(content, "Referenced/", (string f) => f.ToLower() == ".dll").FirstOrDefault((Tuple<string, FileInfo> f) => f.Item2.Name == "Multiplayer_Compat_Referenced.dll")?.Item2;
		if (fileInfo == null)
		{
			return;
		}
		using Mono.Cecil.AssemblyDefinition assemblyDefinition = Mono.Cecil.AssemblyDefinition.ReadAssembly(fileInfo.FullName);
		Mono.Cecil.TypeDefinition[] array = assemblyDefinition.MainModule.GetTypes().ToArray();
		foreach (Mono.Cecil.TypeDefinition typeDefinition in array)
		{
			Mono.Cecil.CustomAttribute[] source = typeDefinition.CustomAttributes.Where((Mono.Cecil.CustomAttribute a) =>
			{
				string name = a.Constructor.DeclaringType.Name;
				return (name == "MpCompatForAttribute" || name == "MpCompatRequireModAttribute") ? true : false;
			}).ToArray();
			if (source.Any() && !source.Any((Mono.Cecil.CustomAttribute a) =>
			{
				string modId = ((string)a.ConstructorArguments.First().Value).ToLower();
				return LoadedModManager.RunningMods.FirstOrDefault((ModContentPack m) => m.PackageId.NoModIdSuffix() == modId) != null;
			}))
			{
				assemblyDefinition.MainModule.Types.Remove(typeDefinition);
			}
		}
		using MemoryStream memoryStream = new MemoryStream();
		assemblyDefinition.Write(memoryStream);
		Assembly item2 = Assembly.Load(memoryStream.ToArray());
		content.assemblies.loadedAssemblies.Add(item2);
	}

	private static void InitCompatInAsm(Assembly asm)
	{
		foreach (var item in from box in (from t in asm.GetTypes()
				where t.HasAttribute<MpCompatForAttribute>()
				select t).SelectMany((Type t) => (MpCompatForAttribute[])t.GetCustomAttributes(typeof(MpCompatForAttribute), inherit: false), (Type type, MpCompatForAttribute compat) => new { type, compat })
			join mod in LoadedModManager.RunningMods on box.compat.PackageId.ToLower() equals mod.PackageId.NoModIdSuffix()
			select new { box.type, mod })
		{
			try
			{
				Activator.CreateInstance(item.type, item.mod);
				Log.Message("MPCompat :: Initialized compatibility for " + item.mod.PackageId);
			}
			catch (Exception ex)
			{
				Log.Error($"MPCompat :: Exception loading {item.mod.PackageId}: {ex.InnerException ?? ex}");
			}
		}
	}

	private static void ClearCaches()
	{
		GenTypes.ClearCache();
		Type mpType = AccessTools.TypeByName("Multiplayer.Client.Util.TypeCache") ?? AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
		if (mpType == null)
		{
			Log.Warning("MPCompat :: Multiplayer not found, skipping cache clearing.");
			return;
		}
		ClearDict("subClasses");
		ClearDict("subClassesOrdered");
		ClearDict("subClassesNonAbstract");
		ClearDict("interfaceImplementations");
		ClearDict("interfaceImplementationsOrdered");
		AccessTools.Method(mpType, "CacheTypeHierarchy")?.Invoke(null, Array.Empty<object>());
		AccessTools.Method("Multiplayer.Client.ApiSerialization:Init")?.Invoke(null, Array.Empty<object>());
		AccessTools.Method("Multiplayer.Client.MultiplayerData:CollectDefInfos")?.Invoke(null, Array.Empty<object>());
		void ClearDict(string field)
		{
			((IDictionary)(AccessTools.Field(mpType, field)?.GetValue(null)))?.Clear();
		}
	}
}
