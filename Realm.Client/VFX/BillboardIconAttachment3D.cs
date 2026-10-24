using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Metadata;

namespace Realm.Client.VFX;

[GlobalClass]
public partial class BillboardIconAttachment3D : AnimatedSprite3D
{
	private BillboardIconMetadata? _config;

	public static BillboardIconAttachment3D? TryCreate(BillboardIconMetadata? config)
	{
		if (config == null || string.IsNullOrWhiteSpace(config.TexturePath)) return null;

		var billboard = new BillboardIconAttachment3D();
		if (!billboard.ApplyConfiguration(config))
		{
			billboard.Free();
			return null;
		}

		billboard.Name = "BillboardAttachment";
		return billboard;
	}

	public bool ApplyConfiguration(BillboardIconMetadata config)
	{
		_config = config;

		var frames = GameContext.FXService.TryGetSpritesheetFrames(
			config.TexturePath,
			config.Columns,
			config.Rows,
			config.FramesPerSecond,
			config.Loop);

		if (frames == null)
		{
			GD.PrintErr($"[BillboardIconAttachment3D] Could not load spritesheet '{config.TexturePath}'.");
			return false;
		}

		Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		Transparent = true;
		Shaded = false;
		DoubleSided = true;
		NoDepthTest = config.NoDepthTest;
		AlphaCut = SpriteBase3D.AlphaCutMode.Disabled;
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;

		int frameWidth = Math.Max(1, frames.Value.FrameWidth);
		FixedSize = config.KeepConstantScreenSize;
		PixelSize = Math.Max(0.0001f, config.WorldHeight) / frameWidth;

		SpriteFrames = frames.Value.Frames;
		Animation = "play";
		Frame = 0;
		Play("play");
		return true;
	}

	public Vector3 ResolvePositionOffset()
	{
		return _config != null ? _config.PositionOffset.ToGodotVector3() : Vector3.Zero;
	}

	public string ResolveSocketId()
	{
		return !string.IsNullOrWhiteSpace(_config?.Socket) ? _config!.Socket! : "Overhead";
	}
}
