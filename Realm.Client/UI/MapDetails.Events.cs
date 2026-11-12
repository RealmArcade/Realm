// Event wiring. Each Register* group pairs a set of children with the callbacks that move the
// page: carousel arrows, the download button, the smooth scroll containers, and the hover rows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;
using FileAccess = Godot.FileAccess;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;


namespace Realm.Client.UI;

public partial class MapDetails
{
	private void RegisterEvents()
	{
		RegisterCarouselEvents();
		RegisterDownloadEvents();
		RegisterAllScrollEvents();
		RegisterHoverEvents();
	}

	private void RegisterCarouselEvents()
	{
		_btnLeftArrow.Pressed += () =>
		{
			int mediaCount = GetCarouselMediaCount();
			if (mediaCount <= 1) return;
			_carouselIndex = (_carouselIndex - 1 + mediaCount) % mediaCount;
			_carouselSlider.Value = _carouselIndex;
			UpdateCarousel();
			GameContext.UserInterface?.PlayClickSound();
		};

		_btnRightArrow.Pressed += () =>
		{
			int mediaCount = GetCarouselMediaCount();
			if (mediaCount <= 1) return;
			_carouselIndex = (_carouselIndex + 1) % mediaCount;
			_carouselSlider.Value = _carouselIndex;
			UpdateCarousel();
			GameContext.UserInterface?.PlayClickSound();
		};

		_carouselSlider.ValueChanged += (value) =>
		{
			_carouselIndex = (int)value;
			UpdateCarousel();
		};
	}

	private void RegisterDownloadEvents()
	{
		_versionDropdown.ItemSelected += (index) =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OnVersionSelected((int)index);
		};

		_downloadButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			if (MapProvisioningService.IsLocalCopyPresent(_mapData))
			{
				if (_mapData != null && !string.IsNullOrWhiteSpace(_mapData.Title))
				{
					GameContext.UserInterface?.TransitionToLobbyCreateWithMap(_mapData.Title, _mapData.Version);
				}
				else
				{
					GameContext.UserInterface?.TransitionTo(GameScreen.LobbyCreate);
				}
			}
			else
			{
				StartMapDownloadAsync();
			}
		};
	}

	private void RegisterAllScrollEvents()
	{
		if (_statsScrollContainer != null)
		{
			_targetScrollVertical = _statsScrollContainer.ScrollVertical;
			_statsScrollContainer.GuiInput += (@event) => HandleScrollInput(@event, _statsScrollContainer, ref _targetScrollVertical, 70f);
		}

		if (_descScrollContainer != null)
		{
			_targetDescScrollVertical = _descScrollContainer.ScrollVertical;
			_descScrollContainer.GuiInput += (@event) => HandleScrollInput(@event, _descScrollContainer, ref _targetDescScrollVertical, 50f);
		}

		if (_featuresScrollContainer != null)
		{
			_targetFeaturesScrollVertical = _featuresScrollContainer.ScrollVertical;
			_featuresScrollContainer.GuiInput += (@event) => HandleScrollInput(@event, _featuresScrollContainer, ref _targetFeaturesScrollVertical, 50f);
		}
	}

	private void HandleScrollInput(InputEvent @event, ScrollContainer container, ref float targetScroll, float step)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			var vScroll = container.GetVScrollBar();
			float maxScroll = vScroll != null ? (float)Mathf.Max(0, vScroll.MaxValue - vScroll.Page) : 1000f;

			if (mb.ButtonIndex == MouseButton.WheelUp)
			{
				targetScroll = Mathf.Max(0, targetScroll - step);
				GetViewport().SetInputAsHandled();
			}
			else if (mb.ButtonIndex == MouseButton.WheelDown)
			{
				targetScroll = Mathf.Min(maxScroll, targetScroll + step);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void RegisterHoverEvents()
	{
		_texCenter.MouseDefaultCursorShape = CursorShape.PointingHand;
		_texLeftPeek.MouseDefaultCursorShape = CursorShape.PointingHand;
		_texRightPeek.MouseDefaultCursorShape = CursorShape.PointingHand;

		AttachHoverAnimation(_texCenter, 1.03f);
		AttachHoverAnimation(_texLeftPeek, 1.04f);
		AttachHoverAnimation(_texRightPeek, 1.04f);

		_texLeftPeek.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				_btnLeftArrow.EmitSignal("pressed");
			}
		};

		_texRightPeek.GuiInput += (@event) =>
		{
			if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			{
				_btnRightArrow.EmitSignal("pressed");
			}
		};
	}

}