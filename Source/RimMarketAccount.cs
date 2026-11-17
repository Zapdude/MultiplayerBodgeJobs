using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

public sealed class RimMarketAccount : IExposable
{
	internal static readonly string[] Names = new string[18]
	{
		"reputation", "journal", "ledgers", "notice", "innPrices", "guestBook", "fairExcluded", "lastFairTick", "totalFees", "totalGuestSales",
		"totalColonySales", "thievesCaught", "thievesEscaped", "thievesDeterred", "recentTakings", "recentTakingsTick", "awayCooldowns", "guideOffered"
	};

	public Faction owner;

	public bool primary;

	internal int noticeRevision;

	private Dictionary<string, object> values;

	public RimMarketAccount()
	{
	}

	internal RimMarketAccount(Faction owner, bool primary)
	{
		this.owner = owner;
		this.primary = primary;
	}

	private void Ensure()
	{
		if (values == null)
		{
			values = new Dictionary<string, object>();
			string[] names = Names;
			foreach (string text in names)
			{
				Type fieldType = RimMarket.Fields[text].FieldType;
				Dictionary<string, object> dictionary = values;
				string key = text;
				object? value;
				if (text == "notice")
				{
					value = null;
				}
				else
				{
					value = (fieldType.IsValueType ? Activator.CreateInstance(fieldType) : Activator.CreateInstance(fieldType));
				}
				dictionary[key] = value;
			}
			values["innPrices"] = new List<int> { -1, -1, -1 };
		}
	}

	internal void Capture(object manager)
	{
		Ensure();
		string[] names = Names;
		foreach (string key in names)
		{
			values[key] = RimMarket.Fields[key].GetValue(manager);
		}
	}

	internal void Apply(object manager)
	{
		Ensure();
		string[] names = Names;
		foreach (string key in names)
		{
			RimMarket.Fields[key].SetValue(manager, values[key]);
		}
	}

	internal object Value(string name)
	{
		Ensure();
		return values[name];
	}

	public void ExposeData()
	{
		Scribe_References.Look(ref owner, "owner");
		Scribe_Values.Look(ref primary, "nativePrimary", defaultValue: false);
		Scribe_Values.Look(ref noticeRevision, "noticeRevision", 0);
		if (primary || !RimMarket.Installed)
		{
			return;
		}
		Ensure();
		string[] names = Names;
		bool flag = default;
		foreach (string text in names)
		{
			Type fieldType = RimMarket.Fields[text].FieldType;
			object obj = values[text];
			if (fieldType == typeof(int))
			{
				int value = ((obj is int num) ? num : 0);
				Scribe_Values.Look(ref value, text, 0);
				values[text] = value;
				continue;
			}
			if (fieldType == typeof(float))
			{
				float value2 = ((obj is float num2) ? num2 : 0f);
				Scribe_Values.Look(ref value2, text, 0f);
				values[text] = value2;
				continue;
			}
			if (fieldType == typeof(bool))
			{
				int num3;
				if (obj is bool)
				{
					flag = (bool)obj;
					num3 = 1;
				}
				else
				{
					num3 = 0;
				}
				bool value3 = (byte)((uint)num3 & (flag ? 1u : 0u)) != 0;
				Scribe_Values.Look(ref value3, text, defaultValue: false);
				values[text] = value3;
				continue;
			}
			if (fieldType == typeof(List<int>))
			{
				List<int> list = (List<int>)obj;
				Scribe_Collections.Look(ref list, text, LookMode.Value);
				values[text] = list ?? new List<int> { -1, -1, -1 };
				continue;
			}
			if (fieldType == typeof(List<Faction>))
			{
				List<Faction> list2 = (List<Faction>)obj;
				Scribe_Collections.Look(ref list2, text, LookMode.Reference);
				values[text] = list2 ?? new List<Faction>();
				continue;
			}
			if (fieldType == typeof(Dictionary<int, int>))
			{
				Dictionary<int, int> dict = (Dictionary<int, int>)obj;
				Scribe_Collections.Look(ref dict, text, LookMode.Value, LookMode.Value);
				values[text] = dict ?? new Dictionary<int, int>();
				continue;
			}
			if (typeof(IExposable).IsAssignableFrom(fieldType))
			{
				IExposable target = obj as IExposable;
				Scribe_Deep.Look(ref target, text);
				values[text] = target;
				continue;
			}
			if (typeof(IList).IsAssignableFrom(fieldType))
			{
				IList list3 = (IList)obj;
				List<IExposable> list4 = ((list3 != null) ? list3.Cast<IExposable>().ToList() : null);
				Scribe_Collections.Look(ref list4, text, LookMode.Deep);
				LoadSaveMode mode = Scribe.mode;
				if (mode - 2 > LoadSaveMode.LoadingVars)
				{
					continue;
				}
				IList list5 = (IList)Activator.CreateInstance(fieldType);
				foreach (IExposable item in list4 ?? new List<IExposable>())
				{
					if (item != null)
					{
						list5.Add(item);
					}
				}
				values[text] = list5;
				continue;
			}
			throw new InvalidOperationException("Unreviewed RimMarket account field " + text);
		}
	}
}
