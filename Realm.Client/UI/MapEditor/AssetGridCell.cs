using Godot;
using Realm.Client.Services;
using System;
using System.Linq;

namespace Realm.Client.UI.MapEditor;

public partial class AssetGridCell : PanelContainer
{
	private TextureRect _thumbnailRect;
	private Label _titleLabel;
	private Label _badgeLabel;
	private IndexedAsset? _currentAsset;
	private AnimatedThumbnail? _currentAnimatedThumbnail;
	private float _animTimer;
	private int _lastRenderedFrameIndex;
	private bool _isSelected;
	private bool _isHovered;
	private Action<IndexedAsset, bool>? _onSelectCallback;

	public IndexedAsset? CurrentAsset => _currentAsset;

	public AssetGridCell()
	{
		CustomMinimumSize = new Vector2(130, 150);
		MouseFilter = MouseFilterEnum.Stop;
		MouseDefaultCursorShape = CursorShape.PointingHand;

		AddThemeStyleboxOverride("panel", CreatePanelStyle(false, false));

		var mainVBox = new VBoxContainer();
		mainVBox.AddThemeConstantOverride("separation", 4);
		mainVBox.MouseFilter = MouseFilterEnum.Pass;
		AddChild(mainVBox);

		var thumbContainer = new PanelContainer();
		thumbContainer.CustomMinimumSize = new Vector2(110, 90);
		thumbContainer.MouseFilter = MouseFilterEnum.Pass;
		var thumbStyle = new StyleBoxFlat();
		thumbStyle.BgColor = new Color(0.06f, 0.07f, 0.09f, 0.8f);
		thumbStyle.CornerRadiusTopLeft = 3;
		thumbStyle.CornerRadiusTopRight = 3;
		thumbStyle.CornerRadiusBottomLeft = 3;
		thumbStyle.CornerRadiusBottomRight = 3;
		thumbContainer.AddThemeStyleboxOverride("panel", thumbStyle);
		mainVBox.AddChild(thumbContainer);

		_thumbnailRect = new TextureRect();
		_thumbnailRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_thumbnailRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_thumbnailRect.CustomMinimumSize = new Vector2(100, 84);
		_thumbnailRect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_thumbnailRect.SizeFlagsVertical = SizeFlags.ExpandFill;
		_thumbnailRect.MouseFilter = MouseFilterEnum.Pass;
		thumbContainer.AddChild(_thumbnailRect);

		_titleLabel = new Label();
		_titleLabel.AddThemeFontSizeOverride("font_size", 10);
		_titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_titleLabel.MouseFilter = MouseFilterEnum.Pass;
		mainVBox.AddChild(_titleLabel);

		_badgeLabel = new Label();
		_badgeLabel.AddThemeFontSizeOverride("font_size", 9);
		_badgeLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_badgeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_badgeLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_badgeLabel.MouseFilter = MouseFilterEnum.Pass;
		mainVBox.AddChild(_badgeLabel);

		GuiInput += OnCellGuiInput;
		MouseEntered += OnCellMouseEntered;
		MouseExited += OnCellMouseExited;

		AssetThumbnailProvider.ThumbnailGenerated += OnThumbnailGenerated;
	}

	public override void _ExitTree()
	{
		AssetThumbnailProvider.ThumbnailGenerated -= OnThumbnailGenerated;
	}

