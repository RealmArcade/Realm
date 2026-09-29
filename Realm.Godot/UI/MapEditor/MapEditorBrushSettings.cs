using Godot;
using Realm.Ecs.Components.Terrain;
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

	private OptionButton _optWaterMode;

	public MapEditorBrushSettings(Slider sldBrushSize, Label lblBrushSizeValue, Slider sldBrushStrength, Label lblBrushStrengthValue,
		CheckBox chkBlockMode, Slider sldBlockStep, Label lblBlockStepValue,
		OptionButton optWaterMode = null)
	{
		_sldBrushSize = sldBrushSize;
		_lblBrushSizeValue = lblBrushSizeValue;
		_sldBrushStrength = sldBrushStrength;
		_lblBrushStrengthValue = lblBrushStrengthValue;

		_chkBlockMode = chkBlockMode;
		_sldBlockStep = sldBlockStep;
		_lblBlockStepValue = lblBlockStepValue;

		_optWaterMode = optWaterMode;

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

		if (!Mathf.IsEqualApprox((float)_sldBrushSize.Value, viewModel.BrushSize))
		{
			_sldBrushSize.Value = viewModel.BrushSize;
			_lblBrushSizeValue.Text = viewModel.BrushSize.ToString("F0");
		}

		if (!Mathf.IsEqualApprox((float)_sldBrushStrength.Value, viewModel.BrushStrength))
		{
			_sldBrushStrength.Value = viewModel.BrushStrength;
			_lblBrushStrengthValue.Text = viewModel.BrushStrength.ToString("F0");
		}

		if (_chkBlockMode != null && GameHost.Instance != null)
		{
			bool isBlock = GameHost.Instance.EditorBlockMode;
			if (_chkBlockMode.ButtonPressed != isBlock)
			{
				_chkBlockMode.ButtonPressed = isBlock;
			}
		}

		if (_sldBlockStep != null)
		{
			if (!Mathf.IsEqualApprox((float)_sldBlockStep.Value, viewModel.BlockStep))
			{
				_sldBlockStep.Value = viewModel.BlockStep;
				_lblBlockStepValue.Text = viewModel.BlockStep.ToString("F1") + "m";
			}
		}

		if (_optWaterMode != null && GameHost.Instance != null)
		{
			WaterType waterMode = GameHost.Instance.EditorWaterMode;
			byte activeProf = GameHost.Instance.ActiveWaterProfileIndex;
			int targetSelected = 0;
			if (waterMode != WaterType.None)
			{
				for (int i = 1; i < _optWaterMode.ItemCount; i++)
				{
					var meta = _optWaterMode.GetItemMetadata(i);
					if (meta.VariantType != Variant.Type.Nil && (byte)(int)meta == activeProf)
					{
						targetSelected = i;
						break;
					}
				}
				if (targetSelected == 0 && _optWaterMode.ItemCount > 1)
				{
					targetSelected = 1;
				}
			}
			if (_optWaterMode.Selected != targetSelected)
			{
				_optWaterMode.Selected = targetSelected;
			}
		}
	}
}
