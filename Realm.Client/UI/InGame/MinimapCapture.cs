using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using System;
using System.Threading.Tasks;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The minimap's terrain background: a capture of the world from above, taken over two frames because the viewport
///     has to settle first. Captures are serialised — a request that arrives while one is running marks the running one
///     to go round again, so the last request is never dropped and never runs twice in parallel.
/// </summary>
public sealed class MinimapCapture
{
	private readonly Node _host;
	private readonly Func<Control?> _area;

	private bool _isGenerating;
	private bool _needsRegen;

	public MinimapCapture(Node host, Func<Control?> area)
	{
		_host = host;
		_area = area;
	}

	/// <summary>The overlay that draws the shroud and unit dots; asking it to redraw is all a colour change needs.</summary>
	public MinimapOverlay? Overlay { get; set; }

	public void QueueRedraw() => Overlay?.QueueRedraw();

	public async Task RegenerateAsync()
	{
		if (_isGenerating)
		{
			_needsRegen = true;
			return;
		}

		_isGenerating = true;

		try
		{
			do
			{
				_needsRegen = false;
				await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
				await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);

				Control? area = _area();
				if (area == null) return;

				TextureRect? minimapBg = area.GetChildCount() > 0 ? area.GetChild<TextureRect>(0) : null;
				if (minimapBg == null) return;

				Texture2D? imgTexture = await MinimapHelper.CaptureTerrainMinimapTextureAsync(_host, 256);
				if (imgTexture != null)
				{
					minimapBg.Texture = imgTexture;
				}
			} while (_needsRegen);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to dynamically capture terrain minimap: {ex.Message}");
		}
		finally
		{
			_isGenerating = false;
		}
	}
}
