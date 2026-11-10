using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>the manifest endpoint and its lookup by content.</summary>
public partial class DistributionServer
	{
	private async Task HandleManifestEndpointAsync(HttpListenerContext context, string method, string path)
	{
		if (method == "GET")
		{
			await HandleManifestGetAsync(context.Response, path);
			return;
		}

		if (method == "POST" && !path.Equals("/api/admin/remove_manifest", StringComparison.OrdinalIgnoreCase))
		{
			await HandleManifestPostAsync(context.Request, context.Response);
			return;
		}

		if (method == "DELETE" || (method == "POST" && path.Equals("/api/admin/remove_manifest", StringComparison.OrdinalIgnoreCase)))
		{
			await HandleManifestDeleteAsync(context.Request, context.Response, method, path);
			return;
		}

		context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
		context.Response.Close();
	}

	private async Task HandleManifestGetAsync(HttpListenerResponse response, string path)
	{
		string rawMapId = path.Length > "/api/manifests/".Length ? path.Substring("/api/manifests/".Length) : string.Empty;
		string mapId = Uri.UnescapeDataString(rawMapId);
		string manifestFolder = Path.Combine(_storage.RootDirectory, "manifests");

		if (!Directory.Exists(manifestFolder))
		{
			response.StatusCode = (int)HttpStatusCode.NotFound;
			response.Close();
			return;
		}

		var candidates = new List<string>
		{
			Path.Combine(manifestFolder, $"{mapId}_manifest.json"),
			Path.Combine(manifestFolder, $"{mapId}.json"),
			Path.Combine(manifestFolder, $"{mapId.Replace(' ', '_')}_manifest.json"),
			Path.Combine(manifestFolder, $"{mapId.Replace(' ', '_')}.json"),
			Path.Combine(manifestFolder, $"{mapId.Replace('_', ' ')}_manifest.json"),
			Path.Combine(manifestFolder, $"{mapId.Replace('_', ' ')}.json")
		};

		string? foundPath = candidates.FirstOrDefault(File.Exists);
		if (foundPath == null) foundPath = TryFindManifestFileByContent(manifestFolder, mapId);

		if (foundPath == null || !File.Exists(foundPath))
		{
			response.StatusCode = (int)HttpStatusCode.NotFound;
			response.Close();
			return;
		}

		byte[] manifestBytes = await File.ReadAllBytesAsync(foundPath);
		response.ContentType = "application/json";
		response.StatusCode = (int)HttpStatusCode.OK;
		await response.OutputStream.WriteAsync(manifestBytes, 0, manifestBytes.Length);
		response.Close();
	}

	private string? TryFindManifestFileByContent(string manifestFolder, string mapId)
	{
		foreach (var file in Directory.EnumerateFiles(manifestFolder, "*.json"))
		{
			try
			{
				var mf = MapManifest.LoadFromFile(file);
				if (mf != null && (string.Equals(mf.MapName, mapId, StringComparison.OrdinalIgnoreCase) ||
				                   string.Equals(mf.MapName?.Replace(' ', '_'), mapId.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)))
				{
					return file;
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[DistributionServer] skipping an unreadable map manifest while resolving a map by content: {ex.GetType().Name}: {ex.Message}");
			}
		}

		return null;
	}
}
