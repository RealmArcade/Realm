using Godot;
using System;
using System.Collections.Generic;

namespace Realm.Client.VFX;

public class ProceduralVfxMeshGenerator
{
	private static readonly Dictionary<VfxPrimitiveType, ArrayMesh> PrimitiveMeshCache = new();
	private static readonly object SyncLock = new();

	public static ArrayMesh GetMesh(VfxPrimitiveType primitiveType)
	{
		lock (SyncLock)
		{
			if (PrimitiveMeshCache.TryGetValue(primitiveType, out var cachedMesh) && cachedMesh != null && GodotObject.IsInstanceValid(cachedMesh))
			{
				return cachedMesh;
			}

			ArrayMesh generatedMesh = GeneratePrimitiveMesh(primitiveType);
			PrimitiveMeshCache[primitiveType] = generatedMesh;
			return generatedMesh;
		}
	}

	public static void ClearMeshCache()
	{
		lock (SyncLock)
		{
			PrimitiveMeshCache.Clear();
		}
	}

	private static readonly Dictionary<VfxPrimitiveType, Func<ArrayMesh>> PrimitiveBuilders = new()
	{
		{ VfxPrimitiveType.VortexDisc, BuildVortexDiscMesh },
		{ VfxPrimitiveType.FunnelCone, BuildFunnelConeMesh },
		{ VfxPrimitiveType.RibbonRing, BuildRibbonRingMesh },
		{ VfxPrimitiveType.HemisphereDome, BuildHemisphereDomeMesh },
		{ VfxPrimitiveType.GroundPlane, BuildGroundPlaneMesh },
		{ VfxPrimitiveType.WeaponFin, BuildWeaponFinMesh },
		{ VfxPrimitiveType.CrossQuad, BuildCrossQuadMesh },
		{ VfxPrimitiveType.SlashArc, BuildSlashArcMesh },
		{ VfxPrimitiveType.LightShaft, BuildLightShaftMesh },
		{ VfxPrimitiveType.AuraCapsule, BuildAuraCapsuleMesh },
		{ VfxPrimitiveType.AuraSphere, BuildAuraSphereMesh },
		{ VfxPrimitiveType.ProjectedVolumeCube, BuildProjectedVolumeCubeMesh }
	};

	public static ArrayMesh GeneratePrimitiveMesh(VfxPrimitiveType primitiveType)
	{
		if (PrimitiveBuilders.TryGetValue(primitiveType, out var builder))
		{
			return builder();
		}

		return BuildVortexDiscMesh();
	}

	private static ArrayMesh BuildProjectedVolumeCubeMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		Vector3[] vertices = new Vector3[]
		{
			// Front face (Z = 0.5)
			new Vector3(-0.5f, -0.5f,  0.5f),
			new Vector3( 0.5f, -0.5f,  0.5f),
			new Vector3( 0.5f,  0.5f,  0.5f),
			new Vector3(-0.5f,  0.5f,  0.5f),

			// Back face (Z = -0.5)
			new Vector3( 0.5f, -0.5f, -0.5f),
			new Vector3(-0.5f, -0.5f, -0.5f),
			new Vector3(-0.5f,  0.5f, -0.5f),
			new Vector3( 0.5f,  0.5f, -0.5f),

			// Left face (X = -0.5)
			new Vector3(-0.5f, -0.5f, -0.5f),
			new Vector3(-0.5f, -0.5f,  0.5f),
			new Vector3(-0.5f,  0.5f,  0.5f),
			new Vector3(-0.5f,  0.5f, -0.5f),

			// Right face (X = 0.5)
			new Vector3( 0.5f, -0.5f,  0.5f),
			new Vector3( 0.5f, -0.5f, -0.5f),
			new Vector3( 0.5f,  0.5f, -0.5f),
			new Vector3( 0.5f,  0.5f,  0.5f),

			// Top face (Y = 0.5)
			new Vector3(-0.5f,  0.5f,  0.5f),
			new Vector3( 0.5f,  0.5f,  0.5f),
			new Vector3( 0.5f,  0.5f, -0.5f),
			new Vector3(-0.5f,  0.5f, -0.5f),

			// Bottom face (Y = -0.5)
			new Vector3(-0.5f, -0.5f, -0.5f),
			new Vector3( 0.5f, -0.5f, -0.5f),
			new Vector3( 0.5f, -0.5f,  0.5f),
			new Vector3(-0.5f, -0.5f,  0.5f)
		};

