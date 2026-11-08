using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Animation;

namespace Realm.Client;
public partial class Unit3D
{
	private static bool TryResolveBillboardIconAttachment(
		string attachmentId,
		ref float defaultScale,
		ref Vector3 defaultPos,
		ref Vector3 defaultRot,
		out Node3D? billboardIcon)
	{
		billboardIcon = null;
		if (!GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var attachmentMetadata)) return false;
		if (attachmentMetadata.BillboardIcon == null || string.IsNullOrWhiteSpace(attachmentMetadata.BillboardIcon.TexturePath)) return false;

		var createdIcon = BillboardIconAttachment3D.TryCreate(attachmentMetadata.BillboardIcon);
		if (createdIcon == null) return false;

		defaultScale = 1.0f;
		defaultPos = Vector3.Zero;
		defaultRot = Vector3.Zero;
		createdIcon.SetMeta("BillboardSocket", createdIcon.ResolveSocketId());
		billboardIcon = createdIcon;
		return true;
	}

	private static ProceduralVfxInstance3D? ResolveVfxAttachment(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, ref Vector3 defaultRot)
	{
		if (attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			string vfxKey = attachmentId.Substring(4);
			VfxAttachmentConfig config;
			if (GameContext.RegistryService.VfxRegistry.TryGetValue(vfxKey, out var regCfg)) config = regCfg.Clone();
			else if (Enum.TryParse<VfxPrimitiveType>(vfxKey, true, out var primType)) config = new VfxAttachmentConfig { VfxId = vfxKey, PrimitiveType = primType };
			else config = new VfxAttachmentConfig { VfxId = vfxKey, Name = vfxKey };

			defaultScale = 1.0f;
			defaultPos = config.PositionOffset.ToGodotVector3();
			defaultRot = config.RotationOffset.ToGodotVector3();
			var vfxInstance = new ProceduralVfxInstance3D(config);
			vfxInstance.Name = "AttachmentVfx_ProceduralVfxInstance3D";
			return vfxInstance;
		}

		if (GameContext.RegistryService.VfxRegistry.TryGetValue(attachmentId, out var vfxCfg))
		{
			defaultScale = 1.0f;
			defaultPos = vfxCfg.PositionOffset.ToGodotVector3();
			defaultRot = vfxCfg.RotationOffset.ToGodotVector3();
			var vfxInstance = new ProceduralVfxInstance3D(vfxCfg.Clone());
			vfxInstance.Name = "AttachmentVfx_ProceduralVfxInstance3D";
			return vfxInstance;
		}

		if (Enum.TryParse<VfxPrimitiveType>(attachmentId, true, out var parsedPrim))
		{
			var cfg = new VfxAttachmentConfig { VfxId = attachmentId, PrimitiveType = parsedPrim };
			defaultScale = 1.0f;
			defaultPos = cfg.PositionOffset.ToGodotVector3();
			defaultRot = cfg.RotationOffset.ToGodotVector3();
			var vfxInstance = new ProceduralVfxInstance3D(cfg);
			vfxInstance.Name = "AttachmentVfx_ProceduralVfxInstance3D";
			return vfxInstance;
		}

		return null;
	}

	private static Node3D? ResolveModelAttachment(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, ref Vector3 defaultRot, out AttachmentMetadata? attMeta)
	{
		attMeta = null;

		if (TryResolveFromAttachmentRegistry(attachmentId, ref defaultScale, ref defaultPos, ref defaultRot, out attMeta, out string modelPath))
		{
			return LoadModelAttachment(modelPath, attachmentId);
		}

		if (TryResolveFromPropRegistry(attachmentId, ref defaultScale, ref defaultPos, out modelPath))
		{
			return LoadModelAttachment(modelPath, attachmentId);
		}

		if (TryResolveFromResourceRegistry(attachmentId, ref defaultScale, ref defaultPos, out modelPath))
		{
			return LoadModelAttachment(modelPath, attachmentId);
		}

		return LoadModelAttachment(string.Empty, attachmentId);
	}

	private static bool TryResolveFromAttachmentRegistry(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, ref Vector3 defaultRot, out AttachmentMetadata? attMeta, out string modelPath)
	{
		attMeta = null;
		modelPath = string.Empty;
		if (!GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var meta)) return false;

		attMeta = meta;
		modelPath = meta.ModelPath;
		defaultScale = meta.Scale <= 0f ? 1.0f : meta.Scale;
		defaultPos = meta.PositionOffset.ToGodotVector3();
		defaultRot = meta.RotationOffset.ToGodotVector3();
		return true;
	}

	private static bool TryResolveFromPropRegistry(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, out string modelPath)
	{
		modelPath = string.Empty;
		if (!GameHost.PropRegistry.TryGetValue(attachmentId, out var propMeta)) return false;
		if (string.IsNullOrEmpty(propMeta.ModelPath)) return false;

		modelPath = propMeta.ModelPath;
		defaultScale = propMeta.Scale <= 0f ? 1.0f : propMeta.Scale;
		defaultPos = new Vector3(0f, propMeta.YOffset, 0f);
		return true;
	}

	private static bool TryResolveFromResourceRegistry(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, out string modelPath)
	{
		modelPath = string.Empty;
		if (!GameHost.ResourceRegistry.TryGetValue(attachmentId, out var resMeta)) return false;
		if (string.IsNullOrEmpty(resMeta.ModelPath)) return false;

		modelPath = resMeta.ModelPath;
		defaultScale = resMeta.Scale <= 0f ? 1.0f : resMeta.Scale;
		defaultPos = new Vector3(0f, resMeta.YOffset, 0f);
		return true;
	}

	private static Node3D? LoadModelAttachment(string modelPath, string attachmentId)
	{
		Node? loaded = !string.IsNullOrEmpty(modelPath) ? ModelCache.GetModel(modelPath) : null;
		if (loaded == null) loaded = ModelCache.GetModel(attachmentId);
		if (loaded is Node3D n) return n;
		if (loaded != null) loaded.QueueFree();
		return null;
	}

	private static void ApplyChildVfx(Node3D loadedNode, AttachmentMetadata attMeta)
	{
		if (attMeta.ChildVfxId is not { Length: > 0 } childVfxId) return;

		var childVfx = ResolveAndInstantiateAttachment(childVfxId, out _, out _, out _);
		if (childVfx == null) return;

		childVfx.Position = attMeta.ChildVfxPosition.ToGodotVector3();
		childVfx.RotationDegrees = attMeta.ChildVfxRotation.ToGodotVector3();
		var childScale = attMeta.ChildVfxScale.ToGodotVector3();
		childVfx.Scale = childScale == Vector3.Zero ? Vector3.One : childScale;
		string cleanChildId = childVfxId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? childVfxId : Path.GetFileNameWithoutExtension(childVfxId);
		childVfx.Name = $"ChildVfx_{cleanChildId}";
		childVfx.SetMeta("AttachmentId", childVfxId);
		childVfx.SetMeta("CleanAttachmentId", cleanChildId);
		loadedNode.AddChild(childVfx);
	}


}