	public void Bind(IndexedAsset asset, bool isSelected, Action<IndexedAsset, bool> onSelectCallback)
	{
		_currentAsset = asset;
		_isSelected = isSelected;
		_onSelectCallback = onSelectCallback;

		_titleLabel.Text = asset.FileName;

		if (asset.Tags != null && asset.Tags.Count > 0)
		{
			_badgeLabel.Text = string.Join(", ", asset.Tags.Take(2));
		}
		else
		{
			_badgeLabel.Text = asset.Extension.ToUpperInvariant();
		}

		string tagString = (asset.Tags != null && asset.Tags.Count > 0) ? string.Join(", ", asset.Tags) : "None";
		TooltipText = $"{asset.FileName}\n{TranslationServer.Translate("Path")}: {asset.FilePath}\n{TranslationServer.Translate("Size")}: {FormatFileSize(asset.FileSizeBytes)}\n{TranslationServer.Translate("Tags")}: {tagString}";

		string ext = asset.Extension.ToLowerInvariant();
		if (ext == ".ranim")
		{
			_currentAnimatedThumbnail = AssetThumbnailProvider.GetAnimatedThumbnail(asset);
			_thumbnailRect.Texture = _currentAnimatedThumbnail?.PrimaryFrame ?? AssetThumbnailProvider.GetThumbnail(asset, isHighPriority: true);
			_animTimer = 0f;
			_lastRenderedFrameIndex = 0;
		}
		else
		{
			_currentAnimatedThumbnail = null;
			_thumbnailRect.Texture = AssetThumbnailProvider.GetThumbnail(asset, isHighPriority: true);
		}

		UpdateStyle();
	}

	public override void _Process(double delta)
	{
		if (!IsVisibleInTree()) return;

		if (_currentAnimatedThumbnail != null && _currentAnimatedThumbnail.Frames.Count > 1)
		{
			_animTimer += (float)delta;
			int frameCount = _currentAnimatedThumbnail.Frames.Count;
			int frameIndex = (int)((_animTimer * _currentAnimatedThumbnail.Fps) % frameCount);
			if (frameIndex != _lastRenderedFrameIndex && frameIndex >= 0 && frameIndex < frameCount)
			{
				_thumbnailRect.Texture = _currentAnimatedThumbnail.Frames[frameIndex];
				_lastRenderedFrameIndex = frameIndex;
			}
		}
	}

	private void OnThumbnailGenerated(string filePath, Texture2D texture)
	{
		if (_currentAsset != null)
		{
			string currentNorm = AssetThumbnailProvider.NormalizePath(_currentAsset.FilePath);
			string eventNorm = AssetThumbnailProvider.NormalizePath(filePath);
			if (string.Equals(currentNorm, eventNorm, StringComparison.OrdinalIgnoreCase))
			{
				if (_currentAsset.Extension.Equals(".ranim", StringComparison.OrdinalIgnoreCase) && _currentAnimatedThumbnail == null)
				{
					_currentAnimatedThumbnail = AssetThumbnailProvider.GetAnimatedThumbnail(_currentAsset);
				}
				_thumbnailRect.Texture = texture;
			}
		}
	}

	private void OnCellGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (_currentAsset != null)
			{
				_onSelectCallback?.Invoke(_currentAsset, mouseButton.DoubleClick);
			}
		}
	}

	private void OnCellMouseEntered()
	{
		_isHovered = true;
		UpdateStyle();
	}

	private void OnCellMouseExited()
	{
		_isHovered = false;
		UpdateStyle();
	}

	private void UpdateStyle()
	{
		AddThemeStyleboxOverride("panel", CreatePanelStyle(_isSelected, _isHovered));
	}

	private StyleBox CreatePanelStyle(bool isSelected, bool isHovered)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isSelected
			? new Color(0.24f, 0.22f, 0.18f, 0.95f)
			: (isHovered ? new Color(0.18f, 0.19f, 0.22f, 0.9f) : new Color(0.12f, 0.13f, 0.15f, 0.85f));

		if (isSelected)
		{
			style.BorderColor = UIStyle.ColorGold;
			style.SetBorderWidthAll(2);
		}
		else if (isHovered)
		{
			style.BorderColor = UIStyle.ColorCyanGlow;
			style.SetBorderWidthAll(1);
		}
		else
		{
			style.BorderColor = new Color(0.28f, 0.26f, 0.22f, 0.6f);
			style.SetBorderWidthAll(1);
		}

		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;

		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;

		return style;
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes < 1024)
		{
			return $"{bytes} B";
		}
		if (bytes < 1024 * 1024)
		{
			return $"{bytes / 1024.0:F1} KB";
		}
		return $"{bytes / (1024.0 * 1024.0):F2} MB";
	}
}