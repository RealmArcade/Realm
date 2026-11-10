using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.UI.MapEditor;

public partial class VfxStudioDialog
{
	private void BuildParticleEmissionControls(VBoxContainer parent)
	{
		AddSectionHeader(parent, "✨ " + TranslationServer.Translate("PARTICLE EMISSION & DYNAMICS"), new Color(0.95f, 0.75f, 0.35f));

		string[] shapeNames = Enum.GetNames<SpellParticleShape>();
		_optParticleShape = AddOptionDropdown(parent, TranslationServer.Translate("Emitter Shape"), shapeNames, 0, (idx) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.EmitterShape = (SpellParticleShape)idx;
			RestartPreviewVfx();
		}, 140f);

		(_sliderParticleAmount, _lblParticleAmount) = AddSlider(parent, TranslationServer.Translate("Particle Count"), 1f, 512f, 1f, 32f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.Amount = (int)val;
			RestartPreviewVfx();
		}, "0", 140f);

		(_sliderParticleLifetime, _lblParticleLifetime) = AddSlider(parent, TranslationServer.Translate("Lifetime (sec)"), 0.1f, 10.0f, 0.1f, 1.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.Lifetime = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderParticleExplosiveness, _lblParticleExplosiveness) = AddSlider(parent, TranslationServer.Translate("Explosiveness"), 0.0f, 1.0f, 0.05f, 0.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.Explosiveness = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		_chkParticleLocalCoords = AddCheckBox(parent, TranslationServer.Translate("Local Coordinates"), false, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.LocalCoords = val;
			RestartPreviewVfx();
		}, "Particles move with parent rather than drifting in world space");
	}

	private void BuildParticleVelocityControls(VBoxContainer parent)
	{
		AddSectionHeader(parent, "💨 " + TranslationServer.Translate("VELOCITY, SPREAD & FORCES"), new Color(0.45f, 0.85f, 0.95f));

		(_txtParticleDirX, _txtParticleDirY, _txtParticleDirZ) = AddVector3Input(parent, TranslationServer.Translate("Emitter Direction"), Vector3.Up, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.Direction = val.ToVector3Data();
			RestartPreviewVfx();
		}, 140f);

		(_sliderParticleSpread, _lblParticleSpread) = AddSlider(parent, TranslationServer.Translate("Spread (Degrees)"), 0.0f, 180.0f, 1.0f, 45.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.SpreadDegrees = val;
			RestartPreviewVfx();
		}, "0", 140f);

		(_sliderParticleVelMin, _lblParticleVelMin) = AddSlider(parent, TranslationServer.Translate("Velocity Min"), 0.0f, 50.0f, 0.2f, 1.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.InitialVelocityMin = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderParticleVelMax, _lblParticleVelMax) = AddSlider(parent, TranslationServer.Translate("Velocity Max"), 0.0f, 50.0f, 0.2f, 3.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.InitialVelocityMax = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_txtParticleGravX, _txtParticleGravY, _txtParticleGravZ) = AddVector3Input(parent, TranslationServer.Translate("Gravity"), new Vector3(0, -4f, 0), (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.Gravity = val.ToVector3Data();
			RestartPreviewVfx();
		}, 140f);

		(_sliderParticleDamping, _lblParticleDamping) = AddSlider(parent, TranslationServer.Translate("Linear Damping"), 0.0f, 20.0f, 0.1f, 0.5f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.Damping = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderParticleRadialAccel, _lblParticleRadialAccel) = AddSlider(parent, TranslationServer.Translate("Radial Acceleration"), -50.0f, 50.0f, 0.5f, 0.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.RadialAccel = val;
			RestartPreviewVfx();
		}, "0.0", 140f);

		(_sliderParticleTangentialAccel, _lblParticleTangentialAccel) = AddSlider(parent, TranslationServer.Translate("Tangential Accel"), -50.0f, 50.0f, 0.5f, 0.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.TangentialAccel = val;
			RestartPreviewVfx();
		}, "0.0", 140f);
	}

	private void BuildParticleAppearanceControls(VBoxContainer parent)
	{
		AddSectionHeader(parent, "🎨 " + TranslationServer.Translate("PARTICLE APPEARANCE & RAMP"), new Color(0.95f, 0.55f, 0.45f));

		(_sliderParticleScaleMin, _lblParticleScaleMin) = AddSlider(parent, TranslationServer.Translate("Initial Scale Min"), 0.01f, 5.0f, 0.05f, 0.2f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.InitialScaleMin = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderParticleScaleMax, _lblParticleScaleMax) = AddSlider(parent, TranslationServer.Translate("Initial Scale Max"), 0.01f, 5.0f, 0.05f, 0.4f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.InitialScaleMax = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_sliderParticleEndScaleRatio, _lblParticleEndScaleRatio) = AddSlider(parent, TranslationServer.Translate("End Scale Ratio"), 0.0f, 3.0f, 0.05f, 0.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.EndScaleRatio = val;
			RestartPreviewVfx();
		}, "0.00", 140f);

		(_pickerParticleColorStart, _sliderParticleColorStartHue) = AddColorPicker(parent, TranslationServer.Translate("Color Start"), Colors.Gold, (c) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.ColorStart = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_pickerParticleColorMid, _sliderParticleColorMidHue) = AddColorPicker(parent, TranslationServer.Translate("Color Mid"), Colors.DarkOrange, (c) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.ColorMid = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_pickerParticleColorEnd, _sliderParticleColorEndHue) = AddColorPicker(parent, TranslationServer.Translate("Color End"), Colors.Maroon, (c) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.ColorEnd = "#" + c.ToHtml(false);
			RestartPreviewVfx();
		}, 140f);

		(_sliderParticleEmissionEnergy, _lblParticleEmissionEnergy) = AddSlider(parent, TranslationServer.Translate("Emission Energy"), 0.0f, 20.0f, 0.2f, 3.0f, (val) =>
		{
			if (_isUpdatingUI) return;
			EnsureParticleConfig();
			Particle.EmissionEnergy = val;
			RestartPreviewVfx();
		}, "0.0", 140f);
	}

	private void BuildParticleControls(VBoxContainer parent)
	{
		BuildParticleEmissionControls(parent);
		BuildParticleVelocityControls(parent);
		BuildParticleAppearanceControls(parent);
	}

	private void EnsureParticleConfig()
	{
		if (_currentConfig.ParticleConfig == null)
		{
			_currentConfig.ParticleConfig = new SpellParticleConfig
			{
				ParticleId = _currentConfig.VfxId,
				Name = _currentConfig.Name
			};
		}
	}

}
