using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class UtilitiesDialog : FloatingDialogBase
{
	public UtilitiesDialog() { }

	public interface IUtilityOperation
	{
		string Name { get; }
		string Title { get; }
		string Description { get; }
		string Icon { get; }
		Control CreateConfigurationControl();
		Task ExecuteAsync(IProgress<(double Progress, string Status)> progress);
	}

	public class AutoFillAssetsUtilityOperation : IUtilityOperation
	{
		public string Name => "Auto-Fill Missing Assets";
		public string Title => TranslationServer.Translate("Auto-Fill Missing Assets");
		public string Description => TranslationServer.Translate("Scans the active map's metadata templates and queries the local Content Addressable Storage (CAS) asset index to randomly assign matching models, textures, animations, and audio clips to templates with missing asset references.");
		public string Icon => UnicodeIcons.DICE;

		private CheckBox _chkOnlyFillEmpty;

		public Control CreateConfigurationControl()
		{
			var container = new VBoxContainer();
			container.Name = "Container_VBoxContainer";
			container.AddThemeConstantOverride("separation", 8);

			var lblConfigHeader = new Label();
			lblConfigHeader.Name = "LblConfigHeader_Label";
			lblConfigHeader.Text = TranslationServer.Translate("CONFIGURATION");
			lblConfigHeader.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			lblConfigHeader.AddThemeFontSizeOverride("font_size", 12);
			container.AddChild(lblConfigHeader);

			_chkOnlyFillEmpty = new CheckBox();
			_chkOnlyFillEmpty.Name = "ChkOnlyFillEmpty_CheckBox";
			_chkOnlyFillEmpty.Text = TranslationServer.Translate("Only fill empty fields (preserve existing assignments)");
			_chkOnlyFillEmpty.ButtonPressed = true;
			_chkOnlyFillEmpty.AddThemeFontSizeOverride("font_size", 11);
			_chkOnlyFillEmpty.TooltipText = TranslationServer.Translate("When checked, templates that already have valid asset assignments will not be changed. When unchecked, existing assignments are re-rolled.");
			container.AddChild(_chkOnlyFillEmpty);

			return container;
		}

		public async Task ExecuteAsync(IProgress<(double Progress, string Status)> progress)
		{
			bool overwriteExisting = _chkOnlyFillEmpty != null && !_chkOnlyFillEmpty.ButtonPressed;
			await Task.Run(() =>
			{
				GameContext.EditorService.AssignRandomAssetsToTemplates(overwriteExisting, (current, total, slug) =>
				{
					double pct = total > 0 ? (double)current / total * 100.0 : 0.0;
					string status = !string.IsNullOrEmpty(slug)
						? $"{TranslationServer.Translate("Processing")}: {slug} ({current}/{total})"
						: $"{TranslationServer.Translate("Completed")}: {total}/{total}";
					progress?.Report((pct, status));
				});
			});
		}
	}

	private readonly List<IUtilityOperation> _operations = new();
	private IUtilityOperation _selectedOperation;

	private VBoxContainer _sidebarVBox;
	private Label _lblDetailTitle;
	private Label _lblDetailDescription;
	private VBoxContainer _configContainer;
	private VBoxContainer _progressContainer;
	private Label _lblProgressStatus;
	private ProgressBar _progressBar;
	private Button _btnRunUtility;
	private Button _btnCloseFooter;
	private readonly List<Button> _operationButtons = new();
	private bool _isRunning = false;

	public UtilitiesDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Map Utilities & Maintenance"), new Vector2(720, 560))
	{
		RegisterOperations();
		BuildControls();
	}

	private void RegisterOperations()
	{
		_operations.Add(new AutoFillAssetsUtilityOperation());
	}

	private void BuildControls()
	{
		ApplyButton.Visible = false;
		CancelButton.Visible = false;

		var splitHBox = new HBoxContainer();
		splitHBox.Name = "SplitHBox_HBoxContainer";
		splitHBox.AddThemeConstantOverride("separation", 12);
		splitHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		splitHBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		BodyContainer.AddChild(splitHBox);

		var leftPanel = new PanelContainer();
		leftPanel.Name = "LeftPanel_PanelContainer";
		leftPanel.CustomMinimumSize = new Vector2(210, 0);
		leftPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		leftPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		splitHBox.AddChild(leftPanel);

		var leftVBox = new VBoxContainer();
		leftVBox.Name = "LeftVBox_VBoxContainer";
		leftVBox.AddThemeConstantOverride("separation", 8);
		leftPanel.AddChild(leftVBox);

		var lblSidebarTitle = new Label();
		lblSidebarTitle.Name = "LblSidebarTitle_Label";
		lblSidebarTitle.Text = TranslationServer.Translate("OPERATIONS");
		lblSidebarTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		lblSidebarTitle.AddThemeFontSizeOverride("font_size", 12);
		leftVBox.AddChild(lblSidebarTitle);

		var scrollSidebar = new ScrollContainer();
		scrollSidebar.Name = "ScrollSidebar_ScrollContainer";
		scrollSidebar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollSidebar.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollSidebar.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		leftVBox.AddChild(scrollSidebar);

		_sidebarVBox = new VBoxContainer();
		_sidebarVBox.Name = "SidebarVBox_VBoxContainer";
		_sidebarVBox.AddThemeConstantOverride("separation", 4);
		_sidebarVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollSidebar.AddChild(_sidebarVBox);

		var rightPanel = new PanelContainer();
		rightPanel.Name = "RightPanel_PanelContainer";
		rightPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		rightPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		splitHBox.AddChild(rightPanel);

		var rightScroll = new ScrollContainer();
		rightScroll.Name = "RightScroll_ScrollContainer";
		rightScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		rightScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		rightPanel.AddChild(rightScroll);

		var rightVBox = new VBoxContainer();
		rightVBox.Name = "RightVBox_VBoxContainer";
		rightVBox.AddThemeConstantOverride("separation", 12);
		rightVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightScroll.AddChild(rightVBox);

		_lblDetailTitle = new Label();
		_lblDetailTitle.Name = "LblDetailTitle_Label";
		_lblDetailTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_lblDetailTitle.AddThemeFontSizeOverride("font_size", 15);
		rightVBox.AddChild(_lblDetailTitle);

		_lblDetailDescription = new Label();
		_lblDetailDescription.Name = "LblDetailDescription_Label";
		_lblDetailDescription.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_lblDetailDescription.AddThemeFontSizeOverride("font_size", 11);
		_lblDetailDescription.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblDetailDescription.CustomMinimumSize = new Vector2(400, 0);
		_lblDetailDescription.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightVBox.AddChild(_lblDetailDescription);

		var disclaimerPanel = new PanelContainer();
		disclaimerPanel.Name = "DisclaimerPanel_PanelContainer";
		disclaimerPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		var disclaimerStyle = new StyleBoxFlat();
		disclaimerStyle.BgColor = new Color(0.28f, 0.16f, 0.08f, 0.85f);
		disclaimerStyle.BorderColor = new Color(0.85f, 0.55f, 0.2f, 0.95f);
		disclaimerStyle.SetBorderWidthAll(1);
		disclaimerStyle.CornerRadiusTopLeft = 4;
		disclaimerStyle.CornerRadiusTopRight = 4;
		disclaimerStyle.CornerRadiusBottomLeft = 4;
		disclaimerStyle.CornerRadiusBottomRight = 4;
		disclaimerStyle.ContentMarginLeft = 12;
		disclaimerStyle.ContentMarginRight = 12;
		disclaimerStyle.ContentMarginTop = 8;
		disclaimerStyle.ContentMarginBottom = 8;
		disclaimerPanel.AddThemeStyleboxOverride("panel", disclaimerStyle);

		var disclaimerHBox = new HBoxContainer();
		disclaimerHBox.Name = "DisclaimerHBox_HBoxContainer";
		disclaimerHBox.AddThemeConstantOverride("separation", 8);
		disclaimerPanel.AddChild(disclaimerHBox);

		var lblWarnIcon = new Label();
		lblWarnIcon.Name = "LblWarnIcon_Label";
		lblWarnIcon.Text = "⚠️";
		lblWarnIcon.AddThemeFontSizeOverride("font_size", 14);
		disclaimerHBox.AddChild(lblWarnIcon);

		var lblDisclaimer = new Label();
		lblDisclaimer.Name = "LblDisclaimer_Label";
		lblDisclaimer.Text = TranslationServer.Translate("Safety Notice: Bulk utility operations modify map files directly. Commit to Git or make a backup copy before executing bulk modifications.");
		lblDisclaimer.AddThemeFontSizeOverride("font_size", 10);
		lblDisclaimer.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.65f));
		lblDisclaimer.AutowrapMode = TextServer.AutowrapMode.Word;
		lblDisclaimer.CustomMinimumSize = new Vector2(360, 0);
		lblDisclaimer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		disclaimerHBox.AddChild(lblDisclaimer);

		rightVBox.AddChild(disclaimerPanel);

		_configContainer = new VBoxContainer();
		_configContainer.Name = "ConfigContainer_VBoxContainer";
		_configContainer.AddThemeConstantOverride("separation", 8);
		_configContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rightVBox.AddChild(_configContainer);

		_progressContainer = new VBoxContainer();
		_progressContainer.Name = "ProgressContainer_VBoxContainer";
		_progressContainer.AddThemeConstantOverride("separation", 6);
		_progressContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_progressContainer.Visible = false;

		_lblProgressStatus = new Label();
		_lblProgressStatus.Name = "LblProgressStatus_Label";
		_lblProgressStatus.AddThemeFontSizeOverride("font_size", 11);
		_lblProgressStatus.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_progressContainer.AddChild(_lblProgressStatus);

		_progressBar = new ProgressBar();
		_progressBar.Name = "ProgressBar_ProgressBar";
		_progressBar.CustomMinimumSize = new Vector2(0, 22);
		_progressBar.MinValue = 0;
		_progressBar.MaxValue = 100;
		_progressBar.Value = 0;
		_progressBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_progressContainer.AddChild(_progressBar);

		rightVBox.AddChild(_progressContainer);

		_btnRunUtility = new Button();
		_btnRunUtility.Name = "BtnRunUtility_Button";
		_btnRunUtility.Set("icon_max_width", 0);
		_btnRunUtility.Text = $"{UnicodeIcons.PLAY} " + TranslationServer.Translate("Run Utility");
		_btnRunUtility.CustomMinimumSize = new Vector2(130, 30);
		_btnRunUtility.FocusMode = FocusModeEnum.None;
		var faFont = Hud?.GetFontAwesomeFont();
		if (faFont != null) _btnRunUtility.AddThemeFontOverride("font", faFont);
		_btnRunUtility.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_btnRunUtility.Pressed += OnRunUtilityPressed;
		FooterHBox.AddChild(_btnRunUtility);

		_btnCloseFooter = new Button();
		_btnCloseFooter.Name = "BtnCloseFooter_Button";
		_btnCloseFooter.Set("icon_max_width", 0);
		_btnCloseFooter.Text = TranslationServer.Translate("CLOSE");
		_btnCloseFooter.CustomMinimumSize = new Vector2(90, 30);
		_btnCloseFooter.FocusMode = FocusModeEnum.None;
		_btnCloseFooter.Pressed += OnClosePressed;
		FooterHBox.AddChild(_btnCloseFooter);

		PopulateSidebar();
		if (_operations.Count > 0)
		{
			SelectOperation(_operations[0]);
		}
	}

	private void PopulateSidebar()
	{
		_operationButtons.Clear();
		foreach (var child in _sidebarVBox.GetChildren())
		{
			child.QueueFree();
		}

		var faFont = Hud?.GetFontAwesomeFont();
		for (int i = 0; i < _operations.Count; i++)
		{
			var op = _operations[i];
			var btn = new Button();
			btn.Name = "Btn_Button";
			btn.Set("icon_max_width", 0);
			btn.Text = $"{op.Icon} {op.Title}";
			btn.Alignment = HorizontalAlignment.Left;
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.CustomMinimumSize = new Vector2(0, 32);
			btn.FocusMode = FocusModeEnum.None;
			btn.AddThemeFontSizeOverride("font_size", 11);
			if (faFont != null) btn.AddThemeFontOverride("font", faFont);

			btn.Pressed += () => SelectOperation(op);
			_sidebarVBox.AddChild(btn);
			_operationButtons.Add(btn);
		}
	}

	private void SelectOperation(IUtilityOperation operation)
	{
		if (_isRunning) return;

		_selectedOperation = operation;

		for (int i = 0; i < _operations.Count; i++)
		{
			var btn = _operationButtons[i];
			bool isSelected = _operations[i] == operation;
			btn.AddThemeColorOverride("font_color", isSelected ? UIStyle.ColorGold : Colors.White);
		}

		_lblDetailTitle.Text = operation.Title;
		_lblDetailDescription.Text = operation.Description;

		foreach (var child in _configContainer.GetChildren())
		{
			child.QueueFree();
		}

		var configControl = operation.CreateConfigurationControl();
		if (configControl != null)
		{
			_configContainer.AddChild(configControl);
		}

		_progressContainer.Visible = false;
	}

	private void SetRunningState(bool running)
	{
		_isRunning = running;
		_btnRunUtility.Disabled = running;
		_btnCloseFooter.Disabled = running;
		CloseButton.Disabled = running;
		foreach (var btn in _operationButtons)
		{
			btn.Disabled = running;
		}

		_progressContainer.Visible = running;
	}

	private void OnClosePressed()
	{
		if (_isRunning) return;
		CloseDialog();
	}

	public override void CancelAndClose()
	{
		if (_isRunning) return;
		base.CancelAndClose();
	}

	private async void OnRunUtilityPressed()
	{
		if (_selectedOperation == null || _isRunning) return;

		string opName = _selectedOperation.Name;
		SetRunningState(true);

		_progressBar.Value = 0;
		_lblProgressStatus.Text = $"{TranslationServer.Translate("Starting")} {opName}...";

		var progressReporter = new Progress<(double Progress, string Status)>(update =>
		{
			if (GodotObject.IsInstanceValid(this) && GodotObject.IsInstanceValid(_progressBar) && GodotObject.IsInstanceValid(_lblProgressStatus))
			{
				_progressBar.Value = Mathf.Clamp(update.Progress, 0, 100);
				_lblProgressStatus.Text = update.Status;
			}
		});

		try
		{
			await _selectedOperation.ExecuteAsync(progressReporter);
		}
		finally
		{
			SetRunningState(false);
		}

		CloseDialog();

		Hud?.RefreshAllInspectors();
		Hud?.ShowFeedback($"'{opName}' Operation Completed");
	}
}
