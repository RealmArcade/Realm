using Godot;

namespace Realm.Client.UI.MapEditor;

public class MapEditorPlacementSettings
{
	private Slider _sldPlacementRotate;
	private Label _lblPlacementRotateValue;
	private Slider _sldPlacementScale;
	private Label _lblPlacementScaleValue;
	private CheckBox _chkRandomRotation;
	private CheckBox _chkRandomScale;
	private CheckBox _chkClumpMode;
	private Slider _sldClumpCount;
	private Label _lblClumpCountValue;
	private Slider _sldClumpScale;
	private Label _lblClumpScaleValue;

	public MapEditorPlacementSettings(Slider sldPlacementRotate, Label lblPlacementRotateValue, Slider sldPlacementScale, Label lblPlacementScaleValue,
		CheckBox chkRandomRotation, CheckBox chkRandomScale, CheckBox chkClumpMode,
		Slider sldClumpCount, Label lblClumpCountValue, Slider sldClumpScale, Label lblClumpScaleValue)
	{
		_sldPlacementRotate = sldPlacementRotate;
		_lblPlacementRotateValue = lblPlacementRotateValue;
		_sldPlacementScale = sldPlacementScale;
		_lblPlacementScaleValue = lblPlacementScaleValue;
		_chkRandomRotation = chkRandomRotation;
		_chkRandomScale = chkRandomScale;
		_chkClumpMode = chkClumpMode;
		_sldClumpCount = sldClumpCount;
		_lblClumpCountValue = lblClumpCountValue;
		_sldClumpScale = sldClumpScale;
		_lblClumpScaleValue = lblClumpScaleValue;

		_sldPlacementRotate.ValueChanged += (val) =>
		{
			_lblPlacementRotateValue.Text = val.ToString("F0") + "°";
			if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorPlacementRotation = (float)val;
		};

		_sldPlacementScale.ValueChanged += (val) =>
		{
			_lblPlacementScaleValue.Text = val.ToString("F1") + "x";
			if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorPlacementScale = (float)val;
		};

		_chkRandomRotation.Toggled += (buttonPressed) =>
		{
			if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorRandomRotation = buttonPressed;
			UpdateVisibility();
		};

		_chkRandomScale.Toggled += (buttonPressed) =>
		{
			if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorRandomScale = buttonPressed;
			UpdateVisibility();
		};

		_chkClumpMode.Toggled += (buttonPressed) =>
		{
			if (buttonPressed && !_chkRandomRotation.ButtonPressed)
			{
				_chkRandomRotation.ButtonPressed = true;
				if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorRandomRotation = true;
			}
			if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorClumpMode = buttonPressed;
			UpdateVisibility();
		};

		if (_sldClumpCount != null)
		{
			_sldClumpCount.ValueChanged += (val) =>
			{
				if (_lblClumpCountValue != null) _lblClumpCountValue.Text = val.ToString("F0");
				if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorClumpCount = (float)val;
			};
		}

		if (_sldClumpScale != null)
		{
			_sldClumpScale.ValueChanged += (val) =>
			{
				if (_lblClumpScaleValue != null) _lblClumpScaleValue.Text = val.ToString("F2");
				if (Realm.Client.Core.GameHost.Instance != null) Realm.Client.Core.GameHost.Instance.EditorClumpScale = (float)val;
			};
		}

		UpdateVisibility();
	}

	private void UpdateVisibility()
	{
		bool randomRotation = _chkRandomRotation.ButtonPressed;
		bool randomScale = _chkRandomScale.ButtonPressed;
		bool isClumpMode = _chkClumpMode.ButtonPressed;

		var rotateContainer = _sldPlacementRotate.GetParent() as Control;
		var scaleContainer = _sldPlacementScale.GetParent() as Control;

		bool rotVis = !randomRotation;
		if (rotateContainer != null && rotateContainer.Visible != rotVis) rotateContainer.Visible = rotVis;

		bool scaleVis = isClumpMode || !randomScale;
		if (scaleContainer != null && scaleContainer.Visible != scaleVis) scaleContainer.Visible = scaleVis;

		if (!_chkRandomRotation.Visible) _chkRandomRotation.Visible = true;
		
		bool chkScaleVis = !isClumpMode;
		if (_chkRandomScale.Visible != chkScaleVis) _chkRandomScale.Visible = chkScaleVis;
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (viewModel == null) return;

		UpdateSliders(viewModel);
		UpdateClumpSliders(viewModel);

		if (UpdateToggles(viewModel))
		{
			UpdateVisibility();
		}
	}

	private void UpdateSliders(MapEditorHUDViewModel viewModel)
	{
		if (!Mathf.IsEqualApprox((float)_sldPlacementRotate.Value, viewModel.PlacementRotate))
		{
			_sldPlacementRotate.Value = viewModel.PlacementRotate;
			_lblPlacementRotateValue.Text = viewModel.PlacementRotate.ToString("F0") + "°";
		}

		if (!Mathf.IsEqualApprox((float)_sldPlacementScale.Value, viewModel.PlacementScale))
		{
			_sldPlacementScale.Value = viewModel.PlacementScale;
			_lblPlacementScaleValue.Text = viewModel.PlacementScale.ToString("F1") + "x";
		}
	}

	private void UpdateClumpSliders(MapEditorHUDViewModel viewModel)
	{
		if (_sldClumpCount != null && !Mathf.IsEqualApprox((float)_sldClumpCount.Value, viewModel.ClumpCount))
		{
			_sldClumpCount.Value = viewModel.ClumpCount;
			if (_lblClumpCountValue != null) _lblClumpCountValue.Text = viewModel.ClumpCount.ToString("F0");
		}

		if (_sldClumpScale != null && !Mathf.IsEqualApprox((float)_sldClumpScale.Value, viewModel.ClumpScale))
		{
			_sldClumpScale.Value = viewModel.ClumpScale;
			if (_lblClumpScaleValue != null) _lblClumpScaleValue.Text = viewModel.ClumpScale.ToString("F2");
		}
	}

	private bool UpdateToggles(MapEditorHUDViewModel viewModel)
	{
		bool stateChanged = false;

		if (_chkRandomRotation.ButtonPressed != viewModel.RandomRotation)
		{
			_chkRandomRotation.ButtonPressed = viewModel.RandomRotation;
			stateChanged = true;
		}

		if (_chkRandomScale.ButtonPressed != viewModel.RandomScale)
		{
			_chkRandomScale.ButtonPressed = viewModel.RandomScale;
			stateChanged = true;
		}

		if (_chkClumpMode.ButtonPressed != viewModel.ClumpMode)
		{
			_chkClumpMode.ButtonPressed = viewModel.ClumpMode;
			stateChanged = true;
		}

		return stateChanged;
	}
}