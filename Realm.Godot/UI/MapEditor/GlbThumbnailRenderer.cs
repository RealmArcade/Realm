using Godot;
using Realm.Godot.Animation;
using System;
using System.Collections.Generic;
using System.IO;

public partial class GlbThumbnailRenderer : Node
{
	private static GlbThumbnailRenderer? _instance;
	private static readonly object _instanceLock = new();

	public static event Action<string, Texture2D>? ThumbnailGenerated;

	private class GlbRequest
	{
		public string FilePath { get; set; } = string.Empty;
		public DateTime LastModifiedUtc { get; set; }
		public string Blake3 { get; set; } = string.Empty;
		public Action<string, Texture2D>? Callback { get; set; }
		public bool IsHighPriority { get; set; }
	}

	private static readonly LinkedList<GlbRequest> _requestQueue = new();
	private static readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
	private static readonly object _requestLock = new();

	private SubViewport _subViewport;
	private Node3D _modelContainer;
	private Camera3D _camera;
	private DirectionalLight3D _keyLight;
	private DirectionalLight3D _fillLight;

	private GlbRequest? _currentRequest;
	private int _framesRemainingForCapture;

	public static void EnsureInTreeDeferred()
	{
		if (_instance != null && GodotObject.IsInstanceValid(_instance) && _instance.IsInsideTree())
		{
			return;
		}

		if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
		{
			if (System.Environment.CurrentManagedThreadId == 1)
			{
				CreateInstanceInTree(tree);
			}
			else
			{
				Callable.From(() =>
				{
					if (Engine.GetMainLoop() is SceneTree mainTree)
					{
						CreateInstanceInTree(mainTree);
					}
				}).CallDeferred();
			}
		}
		else
		{
			Callable.From(() =>
			{
				if (Engine.GetMainLoop() is SceneTree mainTree)
				{
					CreateInstanceInTree(mainTree);
				}
			}).CallDeferred();
		}
	}

	private static void CreateInstanceInTree(SceneTree tree)
	{
		if (tree.Root == null) return;
		lock (_instanceLock)
		{
			if (_instance == null || !GodotObject.IsInstanceValid(_instance))
			{
				_instance = new GlbThumbnailRenderer();
			}

			if (!_instance.IsInsideTree())
			{
				tree.Root.AddChild(_instance);
			}
		}
	}

	public GlbThumbnailRenderer()
	{
		Name = "GlbThumbnailRenderer";
		SetupViewport();
	}

	public override void _Ready()
	{
		SetupViewport();
	}

	private void SetupViewport()
	{
		if (_subViewport != null) return;

		_subViewport = new SubViewport();
		_subViewport.Size = new Vector2I(128, 128);
		_subViewport.TransparentBg = false;
		_subViewport.OwnWorld3D = true;
		_subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

		var world = new World3D();
		var env = new Godot.Environment();
		env.BackgroundMode = Godot.Environment.BGMode.Color;
		env.BackgroundColor = new Color(0.10f, 0.11f, 0.14f, 1.0f);
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor = new Color(0.40f, 0.42f, 0.48f);
		env.AmbientLightEnergy = 1.2f;
		world.Environment = env;
		_subViewport.World3D = world;

		_modelContainer = new Node3D();
		_modelContainer.Name = "ModelContainer";
		_subViewport.AddChild(_modelContainer);

		_keyLight = new DirectionalLight3D();
		_keyLight.LightColor = new Color(1.0f, 0.96f, 0.90f);
		_keyLight.LightEnergy = 1.5f;
		_keyLight.RotationDegrees = new Vector3(-35, 45, 0);
		_subViewport.AddChild(_keyLight);

		_fillLight = new DirectionalLight3D();
		_fillLight.LightColor = new Color(0.55f, 0.70f, 0.95f);
		_fillLight.LightEnergy = 0.9f;
		_fillLight.RotationDegrees = new Vector3(25, -135, 0);
		_subViewport.AddChild(_fillLight);

		_camera = new Camera3D();
		_camera.Current = true;
		_camera.Fov = 35.0f;
		_camera.Near = 0.01f;
		_camera.Far = 500.0f;
		_subViewport.AddChild(_camera);

		AddChild(_subViewport);
	}

