using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Textures;
using FileAccess = Godot.FileAccess;

namespace Realm.Client;

public partial class VisualProjectile3D : Node3D
{
	private void ReleaseLight()
	{
		if (_hasAcquiredLight)
		{
			_hasAcquiredLight = false;
			GameContext.FXService?.ReleaseProjectileLight();
		}

		if (_pointLight != null)
		{
			_pointLight.Visible = false;
		}
	}

	private void UpdatePointLight()
	{
		if (_weapon.PointLightEnabled && _weapon.PointLightIntensity > 0.01f)
		{
			if (!_hasAcquiredLight && GameContext.FXService != null && GameContext.FXService.TryAcquireProjectileLight())
			{
				_hasAcquiredLight = true;
			}

			if (_hasAcquiredLight)
			{
				_pointLight.Visible = true;
				_pointLight.LightColor = ParseColor(_weapon.PointLightColor, new Color(1.0f, 0.7f, 0.3f));
				_pointLight.LightEnergy = _weapon.PointLightIntensity;
				_pointLight.OmniRange = _weapon.PointLightRange > 0 ? _weapon.PointLightRange : 6.0f;
				_pointLight.OmniAttenuation = 2.0f;
			}
			else
			{
				_pointLight.Visible = false;
			}
		}
		else
		{
			ReleaseLight();
		}
	}

	public float TimeScale { get; set; } = 1.0f;
	public bool IsPaused { get; set; } = false;

}
