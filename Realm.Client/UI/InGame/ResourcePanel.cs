using System;
using Godot;

namespace Realm.Client.UI.InGame;

public class ResourcePanel
{
	private PanelContainer _container;
	private Label _goldLabel;
	private Label _woodLabel;
	private Label _stoneLabel;
	private Label _populationLabel;
	private Label _clockLabel;

	private int _lastGold = int.MinValue;
	private int _lastWood = int.MinValue;
	private int _lastStone = int.MinValue;
	private int _lastCurPop = int.MinValue;
	private int _lastMaxPop = int.MinValue;
	private bool _lastPopCapped;
	private bool _popColorInitialized;
	private string? _lastClockText;

	public ResourcePanel(PanelContainer container)
	{
		_container = container;
		_goldLabel = container.GetNode<Label>("HBox/GoldBox/GoldLabel");
		_woodLabel = container.GetNode<Label>("HBox/WoodBox/WoodLabel");
		_stoneLabel = container.GetNode<Label>("HBox/StoneBox/StoneLabel");
	}

	public void InitializeSupplyAndClock(Label populationLabel, Label clockLabel)
	{
		_populationLabel = populationLabel;
		_clockLabel = clockLabel;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (_goldLabel != null)
		{
			int goldInt = (int)MathF.Round(viewModel.Gold);
			if (goldInt != _lastGold)
			{
				_goldLabel.Text = goldInt.ToString();
				_lastGold = goldInt;
			}
		}

		if (_woodLabel != null)
		{
			int woodInt = (int)MathF.Round(viewModel.Wood);
			if (woodInt != _lastWood)
			{
				_woodLabel.Text = woodInt.ToString();
				_lastWood = woodInt;
			}
		}

		if (_stoneLabel != null)
		{
			int stoneInt = (int)MathF.Round(viewModel.Stone);
			if (stoneInt != _lastStone)
			{
				_stoneLabel.Text = stoneInt.ToString();
				_lastStone = stoneInt;
			}
		}

		if (_populationLabel != null)
		{
			int curPop = viewModel.CurrentPopulation;
			int maxPop = viewModel.MaxPopulation;
			if (curPop != _lastCurPop || maxPop != _lastMaxPop)
			{
				_populationLabel.Text = $"{curPop} / {maxPop}";
				_lastCurPop = curPop;
				_lastMaxPop = maxPop;
			}

			bool capped = curPop >= maxPop;
			if (!_popColorInitialized || capped != _lastPopCapped)
			{
				_populationLabel.AddThemeColorOverride("font_color", capped ? new Color(1f, 0.3f, 0.3f) : UIStyle.ColorGoldDull);
				_lastPopCapped = capped;
				_popColorInitialized = true;
			}
		}

		if (_clockLabel != null)
		{
			string clockText = viewModel.ClockText ?? string.Empty;
			if (clockText != _lastClockText)
			{
				_clockLabel.Text = clockText;
				_lastClockText = clockText;
			}
		}
	}
}