	public static string NormalizePath(string path)
	{
		if (string.IsNullOrEmpty(path)) return string.Empty;
		return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
	}

	public static string GetBlake3(string filePath, string? preferredBlake3 = null)
	{
		if (!string.IsNullOrEmpty(preferredBlake3))
		{
			return Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(preferredBlake3);
		}

		string fileName = Path.GetFileNameWithoutExtension(filePath);
		if (fileName.Length == 64 && IsHexOnly(fileName))
		{
			return fileName.ToLowerInvariant();
		}

		try
		{
			string? metaJson = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(filePath);
			if (!string.IsNullOrEmpty(metaJson))
			{
				var node = System.Text.Json.Nodes.JsonNode.Parse(metaJson);
				string? b3 = node?["blake3"]?.ToString();
				if (!string.IsNullOrEmpty(b3))
				{
					return Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(b3);
				}
			}
		}
		catch { }

		try
		{
			if (File.Exists(filePath))
			{
				var fi = new FileInfo(filePath);
				string key = $"{filePath}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
				using var sha = System.Security.Cryptography.SHA256.Create();
				byte[] hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
				return Convert.ToHexString(hashBytes).ToLowerInvariant();
			}
		}
		catch { }

		return Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(fileName);
	}

	private static bool IsHexOnly(string str)
	{
		foreach (char c in str)
		{
			if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
			{
				return false;
			}
		}
		return true;
	}

	public static bool TryGetDiskCached(string glbPath, DateTime lastModifiedUtc, out Texture2D? texture)
	{
		return TryGetDiskCached(glbPath, null, out texture);
	}

