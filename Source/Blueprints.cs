using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Fluffy.Blueprints")]
internal class Blueprints
{
	private static readonly Type buildableInfo = AccessTools.TypeByName("Blueprints.BuildableInfo");

	private static readonly FieldInfo buildableInfoTerrainDef = AccessTools.Field(buildableInfo, "_terrainDef");

	private static readonly FieldInfo buildableInfoThingDef = AccessTools.Field(buildableInfo, "_thingDef");

	private static readonly FieldInfo buildableInfoPosition = AccessTools.Field(buildableInfo, "_position");

	private static readonly FieldInfo buildableInfoRotation = AccessTools.Field(buildableInfo, "_rotation");

	private static readonly FieldInfo buildableInfoStuff = AccessTools.Field(buildableInfo, "_stuff");

	private static readonly ConstructorInfo buildableInfoConstructorTerrain = AccessTools.Constructor(buildableInfo, new Type[3]
	{
		typeof(TerrainDef),
		typeof(IntVec3),
		typeof(IntVec3)
	});

	private static readonly ConstructorInfo buildableInfoConstructorThing = AccessTools.Constructor(buildableInfo, new Type[2]
	{
		typeof(Thing),
		typeof(IntVec3)
	});

	private static readonly Type listBuildableInfo = typeof(List<>).MakeGenericType(buildableInfo);

	private static readonly Type blueprint = AccessTools.TypeByName("Blueprints.Blueprint");

	private static readonly FieldInfo contentsField = AccessTools.Field(blueprint, "contents");

	private static readonly FieldInfo _sizeField = AccessTools.Field(blueprint, "_size");

	private static readonly ConstructorInfo blueprintConstructor = AccessTools.Constructor(blueprint, new Type[4]
	{
		listBuildableInfo,
		typeof(IntVec2),
		typeof(string),
		typeof(bool)
	});

	private static readonly Type designator_Blueprint = AccessTools.TypeByName("Blueprints.Designator_Blueprint");

	private static readonly MethodInfo blueprintGetter = AccessTools.DeclaredPropertyGetter(designator_Blueprint, "Blueprint");

	private static readonly ConstructorInfo designator_BlueprintConstructor = AccessTools.Constructor(designator_Blueprint, new Type[1] { blueprint });

	public Blueprints(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void SyncBuildableInfo(SyncWorker sync, ref IExposable obj)
	{
		if (sync.isWriting)
		{
			TerrainDef terrainDef = (TerrainDef)buildableInfoTerrainDef.GetValue(obj);
			bool flag = terrainDef != null;
			sync.Write(flag);
			if (flag)
			{
				sync.Write(terrainDef);
				IntVec3 intVec = (IntVec3)buildableInfoPosition.GetValue(obj);
				sync.Write(intVec.x);
				sync.Write(intVec.y);
				sync.Write(intVec.z);
			}
			else
			{
				sync.Write((ThingDef)buildableInfoThingDef.GetValue(obj));
				IntVec3 intVec2 = (IntVec3)buildableInfoPosition.GetValue(obj);
				sync.Write(intVec2.x);
				sync.Write(intVec2.y);
				sync.Write(intVec2.z);
				sync.Write((Rot4)buildableInfoRotation.GetValue(obj));
				sync.Write((ThingDef)buildableInfoStuff.GetValue(obj));
			}
		}
		else if (sync.Read<bool>())
		{
			TerrainDef terrainDef2 = sync.Read<TerrainDef>();
			IntVec3 intVec3 = new IntVec3(sync.Read<int>(), sync.Read<int>(), sync.Read<int>());
			obj = (IExposable)buildableInfoConstructorTerrain.Invoke(new object[3]
			{
				terrainDef2,
				intVec3,
				IntVec3.Zero
			});
		}
		else
		{
			ThingDef def = sync.Read<ThingDef>();
			IntVec3 position = new IntVec3(sync.Read<int>(), sync.Read<int>(), sync.Read<int>());
			Rot4 rotation = sync.Read<Rot4>();
			ThingDef stuffDirect = sync.Read<ThingDef>();
			Thing thing = new Thing();
			thing.def = def;
			thing.Position = position;
			thing.Rotation = rotation;
			thing.SetStuffDirect(stuffDirect);
			obj = (IExposable)buildableInfoConstructorThing.Invoke(new object[2]
			{
				thing,
				IntVec3.Zero
			});
		}
	}

	private static void SyncBlueprint(SyncWorker sync, ref IExposable obj)
	{
		if (sync.isWriting)
		{
			sync.Write((IList)contentsField.GetValue(obj), listBuildableInfo);
			sync.Write(((IntVec2)_sizeField.GetValue(obj)).x);
			sync.Write(((IntVec2)_sizeField.GetValue(obj)).z);
		}
		else
		{
			obj = (IExposable)blueprintConstructor.Invoke(new object[4]
			{
				sync.Read<IList>(listBuildableInfo),
				new IntVec2(sync.Read<int>(), sync.Read<int>()),
				null,
				true
			});
		}
	}

	private static void SyncDesignator_Blueprint(SyncWorker sync, ref Designator obj)
	{
		if (sync.isWriting)
		{
			sync.Write((IExposable)blueprintGetter.Invoke(obj, null), blueprint);
			return;
		}
		ConstructorInfo constructorInfo = designator_BlueprintConstructor;
		object[] parameters = new IExposable[1] { sync.Read<IExposable>(blueprint) };
		obj = (Designator)constructorInfo.Invoke(parameters);
	}

	private static void LatePatch()
	{
		MP.RegisterSyncWorker<IExposable>(SyncBuildableInfo, buildableInfo);
		MP.RegisterSyncWorker<IExposable>(SyncBlueprint, blueprint);
		MP.RegisterSyncWorker<Designator>(SyncDesignator_Blueprint, designator_Blueprint);
	}
}
