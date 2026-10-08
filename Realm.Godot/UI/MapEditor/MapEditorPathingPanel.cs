using Godot;
using System;

public class MapEditorPathingPanel
{
	private CheckBox _chkShallowWater;
	private CheckBox _chkDeepWater;
	private CheckBox _chkFlying;
	private CheckBox _chkGround;
	private CheckBox _chkBuildable;
	private OptionButton _optPathingMode;

	public MapEditorPathingPanel(CheckBox chkShallowWater, CheckBox chkDeepWater, CheckBox chkFlying,
		CheckBox chkGround, CheckBox chkBuildable, OptionButton optPathingMode)
	{
		_chkShallowWater = chkShallowWater;
		_chkDeepWater = chkDeepWater;
		_chkFlying = chkFlying;
		_chkGround = chkGround;
		_chkBuildable = chkBuildable;
		_optPathingMode = optPathingMode;

		if (_chkShallowWater != null)
			_chkShallowWater.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.ShallowWater = val; };
		if (_chkDeepWater != null)
			_chkDeepWater.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.DeepWater = val; };
		if (_chkFlying != null)
			_chkFlying.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.Flying = val; };
		if (_chkGround != null)
			_chkGround.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.Ground = val; };
		if (_chkBuildable != null)
			_chkBuildable.Toggled += (val) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.Buildable = val; };
		if (_optPathingMode != null)
			_optPathingMode.ItemSelected += (idx) => { if (MapEditorHUD.Instance?.ViewModel != null) MapEditorHUD.Instance.ViewModel.PathingModeIndex = (int)idx; };
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (viewModel == null) return;
		UpdateCheckBox(_chkShallowWater, viewModel.ShallowWater);
		UpdateCheckBox(_chkDeepWater, viewModel.DeepWater);
		UpdateCheckBox(_chkFlying, viewModel.Flying);
		UpdateCheckBox(_chkGround, viewModel.Ground);
		UpdateCheckBox(_chkBuildable, viewModel.Buildable);
		UpdatePathingMode(viewModel.PathingModeIndex);
	}

	private void UpdateCheckBox(CheckBox checkBox, bool value)
	{
		if (checkBox == null || checkBox.ButtonPressed == value) return;
		checkBox.ButtonPressed = value;
	}

	private void UpdatePathingMode(int modeIndex)
	{
		if (_optPathingMode == null || _optPathingMode.Selected == modeIndex || modeIndex >= _optPathingMode.ItemCount) return;
		_optPathingMode.Selected = modeIndex;
	}
}