	public static bool TryGetDiskCached(string glbPath, string? blake3, out Texture2D? texture)
	{
		texture = null;
		string normPath = NormalizePath(glbPath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return false;

		string hash = GetBlake3(normPath, blake3);
		if (string.IsNullOrEmpty(hash) || hash.Length < 2) return false;

		string cacheDirectory = AssetThumbnailProvider.ModelCacheDir;
		string cachedPngPath = Path.Combine(cacheDirectory, hash.Substring(0, 2), $"{hash}.png");

		if (File.Exists(cachedPngPath))
		{
			try
			{
				var img = Image.LoadFromFile(cachedPngPath);
				if (img != null && !img.IsEmpty())
				{
					texture = ImageTexture.CreateFromImage(img);
					return true;
				}
			}
			catch { }
		}

		return false;
	}

	public static bool HasDiskCache(string glbPath, string? blake3 = null)
	{
		string normPath = NormalizePath(glbPath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return false;

		string hash = GetBlake3(normPath, blake3);
		if (string.IsNullOrEmpty(hash) || hash.Length < 2) return false;

		string cacheDirectory = AssetThumbnailProvider.ModelCacheDir;
		string cachedPngPath = Path.Combine(cacheDirectory, hash.Substring(0, 2), $"{hash}.png");

		return File.Exists(cachedPngPath);
	}

	public static void EnqueueRequest(string glbPath, DateTime lastModifiedUtc, string? blake3 = null, Action<string, Texture2D>? callback = null, bool isHighPriority = true)
	{
		string normPath = NormalizePath(glbPath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return;

		lock (_requestLock)
		{
			string hash = GetBlake3(normPath, blake3);

			if (_pendingPaths.Contains(normPath))
			{
				if (isHighPriority)
				{
					var node = _requestQueue.First;
					while (node != null)
					{
						if (string.Equals(node.Value.FilePath, normPath, StringComparison.OrdinalIgnoreCase))
						{
							_requestQueue.Remove(node);
							_requestQueue.AddFirst(node.Value);
							break;
						}
						node = node.Next;
					}
				}
				EnsureInTreeDeferred();
				return;
			}

			_pendingPaths.Add(normPath);

			var req = new GlbRequest
			{
				FilePath = normPath,
				LastModifiedUtc = lastModifiedUtc,
				Blake3 = hash,
				Callback = callback,
				IsHighPriority = isHighPriority
			};

			if (isHighPriority)
			{
				_requestQueue.AddFirst(req);
			}
			else
			{
				_requestQueue.AddLast(req);
			}
		}

		EnsureInTreeDeferred();
	}

	public override void _Process(double delta)
	{
		if (_currentRequest != null)
		{
			_framesRemainingForCapture--;
			if (_framesRemainingForCapture <= 0)
			{
				CaptureCurrentThumbnail();
			}
			return;
		}

		lock (_requestLock)
		{
			if (_requestQueue.Count > 0)
			{
				_currentRequest = _requestQueue.First?.Value;
				_requestQueue.RemoveFirst();
				if (_currentRequest != null)
				{
					LoadGlbForCapture(_currentRequest);
				}
			}
		}
	}

	private void LoadGlbForCapture(GlbRequest request)
	{
		SetupViewport();

		foreach (var child in _modelContainer.GetChildren())
		{
			_modelContainer.RemoveChild(child);
			child.QueueFree();
		}

		if (!File.Exists(request.FilePath))
		{
			lock (_requestLock)
			{
				_pendingPaths.Remove(request.FilePath);
			}
			_currentRequest = null;
			return;
		}

		try
		{
			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.RegisterExtension();

			var doc = new GltfDocument();
			var state = new GltfState();
			Error err;
			byte[] fileBytes = File.ReadAllBytes(request.FilePath);
			string? chromaKey = null;
			byte[] glbBytes;
			if (Realm.Shared.ModelOptimization.RmeshFile.IsRmeshBytes(fileBytes))
			{
				var (meta, glbPayload, _) = Realm.Shared.ModelOptimization.RmeshFile.Parse(fileBytes);
				chromaKey = Realm.Shared.Metadata.RealmMetadataHelper.ExtractChromaKeyFromMetadataJson(meta);
				glbBytes = glbPayload;
			}
			else
			{
				chromaKey = Realm.Shared.Metadata.RealmMetadataHelper.ExtractChromaKey(request.FilePath);
				glbBytes = fileBytes;
			}

			bool despill = GameHost.Instance != null && GameHost.Instance.GetModelDespillPlayerColor(request.FilePath);
			if (despill)
			{
				glbBytes = Realm.Shared.GlbInMemoryColorPreprocessor.PreprocessGlbInMemory(glbBytes, chromaKey);
			}

			err = doc.AppendFromBuffer(glbBytes, "", state);
			if (err != Error.Ok)
			{
				lock (_requestLock)
				{
					_pendingPaths.Remove(request.FilePath);
				}
				_currentRequest = null;
				return;
			}

			var scene = doc.GenerateScene(state);
			if (scene == null)
			{
				lock (_requestLock)
				{
					_pendingPaths.Remove(request.FilePath);
				}
				_currentRequest = null;
				return;
			}

			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.ProcessImportedScene(state, scene);
			StopAnimations(scene);

			_modelContainer.AddChild(scene);

			AnimationRetargetingService.TryApplyRiggedIdlePose(scene);

			Aabb aabb = CalculateVisualAabb(scene);
			if (aabb.Size.LengthSquared() < 0.0001f)
			{
				aabb = new Aabb(new Vector3(-0.5f, 0, -0.5f), new Vector3(1f, 1f, 1f));
			}

			Vector3 center = aabb.Position + aabb.Size * 0.5f;
			float maxDim = MathF.Max(aabb.Size.X, MathF.Max(aabb.Size.Y, aabb.Size.Z));
			if (maxDim < 0.01f) maxDim = 1.0f;

			float fovRad = Mathf.DegToRad(_camera.Fov * 0.5f);
			float dist = (maxDim * 0.85f) / MathF.Tan(fovRad);
			Vector3 camDir = new Vector3(1.2f, 0.8f, 1.4f).Normalized();
			Vector3 eyePos = center + camDir * dist;
			if (eyePos.DistanceSquaredTo(center) > 0.0001f)
			{
				Vector3 dir = (center - eyePos).Normalized();
				Vector3 up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
				_camera.LookAtFromPosition(eyePos, center, up);
			}
			_camera.Current = true;

			_framesRemainingForCapture = 2;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GlbThumbnailRenderer] Error preparing {request.FilePath}: {ex.Message}");
			lock (_requestLock)
			{
				_pendingPaths.Remove(request.FilePath);
			}
			_currentRequest = null;
		}
	}

	private void CaptureCurrentThumbnail()
	{
		if (_currentRequest == null) return;

		try
		{
			var tex = _subViewport.GetTexture();
			if (tex != null)
			{
				var img = tex.GetImage();
				if (img != null && !img.IsEmpty())
				{
					if (img.GetFormat() != Image.Format.Rgba8)
					{
						img.Convert(Image.Format.Rgba8);
					}

					string cacheDirectory = AssetThumbnailProvider.ModelCacheDir;
					string hash = !string.IsNullOrEmpty(_currentRequest.Blake3)
						? _currentRequest.Blake3
						: GetBlake3(_currentRequest.FilePath, null);

					if (!string.IsNullOrEmpty(hash) && hash.Length >= 2)
					{
						string cachedPngPath = Path.Combine(cacheDirectory, hash.Substring(0, 2), $"{hash}.png");
						AssetThumbnailProvider.SaveThumbnailAtomic(img, cachedPngPath);
					}

					var imgTex = ImageTexture.CreateFromImage(img);
					_currentRequest.Callback?.Invoke(_currentRequest.FilePath, imgTex);
					ThumbnailGenerated?.Invoke(_currentRequest.FilePath, imgTex);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GlbThumbnailRenderer] Capture failed: {ex.Message}");
		}
		finally
		{
			foreach (var child in _modelContainer.GetChildren())
			{
				_modelContainer.RemoveChild(child);
				child.QueueFree();
			}
			lock (_requestLock)
			{
				if (_currentRequest != null)
				{
					_pendingPaths.Remove(_currentRequest.FilePath);
				}
			}
			_currentRequest = null;
		}
	}

	private static void StopAnimations(Node node)
	{
		if (node is AnimationPlayer ap)
		{
			ap.Stop();
			ap.ProcessMode = ProcessModeEnum.Disabled;
		}
		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			StopAnimations(node.GetChild(i));
		}
	}

	private static Aabb CalculateVisualAabb(Node node)
	{
		Aabb totalAabb = new Aabb();
		bool first = true;

		void Traverse(Node current, Transform3D currentTransform)
		{
			if (current is VisualInstance3D visual)
			{
				Aabb localAabb = visual.GetAabb();
				if (localAabb.Size.LengthSquared() > 0.00001f)
				{
					Aabb transformedAabb = currentTransform * localAabb;
					if (first)
					{
						totalAabb = transformedAabb;
						first = false;
					}
					else
					{
						totalAabb = totalAabb.Merge(transformedAabb);
					}
				}
			}

			int childCount = current.GetChildCount();
			for (int i = 0; i < childCount; i++)
			{
				var child = current.GetChild(i);
				if (child is Node3D child3D)
				{
					Traverse(child, currentTransform * child3D.Transform);
				}
				else
				{
					Traverse(child, currentTransform);
				}
			}
		}

		if (node is Node3D root3D)
		{
			Traverse(root3D, root3D.Transform);
		}
		else
		{
			Traverse(node, Transform3D.Identity);
		}

		return totalAabb;
	}

	private static string SanitizeFileName(string name)
	{
		var invalidChars = Path.GetInvalidFileNameChars();
		var chars = name.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (Array.IndexOf(invalidChars, chars[i]) >= 0)
			{
				chars[i] = '_';
			}
		}
		return new string(chars);
	}

	private static void TryApplyRiggedIdlePose(Node scene)
	{
		AnimationRetargetingService.TryApplyRiggedIdlePose(scene);
	}

	private static RealmAnimationData? GetIdleAnimationData()
	{
		return AnimationRetargetingService.GetIdleAnimationData();
	}
}
