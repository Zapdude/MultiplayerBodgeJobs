using System;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

public sealed class AscensionDebtChoice : Window
{
	private readonly string text;

	private readonly string accept;

	private readonly string refuse;

	private readonly Func<bool> valid;

	private readonly Action<bool> choose;

	private bool submitted;

	public override Vector2 InitialSize => new Vector2(680f, 440f);

	public AscensionDebtChoice(string text, string accept, string refuse, Func<bool> valid, Action<bool> choose)
	{
		this.text = text;
		this.accept = accept;
		this.refuse = refuse;
		this.valid = valid;
		this.choose = choose;
		forcePause = false;
		absorbInputAroundWindow = true;
		closeOnCancel = false;
		closeOnAccept = false;
	}

	public override void WindowUpdate()
	{
		base.WindowUpdate();
		if (!valid())
		{
			Close(doCloseSound: false);
		}
	}

	public override void DoWindowContents(Rect rect)
	{
		Widgets.Label(new Rect(rect.x, rect.y, rect.width, rect.height - 80f), text);
		if (!submitted)
		{
			if (Widgets.ButtonText(new Rect(rect.x, rect.yMax - 60f, rect.width / 2f - 10f, 50f), accept))
			{
				submitted = true;
				choose(obj: true);
			}
			if (Widgets.ButtonText(new Rect(rect.center.x + 10f, rect.yMax - 60f, rect.width / 2f - 10f, 50f), refuse))
			{
				submitted = true;
				choose(obj: false);
			}
		}
	}
}