		Vector3[] normals = new Vector3[]
		{
			-Vector3.Back,    -Vector3.Back,    -Vector3.Back,    -Vector3.Back,
			-Vector3.Forward, -Vector3.Forward, -Vector3.Forward, -Vector3.Forward,
			-Vector3.Left,    -Vector3.Left,    -Vector3.Left,    -Vector3.Left,
			-Vector3.Right,   -Vector3.Right,   -Vector3.Right,   -Vector3.Right,
			-Vector3.Up,      -Vector3.Up,      -Vector3.Up,      -Vector3.Up,
			-Vector3.Down,    -Vector3.Down,    -Vector3.Down,    -Vector3.Down
		};

		Vector2[] uvs = new Vector2[]
		{
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
			new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
		};

		for (int i = 0; i < vertices.Length; i++)
		{
			surfaceTool.SetNormal(normals[i]);
			surfaceTool.SetUV(uvs[i]);
			surfaceTool.AddVertex(vertices[i]);
		}

		for (int face = 0; face < 6; face++)
		{
			int baseIdx = face * 4;
			surfaceTool.AddIndex(baseIdx + 0);
			surfaceTool.AddIndex(baseIdx + 1);
			surfaceTool.AddIndex(baseIdx + 2);

			surfaceTool.AddIndex(baseIdx + 0);
			surfaceTool.AddIndex(baseIdx + 2);
			surfaceTool.AddIndex(baseIdx + 3);
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildVortexDiscMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int angularSegments = 48;
		const int radialRings = 8;
		const float maxRadius = 1.0f;

		for (int r = 0; r <= radialRings; r++)
		{
			float radiusFraction = (float)r / radialRings;
			float radius = radiusFraction * maxRadius;

			for (int a = 0; a <= angularSegments; a++)
			{
				float angleFraction = (float)a / angularSegments;
				float angleRadians = angleFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(angleRadians) * radius;
				float posZ = MathF.Sin(angleRadians) * radius;

				surfaceTool.SetNormal(Vector3.Up);
				surfaceTool.SetTangent(new Plane(-MathF.Sin(angleRadians), 0.0f, MathF.Cos(angleRadians), 1.0f));
				surfaceTool.SetUV(new Vector2(angleFraction, radiusFraction));
				surfaceTool.AddVertex(new Vector3(posX, 0.0f, posZ));
			}
		}

		int ringVertexCount = angularSegments + 1;
		for (int r = 0; r < radialRings; r++)
		{
			int currentRingOffset = r * ringVertexCount;
			int nextRingOffset = (r + 1) * ringVertexCount;

			for (int a = 0; a < angularSegments; a++)
			{
				int currentInner = currentRingOffset + a;
				int nextInner = currentRingOffset + a + 1;
				int currentOuter = nextRingOffset + a;
				int nextOuter = nextRingOffset + a + 1;

				surfaceTool.AddIndex(currentInner);
				surfaceTool.AddIndex(currentOuter);
				surfaceTool.AddIndex(nextInner);

				surfaceTool.AddIndex(nextInner);
				surfaceTool.AddIndex(currentOuter);
				surfaceTool.AddIndex(nextOuter);
			}
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildFunnelConeMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int angularSegments = 48;
		const int radialRings = 8;
		const float maxRadius = 1.0f;
		const float funnelDepth = 0.5f;

		for (int r = 0; r <= radialRings; r++)
		{
			float radiusFraction = (float)r / radialRings;
			float radius = radiusFraction * maxRadius;
			float posY = -MathF.Pow(1.0f - radiusFraction, 1.35f) * funnelDepth;

			for (int a = 0; a <= angularSegments; a++)
			{
				float angleFraction = (float)a / angularSegments;
				float angleRadians = angleFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(angleRadians) * radius;
				float posZ = MathF.Sin(angleRadians) * radius;

				Vector3 radialNormal = new Vector3(MathF.Cos(angleRadians) * 0.35f, 0.95f, MathF.Sin(angleRadians) * 0.35f).Normalized();
				surfaceTool.SetNormal(radialNormal);
				surfaceTool.SetTangent(new Plane(-MathF.Sin(angleRadians), 0.0f, MathF.Cos(angleRadians), 1.0f));
				surfaceTool.SetUV(new Vector2(angleFraction, radiusFraction));
				surfaceTool.AddVertex(new Vector3(posX, posY, posZ));
			}
		}

		int ringVertexCount = angularSegments + 1;
		for (int r = 0; r < radialRings; r++)
		{
			int currentRingOffset = r * ringVertexCount;
			int nextRingOffset = (r + 1) * ringVertexCount;

			for (int a = 0; a < angularSegments; a++)
			{
				int currentInner = currentRingOffset + a;
				int nextInner = currentRingOffset + a + 1;
				int currentOuter = nextRingOffset + a;
				int nextOuter = nextRingOffset + a + 1;

				surfaceTool.AddIndex(currentInner);
				surfaceTool.AddIndex(currentOuter);
				surfaceTool.AddIndex(nextInner);

				surfaceTool.AddIndex(nextInner);
				surfaceTool.AddIndex(currentOuter);
				surfaceTool.AddIndex(nextOuter);
			}
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildRibbonRingMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int angularSegments = 48;
		const int verticalSegments = 4;
		const float radius = 1.0f;
		const float height = 0.6f;
		const float halfHeight = height * 0.5f;

		for (int v = 0; v <= verticalSegments; v++)
		{
			float heightFraction = (float)v / verticalSegments;
			float posY = -halfHeight + heightFraction * height;

			for (int a = 0; a <= angularSegments; a++)
			{
				float angleFraction = (float)a / angularSegments;
				float angleRadians = angleFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(angleRadians) * radius;
				float posZ = MathF.Sin(angleRadians) * radius;

				surfaceTool.SetNormal(new Vector3(MathF.Cos(angleRadians), 0.0f, MathF.Sin(angleRadians)));
				surfaceTool.SetTangent(new Plane(-MathF.Sin(angleRadians), 0.0f, MathF.Cos(angleRadians), 1.0f));
				surfaceTool.SetUV(new Vector2(angleFraction, heightFraction));
				surfaceTool.AddVertex(new Vector3(posX, posY, posZ));
			}
		}

		int ringVertexCount = angularSegments + 1;
		for (int v = 0; v < verticalSegments; v++)
		{
			int currentLevelOffset = v * ringVertexCount;
			int nextLevelOffset = (v + 1) * ringVertexCount;

			for (int a = 0; a < angularSegments; a++)
			{
				int bLeft = currentLevelOffset + a;
				int bRight = currentLevelOffset + a + 1;
				int tLeft = nextLevelOffset + a;
				int tRight = nextLevelOffset + a + 1;

				surfaceTool.AddIndex(bLeft);
				surfaceTool.AddIndex(tLeft);
				surfaceTool.AddIndex(bRight);

				surfaceTool.AddIndex(bRight);
				surfaceTool.AddIndex(tLeft);
				surfaceTool.AddIndex(tRight);
			}
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildHemisphereDomeMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int latitudeRings = 24;
		const int longitudeSegments = 48;
		const float radius = 1.0f;

		for (int lat = 0; lat <= latitudeRings; lat++)
		{
			float latFraction = (float)lat / latitudeRings;
			float phi = latFraction * MathF.PI * 0.5f;
			float cosPhi = MathF.Cos(phi);
			float sinPhi = MathF.Sin(phi);

			for (int lon = 0; lon <= longitudeSegments; lon++)
			{
				float lonFraction = (float)lon / longitudeSegments;
				float theta = lonFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(theta) * cosPhi * radius;
				float posY = sinPhi * radius;
				float posZ = MathF.Sin(theta) * cosPhi * radius;

				Vector3 normal = new Vector3(posX, posY, posZ).Normalized();
				surfaceTool.SetNormal(normal);
				surfaceTool.SetTangent(new Plane(-MathF.Sin(theta), 0.0f, MathF.Cos(theta), 1.0f));
				surfaceTool.SetUV(new Vector2(lonFraction, latFraction));
				surfaceTool.AddVertex(new Vector3(posX, posY, posZ));
			}
		}

		int ringVertexCount = longitudeSegments + 1;
		for (int lat = 0; lat < latitudeRings; lat++)
		{
			int currentRing = lat * ringVertexCount;
			int nextRing = (lat + 1) * ringVertexCount;

			for (int lon = 0; lon < longitudeSegments; lon++)
			{
				int i0 = currentRing + lon;
				int i1 = currentRing + lon + 1;
				int i2 = nextRing + lon;
				int i3 = nextRing + lon + 1;

				surfaceTool.AddIndex(i0);
				surfaceTool.AddIndex(i2);
				surfaceTool.AddIndex(i1);

				surfaceTool.AddIndex(i1);
				surfaceTool.AddIndex(i2);
				surfaceTool.AddIndex(i3);
			}
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildGroundPlaneMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int subdivisions = 8;
		const float planeHalfSize = 1.0f;

		for (int z = 0; z <= subdivisions; z++)
		{
			float fractionZ = (float)z / subdivisions;
			float posZ = -planeHalfSize + fractionZ * (planeHalfSize * 2.0f);

			for (int x = 0; x <= subdivisions; x++)
			{
				float fractionX = (float)x / subdivisions;
				float posX = -planeHalfSize + fractionX * (planeHalfSize * 2.0f);

				surfaceTool.SetNormal(Vector3.Up);
				surfaceTool.SetTangent(new Plane(1.0f, 0.0f, 0.0f, 1.0f));
				surfaceTool.SetUV(new Vector2(fractionX, fractionZ));
				surfaceTool.AddVertex(new Vector3(posX, 0.0f, posZ));
			}
		}

		int rowVertexCount = subdivisions + 1;
		for (int z = 0; z < subdivisions; z++)
		{
			int currentRow = z * rowVertexCount;
			int nextRow = (z + 1) * rowVertexCount;

			for (int x = 0; x < subdivisions; x++)
			{
				int topLeft = currentRow + x;
				int topRight = currentRow + x + 1;
				int bottomLeft = nextRow + x;
				int bottomRight = nextRow + x + 1;

				surfaceTool.AddIndex(topLeft);
				surfaceTool.AddIndex(bottomLeft);
				surfaceTool.AddIndex(topRight);

				surfaceTool.AddIndex(topRight);
				surfaceTool.AddIndex(bottomLeft);
				surfaceTool.AddIndex(bottomRight);
			}
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildWeaponFinMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int lengthSubdivisions = 16;
		const int widthSubdivisions = 4;
		const float finWidth = 0.4f;
		const float finLength = 2.0f;
		const float halfWidth = finWidth * 0.5f;

		for (int y = 0; y <= lengthSubdivisions; y++)
		{
			float fractionY = (float)y / lengthSubdivisions;
			float posY = fractionY * finLength;

			for (int x = 0; x <= widthSubdivisions; x++)
			{
				float fractionX = (float)x / widthSubdivisions;
				float posX = -halfWidth + fractionX * finWidth;

				surfaceTool.SetNormal(Vector3.Back);
				surfaceTool.SetTangent(new Plane(1.0f, 0.0f, 0.0f, 1.0f));
				surfaceTool.SetUV(new Vector2(fractionX, fractionY));
				surfaceTool.AddVertex(new Vector3(posX, posY, 0.0f));
			}
		}

		int rowVertexCount = widthSubdivisions + 1;
		for (int y = 0; y < lengthSubdivisions; y++)
		{
			int currentRow = y * rowVertexCount;
			int nextRow = (y + 1) * rowVertexCount;

			for (int x = 0; x < widthSubdivisions; x++)
			{
				int bLeft = currentRow + x;
				int bRight = currentRow + x + 1;
				int tLeft = nextRow + x;
				int tRight = nextRow + x + 1;

				surfaceTool.AddIndex(bLeft);
				surfaceTool.AddIndex(tLeft);
				surfaceTool.AddIndex(bRight);

				surfaceTool.AddIndex(bRight);
				surfaceTool.AddIndex(tLeft);
				surfaceTool.AddIndex(tRight);
			}
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildCrossQuadMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int lengthSubdivisions = 16;
		const float finWidth = 0.4f;
		const float finLength = 2.0f;
		const float halfWidth = finWidth * 0.5f;

		int vertexCounter = 0;

		for (int y = 0; y < lengthSubdivisions; y++)
		{
			float y0 = ((float)y / lengthSubdivisions) * finLength;
			float y1 = ((float)(y + 1) / lengthSubdivisions) * finLength;
			float uvY0 = (float)y / lengthSubdivisions;
			float uvY1 = (float)(y + 1) / lengthSubdivisions;

			surfaceTool.SetNormal(Vector3.Back);
			surfaceTool.SetTangent(new Plane(1.0f, 0.0f, 0.0f, 1.0f));
			surfaceTool.SetUV(new Vector2(0.0f, uvY0)); surfaceTool.AddVertex(new Vector3(-halfWidth, y0, 0.0f));
			surfaceTool.SetUV(new Vector2(1.0f, uvY0)); surfaceTool.AddVertex(new Vector3(halfWidth, y0, 0.0f));
			surfaceTool.SetUV(new Vector2(0.0f, uvY1)); surfaceTool.AddVertex(new Vector3(-halfWidth, y1, 0.0f));
			surfaceTool.SetUV(new Vector2(1.0f, uvY1)); surfaceTool.AddVertex(new Vector3(halfWidth, y1, 0.0f));

			surfaceTool.AddIndex(vertexCounter + 0);
			surfaceTool.AddIndex(vertexCounter + 2);
			surfaceTool.AddIndex(vertexCounter + 1);
			surfaceTool.AddIndex(vertexCounter + 1);
			surfaceTool.AddIndex(vertexCounter + 2);
			surfaceTool.AddIndex(vertexCounter + 3);
			vertexCounter += 4;

			surfaceTool.SetNormal(Vector3.Right);
			surfaceTool.SetTangent(new Plane(0.0f, 0.0f, 1.0f, 1.0f));
			surfaceTool.SetUV(new Vector2(0.0f, uvY0)); surfaceTool.AddVertex(new Vector3(0.0f, y0, -halfWidth));
			surfaceTool.SetUV(new Vector2(1.0f, uvY0)); surfaceTool.AddVertex(new Vector3(0.0f, y0, halfWidth));
			surfaceTool.SetUV(new Vector2(0.0f, uvY1)); surfaceTool.AddVertex(new Vector3(0.0f, y1, -halfWidth));
			surfaceTool.SetUV(new Vector2(1.0f, uvY1)); surfaceTool.AddVertex(new Vector3(0.0f, y1, halfWidth));

			surfaceTool.AddIndex(vertexCounter + 0);
			surfaceTool.AddIndex(vertexCounter + 2);
			surfaceTool.AddIndex(vertexCounter + 1);
			surfaceTool.AddIndex(vertexCounter + 1);
			surfaceTool.AddIndex(vertexCounter + 2);
			surfaceTool.AddIndex(vertexCounter + 3);
			vertexCounter += 4;
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildSlashArcMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int arcSegments = 36;
		const float startAngleRadians = -MathF.PI * 0.45f;
		const float endAngleRadians = MathF.PI * 0.45f;
		const float innerRadius = 0.85f;
		const float outerRadius = 1.55f;

		for (int s = 0; s <= arcSegments; s++)
		{
			float fraction = (float)s / arcSegments;
			float angle = startAngleRadians + fraction * (endAngleRadians - startAngleRadians);

			float cosA = MathF.Cos(angle);
			float sinA = MathF.Sin(angle);

			Vector3 innerPos = new Vector3(cosA * innerRadius, 0.0f, sinA * innerRadius);
			Vector3 outerPos = new Vector3(cosA * outerRadius, 0.0f, sinA * outerRadius);

			surfaceTool.SetNormal(Vector3.Up);
			surfaceTool.SetTangent(new Plane(-sinA, 0.0f, cosA, 1.0f));
			surfaceTool.SetUV(new Vector2(fraction, 0.0f));
			surfaceTool.AddVertex(innerPos);

			surfaceTool.SetNormal(Vector3.Up);
			surfaceTool.SetTangent(new Plane(-sinA, 0.0f, cosA, 1.0f));
			surfaceTool.SetUV(new Vector2(fraction, 1.0f));
			surfaceTool.AddVertex(outerPos);
		}

		for (int s = 0; s < arcSegments; s++)
		{
			int i0 = s * 2;
			int i1 = s * 2 + 1;
			int i2 = (s + 1) * 2;
			int i3 = (s + 1) * 2 + 1;

			surfaceTool.AddIndex(i0);
			surfaceTool.AddIndex(i1);
			surfaceTool.AddIndex(i2);

			surfaceTool.AddIndex(i2);
			surfaceTool.AddIndex(i1);
			surfaceTool.AddIndex(i3);
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildLightShaftMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int angularSegments = 36;
		const int verticalSegments = 8;
		const float bottomRadius = 1.0f;
		const float topRadius = 0.35f;
		const float height = 3.0f;

		for (int v = 0; v <= verticalSegments; v++)
		{
			float heightFraction = (float)v / verticalSegments;
			float posY = heightFraction * height;
			float currentRadius = Mathf.Lerp(bottomRadius, topRadius, heightFraction);

			for (int a = 0; a <= angularSegments; a++)
			{
				float angleFraction = (float)a / angularSegments;
				float angleRadians = angleFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(angleRadians) * currentRadius;
				float posZ = MathF.Sin(angleRadians) * currentRadius;

				surfaceTool.SetNormal(new Vector3(MathF.Cos(angleRadians), 0.2f, MathF.Sin(angleRadians)).Normalized());
				surfaceTool.SetTangent(new Plane(-MathF.Sin(angleRadians), 0.0f, MathF.Cos(angleRadians), 1.0f));
				surfaceTool.SetUV(new Vector2(angleFraction, heightFraction));
				surfaceTool.AddVertex(new Vector3(posX, posY, posZ));
			}
		}

		int ringVertexCount = angularSegments + 1;
		for (int v = 0; v < verticalSegments; v++)
		{
			int currentRing = v * ringVertexCount;
			int nextRing = (v + 1) * ringVertexCount;

			for (int a = 0; a < angularSegments; a++)
			{
				int bLeft = currentRing + a;
				int bRight = currentRing + a + 1;
				int tLeft = nextRing + a;
				int tRight = nextRing + a + 1;

				surfaceTool.AddIndex(bLeft);
				surfaceTool.AddIndex(tLeft);
				surfaceTool.AddIndex(bRight);

				surfaceTool.AddIndex(bRight);
				surfaceTool.AddIndex(tLeft);
				surfaceTool.AddIndex(tRight);
			}
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildAuraCapsuleMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int longitudeSegments = 36;
		const int hemisphereRings = 10;
		const int cylinderSegments = 4;
		const float radius = 0.5f;
		const float cylinderHeight = 1.0f;
		const float totalHeight = cylinderHeight + radius * 2.0f;

		var vertices = new List<Vector3>();
		var uvs = new List<Vector2>();
		var normals = new List<Vector3>();

		for (int lat = hemisphereRings; lat >= 0; lat--)
		{
			float fraction = (float)lat / hemisphereRings;
			float phi = fraction * MathF.PI * 0.5f;
			float cosPhi = MathF.Cos(phi);
			float sinPhi = MathF.Sin(phi);
			float posY = radius - sinPhi * radius;

			for (int lon = 0; lon <= longitudeSegments; lon++)
			{
				float lonFraction = (float)lon / longitudeSegments;
				float theta = lonFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(theta) * cosPhi * radius;
				float posZ = MathF.Sin(theta) * cosPhi * radius;

				Vector3 norm = new Vector3(posX, posY - radius, posZ).Normalized();
				vertices.Add(new Vector3(posX, posY, posZ));
				normals.Add(norm);
				uvs.Add(new Vector2(lonFraction, posY / totalHeight));
			}
		}

		for (int c = 1; c <= cylinderSegments; c++)
		{
			float fraction = (float)c / cylinderSegments;
			float posY = radius + fraction * cylinderHeight;

			for (int lon = 0; lon <= longitudeSegments; lon++)
			{
				float lonFraction = (float)lon / longitudeSegments;
				float theta = lonFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(theta) * radius;
				float posZ = MathF.Sin(theta) * radius;

				vertices.Add(new Vector3(posX, posY, posZ));
				normals.Add(new Vector3(MathF.Cos(theta), 0.0f, MathF.Sin(theta)));
				uvs.Add(new Vector2(lonFraction, posY / totalHeight));
			}
		}

		for (int lat = 1; lat <= hemisphereRings; lat++)
		{
			float fraction = (float)lat / hemisphereRings;
			float phi = fraction * MathF.PI * 0.5f;
			float cosPhi = MathF.Cos(phi);
			float sinPhi = MathF.Sin(phi);
			float posY = radius + cylinderHeight + sinPhi * radius;

			for (int lon = 0; lon <= longitudeSegments; lon++)
			{
				float lonFraction = (float)lon / longitudeSegments;
				float theta = lonFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(theta) * cosPhi * radius;
				float posZ = MathF.Sin(theta) * cosPhi * radius;

				Vector3 norm = new Vector3(posX, posY - (radius + cylinderHeight), posZ).Normalized();
				vertices.Add(new Vector3(posX, posY, posZ));
				normals.Add(norm);
				uvs.Add(new Vector2(lonFraction, posY / totalHeight));
			}
		}

		for (int i = 0; i < vertices.Count; i++)
		{
			surfaceTool.SetNormal(normals[i]);
			surfaceTool.SetUV(uvs[i]);
			surfaceTool.AddVertex(vertices[i]);
		}

		int ringVertexCount = longitudeSegments + 1;
		int totalRings = (hemisphereRings + 1) + cylinderSegments + hemisphereRings;

		for (int r = 0; r < totalRings - 1; r++)
		{
			int currentRing = r * ringVertexCount;
			int nextRing = (r + 1) * ringVertexCount;

			for (int lon = 0; lon < longitudeSegments; lon++)
			{
				int i0 = currentRing + lon;
				int i1 = currentRing + lon + 1;
				int i2 = nextRing + lon;
				int i3 = nextRing + lon + 1;

				surfaceTool.AddIndex(i0);
				surfaceTool.AddIndex(i2);
				surfaceTool.AddIndex(i1);

				surfaceTool.AddIndex(i1);
				surfaceTool.AddIndex(i2);
				surfaceTool.AddIndex(i3);
			}
		}

		return surfaceTool.Commit();
	}

	private static ArrayMesh BuildAuraSphereMesh()
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		const int latitudeRings = 24;
		const int longitudeSegments = 48;
		const float radius = 0.8f;
		const float centerY = 1.0f;

		for (int lat = 0; lat <= latitudeRings; lat++)
		{
			float latFraction = (float)lat / latitudeRings;
			float phi = -MathF.PI * 0.5f + latFraction * MathF.PI;
			float cosPhi = MathF.Cos(phi);
			float sinPhi = MathF.Sin(phi);

			for (int lon = 0; lon <= longitudeSegments; lon++)
			{
				float lonFraction = (float)lon / longitudeSegments;
				float theta = lonFraction * MathF.PI * 2.0f;

				float posX = MathF.Cos(theta) * cosPhi * radius;
				float posY = centerY + sinPhi * radius;
				float posZ = MathF.Sin(theta) * cosPhi * radius;

				Vector3 norm = new Vector3(posX, posY - centerY, posZ).Normalized();
				surfaceTool.SetNormal(norm);
				surfaceTool.SetTangent(new Plane(-MathF.Sin(theta), 0.0f, MathF.Cos(theta), 1.0f));
				surfaceTool.SetUV(new Vector2(lonFraction, latFraction));
				surfaceTool.AddVertex(new Vector3(posX, posY, posZ));
			}
		}

		int ringVertexCount = longitudeSegments + 1;
		for (int lat = 0; lat < latitudeRings; lat++)
		{
			int currentRing = lat * ringVertexCount;
			int nextRing = (lat + 1) * ringVertexCount;

			for (int lon = 0; lon < longitudeSegments; lon++)
			{
				int i0 = currentRing + lon;
				int i1 = currentRing + lon + 1;
				int i2 = nextRing + lon;
				int i3 = nextRing + lon + 1;

				surfaceTool.AddIndex(i0);
				surfaceTool.AddIndex(i2);
				surfaceTool.AddIndex(i1);

				surfaceTool.AddIndex(i1);
				surfaceTool.AddIndex(i2);
				surfaceTool.AddIndex(i3);
			}
		}

		return surfaceTool.Commit();
	}
}
