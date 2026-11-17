using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Verse;

namespace Multiplayer.Compat;

internal static class ReferenceBuilder
{
	private enum BuildResult
	{
		Success,
		Skipped,
		Failure
	}

	public static (int successes, int skipped, int failures) Restore(string refsFolder)
	{
		FileInfo[] files = Directory.CreateDirectory(refsFolder).GetFiles("*.txt");
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		FileInfo[] array = files;
		foreach (FileInfo request in array)
		{
			switch (BuildReference(refsFolder, request))
			{
			case BuildResult.Success:
				num++;
				break;
			case BuildResult.Skipped:
				num2++;
				break;
			default:
				num3++;
				break;
			}
		}
		return (successes: num, skipped: num2, failures: num3);
	}

	private static BuildResult BuildReference(string refsFolder, FileInfo request)
	{
		string asmId = Path.GetFileNameWithoutExtension(request.Name);
		Log.Warning("Trying to write:\nFile: " + request.FullName + "\nAssembly: " + asmId);
		FileInfo fileInfo = LoadedModManager.RunningModsListForReading.SelectMany((ModContentPack m) => ModContentPack.GetAllFilesForModPreserveOrder(m, "Assemblies/", (string f) => f.ToLower() == ".dll")).FirstOrDefault((Tuple<string, FileInfo> f) => f.Item2.Name == asmId + ".dll")?.Item2;
		if (fileInfo == null)
		{
			Log.Warning("Null assembly found, skipping");
			return BuildResult.Failure;
		}
		string text = ComputeHash(fileInfo.FullName);
		string path = Path.Combine(refsFolder, asmId + ".txt");
		if (File.Exists(path) && File.ReadAllText(path) == text)
		{
			Log.Warning("Hashes are matching, skipping. Hash: " + text);
			return BuildResult.Skipped;
		}
		Log.Warning("MpCompat References: Writing " + asmId + ".dll");
		string fileName = Path.Combine(refsFolder, asmId + ".dll");
		using Mono.Cecil.AssemblyDefinition assemblyDefinition = Mono.Cecil.AssemblyDefinition.ReadAssembly(fileInfo.FullName);
		foreach (Mono.Cecil.TypeDefinition type in assemblyDefinition.MainModule.GetTypes())
		{
			if (type.IsNested)
			{
				type.IsNestedPublic = true;
			}
			else
			{
				type.IsPublic = true;
			}
			foreach (Mono.Cecil.MethodDefinition method in type.Methods)
			{
				method.IsPublic = true;
				method.Body = new Mono.Cecil.Cil.MethodBody(method);
			}
			foreach (Mono.Cecil.FieldDefinition field in type.Fields)
			{
				field.IsInitOnly = false;
				field.IsPublic = true;
			}
		}
		assemblyDefinition.Write(fileName);
		File.WriteAllText(path, text);
		return BuildResult.Success;
	}

	private static string ComputeHash(string assemblyPath)
	{
		StringBuilder stringBuilder = new StringBuilder();
		using SHA1 sHA = SHA1.Create();
		using FileStream inputStream = File.Open(assemblyPath, FileMode.Open, FileAccess.Read);
		sHA.ComputeHash(inputStream);
		byte[] hash = sHA.Hash;
		foreach (byte b in hash)
		{
			stringBuilder.Append(b.ToString("X2"));
		}
		return stringBuilder.ToString();
	}
}
