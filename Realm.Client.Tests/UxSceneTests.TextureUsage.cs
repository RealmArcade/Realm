using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Client.UI;
using Realm.Client.Utils;
using Environment = System.Environment;
using Realm.Client.Services;
namespace Realm.Client.Tests;

/// <summary>measuring how each texture is drawn.</summary>
public partial class UxSceneTests
	{
	private static void CollectTextureUsages(Node root, Dictionary<string, ImageRenderUsage> usages, HashSet<string> nineSliceFileNames)
	{
		var pendingNodes = new Stack<Node>();
		pendingNodes.Push(root);

		while (pendingNodes.Count > 0)
		{
			Node node = pendingNodes.Pop();

			if (node is Control control)
			{
				CollectControlTextureUsages(control, usages, nineSliceFileNames);
			}

			foreach (Node child in node.GetChildren())
			{
				pendingNodes.Push(child);
			}
		}
	}

	private static void CollectControlTextureUsages(Control control, Dictionary<string, ImageRenderUsage> usages, HashSet<string> nineSliceFileNames)
	{
		bool isLayoutResolved = IsLayoutResolved(control, control.Size);

		if (control is TextureRect textureRect && textureRect.Texture != null)
		{
			RecordTextureUsage(textureRect.Texture, ComputeTextureRectDrawnSize(textureRect), usages, false, nineSliceFileNames);
		}

		if (control is Button button && button.Icon != null)
		{
			Vector2 iconSize = button.Icon.GetSize();
			int iconMaxWidth = button.GetThemeConstant("icon_max_width");
			if (iconMaxWidth > 0 && iconSize.X > iconMaxWidth)
			{
				iconSize *= iconMaxWidth / iconSize.X;
			}
			else if (button.ExpandIcon)
			{
				iconSize = button.Size;
			}

			RecordTextureUsage(button.Icon, iconSize, usages, false, nineSliceFileNames);
		}

		if (!isLayoutResolved)
		{
			return;
		}

		if (control is TextureButton textureButton)
		{
			RecordTextureUsage(textureButton.TextureNormal, control.Size, usages, false, nineSliceFileNames);
			RecordTextureUsage(textureButton.TexturePressed, control.Size, usages, false, nineSliceFileNames);
			RecordTextureUsage(textureButton.TextureHover, control.Size, usages, false, nineSliceFileNames);
			RecordTextureUsage(textureButton.TextureDisabled, control.Size, usages, false, nineSliceFileNames);
		}

		foreach (string themeName in StyleBoxThemeNames)
		{
			StyleBox styleBox = control.GetThemeStylebox(themeName);
			if (styleBox is StyleBoxTexture styleBoxTexture)
			{
				bool hasTextureMargins = Mathf.Max(Mathf.Max(styleBoxTexture.TextureMarginLeft, styleBoxTexture.TextureMarginRight), Mathf.Max(styleBoxTexture.TextureMarginTop, styleBoxTexture.TextureMarginBottom)) > 0.5f;
				RecordTextureUsage(styleBoxTexture.Texture, control.Size, usages, hasTextureMargins, nineSliceFileNames);
			}
		}
	}

	private static bool IsLayoutResolved(Control control, Vector2 drawnSize)
	{
		if (!control.IsVisibleInTree())
		{
			return false;
		}

		if (control.Size.X < 1.0f || control.Size.Y < 1.0f || drawnSize.X < 1.0f || drawnSize.Y < 1.0f)
		{
			return false;
		}

		Vector2 combinedMinimumSize = control.GetCombinedMinimumSize();
		return control.Size.X + 0.5f >= combinedMinimumSize.X && control.Size.Y + 0.5f >= combinedMinimumSize.Y;
	}

	private static Vector2 ComputeTextureRectDrawnSize(TextureRect textureRect)
	{
		Vector2 textureSize = textureRect.Texture.GetSize();
		if (textureRect.ExpandMode != TextureRect.ExpandModeEnum.IgnoreSize)
		{
			return textureRect.IsVisibleInTree() ? textureSize : Vector2.Zero;
		}

		Vector2 availableSize = textureRect.Size;
		if (!IsLayoutResolved(textureRect, availableSize))
		{
			return Vector2.Zero;
		}

		switch (textureRect.StretchMode)
		{
			case TextureRect.StretchModeEnum.Scale:
			case TextureRect.StretchModeEnum.KeepAspectCovered:
				return availableSize;
			case TextureRect.StretchModeEnum.KeepAspect:
			case TextureRect.StretchModeEnum.KeepAspectCentered:
				return FitSizeToAspect(textureSize, availableSize);
			default:
				return textureSize;
		}
	}

	private static Vector2 FitSizeToAspect(Vector2 textureSize, Vector2 availableSize)
	{
		if (textureSize.X < 1.0f || textureSize.Y < 1.0f)
		{
			return availableSize;
		}

		float scale = Mathf.Min(availableSize.X / textureSize.X, availableSize.Y / textureSize.Y);
		return textureSize * scale;
	}
}
