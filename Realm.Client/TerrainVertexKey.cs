using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Realm.Client;

public readonly struct TerrainVertexKey : IEquatable<TerrainVertexKey>
{
	private readonly int _positionX;
	private readonly int _positionY;
	private readonly int _positionZ;
	private readonly int _normalX;
	private readonly int _normalY;
	private readonly int _normalZ;
	private readonly int _textureU;
	private readonly int _textureV;
	private readonly int _colorAlpha;
	private readonly int _quadCliff;
	private readonly int _gTex0;
	private readonly int _gTex1;
	private readonly int _gTex2;
	private readonly int _gTex3;
	private readonly int _gw0;
	private readonly int _gw1;
	private readonly int _gw2;
	private readonly int _gw3;
	private readonly int _cTex0;
	private readonly int _cTex1;
	private readonly int _cTex2;
	private readonly int _cTex3;
	private readonly int _cw0;
	private readonly int _cw1;
	private readonly int _cw2;
	private readonly int _cw3;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int FastRound(float v, float scale) => (int)(v * scale + (v >= 0f ? 0.5f : -0.5f));

	public TerrainVertexKey(
		Vector3 position,
		Vector3 normal,
		Vector2 uv,
		Color color,
		float gTex0, float gTex1, float gTex2, float gTex3,
		float gw0, float gw1, float gw2, float gw3,
		float cTex0, float cTex1, float cTex2, float cTex3,
		float cw0, float cw1, float cw2, float cw3)
	{
		_positionX = FastRound(position.X, 2000.0f);
		_positionY = FastRound(position.Y, 2000.0f);
		_positionZ = FastRound(position.Z, 2000.0f);
		_normalX = FastRound(normal.X, 1000.0f);
		_normalY = FastRound(normal.Y, 1000.0f);
		_normalZ = FastRound(normal.Z, 1000.0f);
		_textureU = FastRound(uv.X, 1000.0f);
		_textureV = FastRound(uv.Y, 1000.0f);
		_colorAlpha = FastRound(color.A, 1000.0f);
		_quadCliff = (int)(color.R + (color.R >= 0f ? 0.5f : -0.5f));
		_gTex0 = (int)(gTex0 + (gTex0 >= 0f ? 0.5f : -0.5f));
		_gTex1 = (int)(gTex1 + (gTex1 >= 0f ? 0.5f : -0.5f));
		_gTex2 = (int)(gTex2 + (gTex2 >= 0f ? 0.5f : -0.5f));
		_gTex3 = (int)(gTex3 + (gTex3 >= 0f ? 0.5f : -0.5f));
		_gw0 = FastRound(gw0, 1000.0f);
		_gw1 = FastRound(gw1, 1000.0f);
		_gw2 = FastRound(gw2, 1000.0f);
		_gw3 = FastRound(gw3, 1000.0f);
		_cTex0 = (int)(cTex0 + (cTex0 >= 0f ? 0.5f : -0.5f));
		_cTex1 = (int)(cTex1 + (cTex1 >= 0f ? 0.5f : -0.5f));
		_cTex2 = (int)(cTex2 + (cTex2 >= 0f ? 0.5f : -0.5f));
		_cTex3 = (int)(cTex3 + (cTex3 >= 0f ? 0.5f : -0.5f));
		_cw0 = FastRound(cw0, 1000.0f);
		_cw1 = FastRound(cw1, 1000.0f);
		_cw2 = FastRound(cw2, 1000.0f);
		_cw3 = FastRound(cw3, 1000.0f);
	}

	public bool Equals(TerrainVertexKey other)
	{
		return EqualsPosition(other) &&
		       EqualsNormal(other) &&
		       EqualsTextureAndColor(other) &&
		       EqualsGroundTextures(other) &&
		       EqualsCliffTextures(other);
	}

	private bool EqualsPosition(TerrainVertexKey other)
	{
		return _positionX == other._positionX &&
		       _positionY == other._positionY &&
		       _positionZ == other._positionZ;
	}

	private bool EqualsNormal(TerrainVertexKey other)
	{
		return _normalX == other._normalX &&
		       _normalY == other._normalY &&
		       _normalZ == other._normalZ;
	}

	private bool EqualsTextureAndColor(TerrainVertexKey other)
	{
		return _textureU == other._textureU &&
		       _textureV == other._textureV &&
		       _colorAlpha == other._colorAlpha &&
		       _quadCliff == other._quadCliff;
	}

	private bool EqualsGroundTextures(TerrainVertexKey other)
	{
		return _gTex0 == other._gTex0 &&
		       _gTex1 == other._gTex1 &&
		       _gTex2 == other._gTex2 &&
		       _gTex3 == other._gTex3 &&
		       _gw0 == other._gw0 &&
		       _gw1 == other._gw1 &&
		       _gw2 == other._gw2 &&
		       _gw3 == other._gw3;
	}

	private bool EqualsCliffTextures(TerrainVertexKey other)
	{
		return _cTex0 == other._cTex0 &&
		       _cTex1 == other._cTex1 &&
		       _cTex2 == other._cTex2 &&
		       _cTex3 == other._cTex3 &&
		       _cw0 == other._cw0 &&
		       _cw1 == other._cw1 &&
		       _cw2 == other._cw2 &&
		       _cw3 == other._cw3;
	}

	public override bool Equals(object? obj)
	{
		return obj is TerrainVertexKey other && Equals(other);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + _positionX;
			hash = hash * 31 + _positionY;
			hash = hash * 31 + _positionZ;
			hash = hash * 31 + _normalX;
			hash = hash * 31 + _normalY;
			hash = hash * 31 + _normalZ;
			hash = hash * 31 + _textureU;
			hash = hash * 31 + _textureV;
			hash = hash * 31 + _colorAlpha;
			hash = hash * 31 + _quadCliff;
			hash = hash * 31 + _gTex0;
			hash = hash * 31 + _gw0;
			hash = hash * 31 + _cTex0;
			hash = hash * 31 + _cw0;
			return hash;
		}
	}
}