using System;
using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Terrain
{
	public enum WaterType : byte
	{
		None = 0,
		Shallow = 1,
		Deep = 2
	}

	/// <summary>
	/// Represents cell quad terrain data including tier, absolute corner heights, and per-cell discrete water state.
	/// </summary>
	public struct TerrainCell
	{
		public const float TIER_HEIGHT = 3.0f;
		public const sbyte MIN_MACRO_TIER = -16;
		public const sbyte MAX_MACRO_TIER = 16;
		public const float MIN_Y = MIN_MACRO_TIER * TIER_HEIGHT;
		public const float MAX_Y = MAX_MACRO_TIER * TIER_HEIGHT;

		private float _centerHeight;
		public float CenterHeight
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _centerHeight;
		}

		private sbyte _macroTier;
		public sbyte MacroTier
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _macroTier;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void UpdateCalculations()
		{
			_centerHeight = Math.Clamp((_yNW + _yNE + _ySE + _ySW) * 0.25f, MIN_Y, MAX_Y);
			_macroTier = (sbyte)Math.Clamp((int)MathF.Round(_centerHeight / TIER_HEIGHT), MIN_MACRO_TIER, MAX_MACRO_TIER);
		}

		private float _yNW;
		public float Y_NW
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _yNW;
			set
			{
				_yNW = Math.Clamp(value, MIN_Y, MAX_Y);
				UpdateCalculations();
			}
		}

		private float _yNE;
		public float Y_NE
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _yNE;
			set
			{
				_yNE = Math.Clamp(value, MIN_Y, MAX_Y);
				UpdateCalculations();
			}
		}

		private float _ySE;
		public float Y_SE
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _ySE;
			set
			{
				_ySE = Math.Clamp(value, MIN_Y, MAX_Y);
				UpdateCalculations();
			}
		}

		private float _ySW;
		public float Y_SW
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _ySW;
			set
			{
				_ySW = Math.Clamp(value, MIN_Y, MAX_Y);
				UpdateCalculations();
			}
		}

		public WaterType WaterMode;
		public byte WaterProfileIndex;

		public TerrainCell(float nw, float ne, float se, float sw, WaterType waterMode = WaterType.None, byte waterProfileIndex = 0)
		{
			_yNW = Math.Clamp(nw, MIN_Y, MAX_Y);
			_yNE = Math.Clamp(ne, MIN_Y, MAX_Y);
			_ySE = Math.Clamp(se, MIN_Y, MAX_Y);
			_ySW = Math.Clamp(sw, MIN_Y, MAX_Y);
			_centerHeight = Math.Clamp((_yNW + _yNE + _ySE + _ySW) * 0.25f, MIN_Y, MAX_Y);
			_macroTier = (sbyte)Math.Clamp((int)MathF.Round(_centerHeight / TIER_HEIGHT), MIN_MACRO_TIER, MAX_MACRO_TIER);
			WaterMode = waterMode;
			WaterProfileIndex = waterProfileIndex;
		}

		public TerrainCell(float uniformHeight, WaterType waterMode = WaterType.None, byte waterProfileIndex = 0)
			: this(uniformHeight, uniformHeight, uniformHeight, uniformHeight, waterMode, waterProfileIndex) { }
	}
}

