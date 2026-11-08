using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>the header and boundary helpers the multipart parser uses.</summary>
public partial class DistributionServer
	{
	private static string? ExtractHeaderParameter(string headers, string parameterName)
	{
		int index = headers.IndexOf(parameterName + "=", StringComparison.OrdinalIgnoreCase);
		if (index < 0)
		{
			return null;
		}

		int valueStart = index + parameterName.Length + 1;
		if (valueStart >= headers.Length)
		{
			return null;
		}

		if (headers[valueStart] == '"')
		{
			valueStart++;
			int end = headers.IndexOf('"', valueStart);
			return end > valueStart ? headers.Substring(valueStart, end - valueStart) : null;
		}
		else
		{
			int end = headers.IndexOfAny(new[] { ';', '\r', '\n', ' ' }, valueStart);
			return end > valueStart ? headers.Substring(valueStart, end - valueStart) : headers.Substring(valueStart).Trim();
		}
	}

	private static string? DecodeMetadataHeader(string? headerValue)
	{
		if (string.IsNullOrWhiteSpace(headerValue))
		{
			return null;
		}

		try
		{
			byte[] decoded = Convert.FromBase64String(headerValue);
			return Encoding.UTF8.GetString(decoded);
		}
		catch
		{
			return headerValue;
		}
	}

	private static List<byte[]> SplitBytesByBoundary(byte[] source, byte[] boundary)
	{
		var result = new List<byte[]>();
		int currentIndex = 0;

		while (currentIndex < source.Length)
		{
			int nextMatch = FindByteSequence(source, boundary, currentIndex);
			if (nextMatch < 0)
			{
				if (currentIndex < source.Length)
				{
					int tailLength = source.Length - currentIndex;
					byte[] tail = new byte[tailLength];
					Buffer.BlockCopy(source, currentIndex, tail, 0, tailLength);
					result.Add(tail);
				}

				break;
			}

			if (nextMatch > currentIndex)
			{
				int segmentLength = nextMatch - currentIndex;
				byte[] segment = new byte[segmentLength];
				Buffer.BlockCopy(source, currentIndex, segment, 0, segmentLength);
				result.Add(segment);
			}

			currentIndex = nextMatch + boundary.Length;
		}

		return result;
	}

	private static int FindByteSequence(byte[] source, byte[] pattern, int startIndex = 0)
	{
		if (source.Length == 0 || pattern.Length == 0 || pattern.Length > source.Length)
		{
			return -1;
		}

		for (int i = startIndex; i <= source.Length - pattern.Length; i++)
		{
			bool match = true;
			for (int j = 0; j < pattern.Length; j++)
			{
				if (source[i + j] != pattern[j])
				{
					match = false;
					break;
				}
			}

			if (match)
			{
				return i;
			}
		}

		return -1;
	}
}
