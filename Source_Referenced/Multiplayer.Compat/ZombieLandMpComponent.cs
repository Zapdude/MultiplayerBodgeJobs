using System;
using System.Collections;
using System.Collections.Generic;
using Multiplayer.API;
using Verse;
using ZombieLand;

namespace Multiplayer.Compat;

[MpCompatRequireMod("brrainz.zombieland")]
public class ZombieLandMpComponent : GameComponent
{
	public static readonly List<IEnumerator> Coroutines = new List<IEnumerator>();

	private static readonly List<int> Cleanup = new List<int>();

	private static readonly List<AvoidRequest> ReplacementRequestQueue = new List<AvoidRequest>();

	private static readonly Dictionary<Map, Queue<AvoidGrid>> ReplacementResultQueue = new Dictionary<Map, Queue<AvoidGrid>>();

	public ZombieLandMpComponent(Game game)
	{
	}

	public static Queue<AvoidGrid> QueueForMap(Map map)
	{
		if (ReplacementResultQueue.TryGetValue(map, out var value))
		{
			return value;
		}
		value = new Queue<AvoidGrid>();
		ReplacementResultQueue.Add(map, value);
		return value;
	}

	public override void GameComponentTick()
	{
		if (ReplacementRequestQueue.Any())
		{
			try
			{
				AvoidRequest val = ReplacementRequestQueue[0];
				ReplacementRequestQueue.RemoveAt(0);
				AvoidGrid val2 = Tools.avoider.ProcessRequest(val);
				QueueForMap(val.map).Enqueue(val2);
			}
			catch (Exception arg)
			{
				Log.Warning($"ZombieAvoider thread replacement for multiplayer error:\n{arg}");
			}
		}
		for (int i = 0; i < Coroutines.Count; i++)
		{
			if (!Coroutines[i].MoveNext())
			{
				Cleanup.Add(i);
			}
		}
		if (Cleanup.Any())
		{
			for (int num = Cleanup.Count - 1; num >= 0; num--)
			{
				Coroutines.RemoveAt(Cleanup[num]);
			}
			Cleanup.Clear();
		}
	}

	public override void StartedNewGame()
	{
		LogError();
	}

	public override void LoadedGame()
	{
		LogError();
	}

	private static void LogError()
	{
		if (!MP.IsInMultiplayer)
		{
			Log.ErrorOnce("For performance reasons, ZombieLand should not be used in single player if Multiplayer Compatibility mod is active. Unless you're setting up a multiplayer session, please disable MP Compat to gain better performance.", -434115778);
		}
	}

	public static void Enqueue(ConcurrentQueue<AvoidRequest> queue, AvoidRequest item, Func<AvoidRequest, bool> overwritePredicate)
	{
		if (overwritePredicate == null)
		{
			ReplacementRequestQueue.Add(item);
			return;
		}
		int num = ReplacementRequestQueue.FirstIndexOf(overwritePredicate);
		if (num >= 0 && num < ReplacementRequestQueue.Count)
		{
			ReplacementRequestQueue[num] = item;
		}
		else
		{
			ReplacementRequestQueue.Add(item);
		}
	}
}
