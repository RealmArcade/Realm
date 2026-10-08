using Godot;
using System;

public class MapEditorBrushSettings
{
	private Slider _sldBrushSize;
	private Label _lblBrushSizeValue;
	private Slider _sldBrushStrength;
	private Label _lblBrushStrengthValue;

	private CheckBox _chkBlockMode;
	private Slider _sldBlockStep;
	private Label _lblBlockStepValue;
	private Slider? _sldHeight;
	private Label? _lblHeightValue;

	public MapEditorBrushSettings(Slider sldBrushSize, Label lblBrushSizeValue, Slider sldBrushStrength, Label lblBrushStrengthValue,
		CheckBox chkBlockMode, Slider sldBlockStep, Label lblBlockStepValue, Slider? sldHeight = null, Label? lblHeightValue = null)
	{
		_sldBrushSize = sldBrushSize;
		_lblBrushSizeValue = lblBrushSizeValue;
		_sldBrushStrength = sldBrushStrength;
		_lblBrushStrengthValue = lblBrushStrengthValue;

		_chkBlockMode = chkBlockMode;
		_sldBlockStep = sldBlockStep;
		_lblBlockStepValue = lblBlockStepValue;
		_sldHeight = sldHeight;
		_lblHeightValue = lblHeightValue;

		_sldBrushSize.Step = 1.0;
		_sldBrushSize.Rounded = true;

		_sldBrushSize.ValueChanged += (val) =>
		{
			_lblBrushSizeValue.Text = val.ToString("F0");
			if (GameHost.Instance != null) GameHost.Instance.EditorBrushRadius = (float)val;
		};

		_sldBrushStrength.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			if (GameHost.Instance != null && GameHost.Instance.ActiveEditorTool == GameHost.EditorTool.PaintTexture)
			{
				MapEditorHUD.SavedTextureIntensity = fVal;
				_lblBrushStrengthValue.Text = fVal.ToString("F0");
			}
			else
			{
				MapEditorHUD.SavedBrushStrength = fVal;
				_lblBrushStrengthValue.Text = fVal.ToString("F1");
			}
			if (GameHost.Instance != null) GameHost.Instance.EditorBrushStrength = fVal;
		};

		if (_chkBlockMode != null)
		{
			_chkBlockMode.Toggled += (buttonPressed) =>
			{
				if (GameHost.Instance != null) GameHost.Instance.EditorBlockMode = buttonPressed;
			};
		}

		if (_sldHeight != null)
		{
			_sldHeight.ValueChanged += (val) =>
			{
				float fVal = (float)val;
				if (_lblHeightValue != null) _lblHeightValue.Text = fVal.ToString("F1") + "m";
				if (GameHost.Instance != null) GameHost.Instance.EditorExactHeight = fVal;
			};
		}

		if (_sldBlockStep != null)
		{
			_sldBlockStep.ValueChanged += (val) =>
			{
				float fVal = (float)val;
				_lblBlockStepValue.Text = fVal.ToString("F1") + "m";
				if (GameHost.Instance != null) GameHost.Instance.EditorBlockLevelHeight = fVal;
			};
		}
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (viewModel == null) return;

		UpdateBrushSize(viewModel);
		UpdateBrushStrength(viewModel);
		UpdateBlockMode();
		UpdateExactHeight(viewModel);
		UpdateBlockStep(viewModel);
	}

	private void UpdateBrushSize(MapEditorHUDViewModel viewModel)
	{
		if (Mathf.IsEqualApprox((float)_sldBrushSize.Value, viewModel.BrushSize)) return;

		_sldBrushSize.Value = viewModel.BrushSize;
		_lblBrushSizeValue.Text = viewModel.BrushSize.ToString("F0");
	}

	private void UpdateBrushStrength(MapEditorHUDViewModel viewModel)
	{
		if (Mathf.IsEqualApprox((float)_sldBrushStrength.Value, viewModel.BrushStrength)) return;

		_sldBrushStrength.Value = viewModel.BrushStrength;
		_lblBrushStrengthValue.Text = viewModel.BrushStrength.ToString("F0");
	}

	private void UpdateBlockMode()
	{
		if (_chkBlockMode == null || GameHost.Instance == null) return;

		bool isBlock = GameHost.Instance.EditorBlockMode;
		if (_chkBlockMode.ButtonPressed != isBlock)
		{
			_chkBlockMode.ButtonPressed = isBlock;
		}
	}

	private void UpdateExactHeight(MapEditorHUDViewModel viewModel)
	{
		if (_sldHeight == null) return;
		if (Mathf.IsEqualApprox((float)_sldHeight.Value, viewModel.ExactHeight)) return;

		_sldHeight.Value = viewModel.ExactHeight;
		if (_lblHeightValue != null) _lblHeightValue.Text = viewModel.ExactHeight.ToString("F1") + "m";
	}

	private void UpdateBlockStep(MapEditorHUDViewModel viewModel)
	{
		if (_sldBlockStep == null) return;
		if (Mathf.IsEqualApprox((float)_sldBlockStep.Value, viewModel.BlockStep)) return;

		_sldBlockStep.Value = viewModel.BlockStep;
		_lblBlockStepValue.Text = viewModel.BlockStep.ToString("F1") + "m";
	}
}
