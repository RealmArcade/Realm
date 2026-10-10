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
		if (_goldLabel != null) _goldLabel.Text = $"{viewModel.Gold:F0}";
		if (_woodLabel != null) _woodLabel.Text = $"{viewModel.Wood:F0}";
		if (_stoneLabel != null) _stoneLabel.Text = $"{viewModel.Stone:F0}";

		if (_populationLabel != null)
		{
			_populationLabel.Text = $"{viewModel.CurrentPopulation} / {viewModel.MaxPopulation}";
			_populationLabel.AddThemeColorOverride("font_color", viewModel.CurrentPopulation >= viewModel.MaxPopulation ? new Color(1f, 0.3f, 0.3f) : UIStyle.ColorGoldDull);
		}

		if (_clockLabel != null)
		{
			_clockLabel.Text = viewModel.ClockText;
		}
	}
}