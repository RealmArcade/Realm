using System;
using GdUnit4;
using Godot;

namespace Realm.Client.Tests;

/// <summary>
/// Captures the rendered frame of a running <see cref="ISceneRunner"/>.
/// A scene runner only has a viewport while its scene is attached to the tree, so the
/// capture states that requirement once instead of every caller dereferencing the chain.
/// </summary>
internal static class SceneRunnerCapture
{
	public static Image CaptureViewportImage(ISceneRunner runner)
	{
		ArgumentNullException.ThrowIfNull(runner);

		Node scene = runner.Scene() ?? throw new InvalidOperationException("The scene runner has no running scene to capture.");
		Viewport viewport = scene.GetViewport() ?? throw new InvalidOperationException("The running scene is not attached to a viewport.");
		return viewport.GetTexture().GetImage();
	}
}
