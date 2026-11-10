using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>parsing a multipart upload.</summary>
public partial class DistributionServer
	{
	private async Task<(byte[]? FileBytes, string? FileName, string? MetadataJson, string? AuthorPublicKey, string? AuthorSignature, string? Hash)> ParseMultipartFormAsync(HttpListenerRequest request)
	{
		using var memoryStream = new MemoryStream();
		await request.InputStream.CopyToAsync(memoryStream);
		byte[] body = memoryStream.ToArray();

		string contentType = request.ContentType ?? string.Empty;
		int boundaryIndex = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
		if (boundaryIndex < 0) return (body, null, null, null, null, null);

		string rawBoundary = contentType.Substring(boundaryIndex + 9).Split(';')[0].Trim().Trim('"');
		byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + rawBoundary);

		byte[]? fileBytes = null;
		string? fileName = null;
		string? metadataJson = null;
		string? authorPublicKey = null;
		string? authorSignature = null;
		string? hash = null;

		foreach (var section in SplitBytesByBoundary(body, boundaryBytes))
		{
			var parsed = ParseSection(section);
			if (!parsed.HasValue) continue;

			ProcessMultipartSection(parsed.Value.Headers, parsed.Value.ContentBytes,
				ref fileBytes, ref fileName, ref metadataJson, ref authorPublicKey, ref authorSignature, ref hash);
		}

		return (fileBytes ?? body, fileName, metadataJson, authorPublicKey, authorSignature, hash);
	}

	private void ProcessMultipartSection(string headers, byte[] contentBytes,
		ref byte[]? fileBytes, ref string? fileName, ref string? metadataJson,
		ref string? authorPublicKey, ref string? authorSignature, ref string? hash)
	{
		string? partName = ExtractHeaderParameter(headers, "name");
		string? partFileName = ExtractHeaderParameter(headers, "filename");

		if (IsFileSection(partName, partFileName))
		{
			ProcessFileSection(partFileName, contentBytes, ref fileName, ref fileBytes);
			return;
		}

		ProcessTextSection(partName, contentBytes, ref metadataJson, ref authorPublicKey, ref authorSignature, ref hash);
	}

	private static bool IsFileSection(string? partName, string? partFileName)
	{
		return string.Equals(partName, "file", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(partFileName);
	}

	private static void ProcessFileSection(string? partFileName, byte[] contentBytes, ref string? fileName, ref byte[]? fileBytes)
	{
		if (!string.IsNullOrEmpty(partFileName))
		{
			fileName = partFileName;
		}

		fileBytes = contentBytes;
	}

	private static void ProcessTextSection(string? partName, byte[] contentBytes,
		ref string? metadataJson, ref string? authorPublicKey, ref string? authorSignature, ref string? hash)
	{
		if (string.Equals(partName, "metadata", StringComparison.OrdinalIgnoreCase))
		{
			metadataJson = Encoding.UTF8.GetString(contentBytes);
			return;
		}

		if (string.Equals(partName, "authorPublicKey", StringComparison.OrdinalIgnoreCase) || string.Equals(partName, "PublicKey", StringComparison.OrdinalIgnoreCase))
		{
			authorPublicKey = Encoding.UTF8.GetString(contentBytes);
			return;
		}

		if (string.Equals(partName, "authorSignature", StringComparison.OrdinalIgnoreCase) || string.Equals(partName, "Signature", StringComparison.OrdinalIgnoreCase))
		{
			authorSignature = Encoding.UTF8.GetString(contentBytes);
			return;
		}

		if (string.Equals(partName, "hash", StringComparison.OrdinalIgnoreCase))
		{
			hash = Encoding.UTF8.GetString(contentBytes);
			return;
		}
	}

	private static (string Headers, byte[] ContentBytes)? ParseSection(byte[] section)
	{
		int headerEnd = FindByteSequence(section, new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' });
		int delimLength = 4;

		if (headerEnd < 0)
		{
			headerEnd = FindByteSequence(section, new byte[] { (byte)'\n', (byte)'\n' });
			delimLength = 2;
		}

		if (headerEnd < 0) return null;

		string headers = Encoding.UTF8.GetString(section, 0, headerEnd);
		int contentStart = headerEnd + delimLength;
		int contentLength = section.Length - contentStart;

		if (contentLength < 0) return null;

		if (contentLength >= 2 && section[section.Length - 2] == '\r' && section[section.Length - 1] == '\n')
		{
			contentLength -= 2;
		}
		else if (contentLength >= 1 && section[section.Length - 1] == '\n')
		{
			contentLength -= 1;
		}

		byte[] contentBytes = new byte[contentLength];
		Buffer.BlockCopy(section, contentStart, contentBytes, 0, contentLength);

		return (headers, contentBytes);
	}
}
