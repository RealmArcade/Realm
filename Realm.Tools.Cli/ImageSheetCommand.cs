using System.Globalization;
using System.Text.Json;
using SkiaSharp;

namespace Realm.Tools.Cli;

public static class ImageSheetCommand
{
	private const string Ellipsis = "\u2026";

	public static int Execute(ImageSheetOptions options)
	{
		if (!TryParseBackground(options.Background, out SKColor background))
		{
			Console.Error.WriteLine($"Error: --background must be a #RRGGBB colour, got '{options.Background}'.");
			return 1;
		}

		if (!TryParseLabelMode(options.Label, out ImageSheetLabelMode labelMode))
		{
			Console.Error.WriteLine($"Error: Unknown label mode '{options.Label}'. Use 'name', 'index', 'path' or 'none'.");
			return 1;
		}

		if (options.Cell < 16)
		{
			Console.Error.WriteLine("Error: --cell must be at least 16 pixels.");
			return 1;
		}

		if (options.Columns < 1)
		{
			Console.Error.WriteLine("Error: --columns must be at least 1.");
			return 1;
		}

		string[] imagePaths = ExpandInputs(options.Input, options.Recursive);
		if (imagePaths.Length == 0)
		{
			Console.Error.WriteLine("Error: No image files found in the given inputs.");
			return 1;
		}

		int columns = Math.Min(options.Columns, imagePaths.Length);
		int rows = (int)Math.Ceiling(imagePaths.Length / (double)columns);
		int padding = Math.Max(0, options.Padding);
		int labelHeight = labelMode == ImageSheetLabelMode.None
			? 0
			: Math.Max(16, (int)Math.Round(options.Cell * 0.075));
		int cellWidth = options.Cell;
		int cellHeight = options.Cell + labelHeight;
		int titleHeight = string.IsNullOrWhiteSpace(options.Title)
			? 0
			: Math.Max(28, (int)Math.Round(options.Cell * 0.10)) + padding;

		int sheetWidth = columns * cellWidth + (columns + 1) * padding;
		int sheetHeight = titleHeight + rows * cellHeight + (rows + 1) * padding;

		var cells = new List<ImageSheetCell>();

		try
		{
			using var sheet = new SKBitmap(sheetWidth, sheetHeight);
			using var canvas = new SKCanvas(sheet);

			canvas.Clear(background);

			using var typeface = ResolveTypeface();
			using var titleFont = new SKFont(typeface, Math.Max(14, cellWidth * 0.09f));
			using var labelFont = new SKFont(typeface, Math.Max(11, cellWidth * 0.07f));
			using var textPaint = new SKPaint { Color = new SKColor(206, 214, 230, 255), IsAntialias = true };
			var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

			if (titleHeight > 0)
			{
				float titleBaseline = padding + titleFont.Size;
				canvas.DrawText(options.Title!, padding, titleBaseline, SKTextAlign.Left, titleFont, textPaint);
			}

			for (int index = 0; index < imagePaths.Length; index++)
			{
				int column = index % columns;
				int row = index / columns;

				float cellLeft = padding + column * (cellWidth + padding);
				float cellTop = titleHeight + padding + row * (cellHeight + padding);

				var imageArea = new SKRect(cellLeft, cellTop, cellLeft + cellWidth, cellTop + cellHeight - labelHeight);

				using var decoded = SKBitmap.Decode(imagePaths[index]);
				if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0)
				{
					Console.Error.WriteLine($"Warning: could not decode image '{imagePaths[index]}', leaving its cell empty.");
				}
				else
				{
					using var image = SKImage.FromBitmap(decoded);
					canvas.DrawImage(image, FitInside(imageArea, decoded.Width, decoded.Height), sampling);
				}

				string label = BuildLabel(labelMode, imagePaths[index], index);
				if (labelHeight > 0 && label.Length > 0)
				{
					string shortenedLabel = ShortenToFit(label, labelFont, cellWidth, textPaint);
					float baseline = cellTop + cellHeight - labelHeight + labelHeight * 0.72f;
					float textWidth = labelFont.MeasureText(shortenedLabel, textPaint);
					canvas.DrawText(shortenedLabel, cellLeft + (cellWidth - textWidth) / 2f, baseline, SKTextAlign.Left, labelFont, textPaint);
				}

				cells.Add(new ImageSheetCell
				{
					Index = index,
					Row = row,
					Column = column,
					Label = label,
					Path = Path.GetFullPath(imagePaths[index])
				});
			}

			CliFileSet.EnsureTargetDirectoryExists(options.Output);
			using var snapshot = SKImage.FromBitmap(sheet);
			using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100);
			using (var outputStream = File.Create(options.Output))
			{
				encoded.SaveTo(outputStream);
			}
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"Error: could not write the image sheet: {exception.Message}");
			return 1;
		}

		string indexPath = string.IsNullOrWhiteSpace(options.IndexPath)
			? Path.ChangeExtension(options.Output, ".json")
			: options.IndexPath;

		WriteIndex(indexPath, options, cells, columns, rows, options.Cell, string.IsNullOrWhiteSpace(options.Title) ? null : options.Title!);

		Console.WriteLine($"Successfully composed {cells.Count} image(s) into {columns}x{rows} sheet: {options.Output}");
		Console.WriteLine($"Cell index: {indexPath}");
		return 0;
	}

	private static void WriteIndex(
		string indexPath,
		ImageSheetOptions options,
		List<ImageSheetCell> cells,
		int columns,
		int rows,
		int cellSize,
		string? title)
	{
		var index = new ImageSheetIndex
		{
			Sheet = Path.GetFullPath(options.Output),
			Title = title,
			Cell = cellSize,
			Columns = columns,
			Rows = rows,
			Label = options.Label.Trim().ToLowerInvariant(),
			Cells = cells
		};

		try
		{
			CliFileSet.EnsureTargetDirectoryExists(indexPath);
			File.WriteAllText(indexPath, JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (IOException exception)
		{
			Console.Error.WriteLine($"Warning: could not write the cell index {indexPath}: {exception.Message}");
		}
	}

	private static string[] ExpandInputs(IList<string> inputs, bool recursive)
	{
		var resolvedPaths = new List<string>();
		var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (string input in inputs)
		{
			if (string.IsNullOrWhiteSpace(input)) continue;

			if (File.Exists(input))
			{
				if (IsImageFile(input) && seenPaths.Add(Path.GetFullPath(input)))
				{
					resolvedPaths.Add(input);
				}

				continue;
			}

			if (!Directory.Exists(input))
			{
				Console.Error.WriteLine($"Warning: input not found: {input}");
				continue;
			}

			SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			var directoryFiles = Directory.EnumerateFiles(input, "*", searchOption)
				.Where(IsImageFile)
				.OrderBy(path => path, StringComparer.Ordinal)
				.ToList();

			foreach (string file in directoryFiles)
			{
				if (seenPaths.Add(Path.GetFullPath(file)))
				{
					resolvedPaths.Add(file);
				}
			}
		}

		return resolvedPaths.ToArray();
	}

	private static bool IsImageFile(string path)
	{
		string extension = Path.GetExtension(path).ToLowerInvariant();
		return extension is ".png" or ".webp" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
	}

	private static SKRect FitInside(SKRect area, int width, int height)
	{
		float scale = Math.Min(area.Width / width, area.Height / height);
		float drawnWidth = width * scale;
		float drawnHeight = height * scale;
		float left = area.Left + (area.Width - drawnWidth) / 2f;
		float top = area.Top + (area.Height - drawnHeight) / 2f;
		return new SKRect(left, top, left + drawnWidth, top + drawnHeight);
	}

	private static string ShortenToFit(string label, SKFont font, float maximumWidth, SKPaint paint)
	{
		if (font.MeasureText(label, paint) <= maximumWidth)
		{
			return label;
		}

		for (int length = label.Length - 1; length > 0; length--)
		{
			string candidate = label[..length] + Ellipsis;
			if (font.MeasureText(candidate, paint) <= maximumWidth)
			{
				return candidate;
			}
		}

		return Ellipsis;
	}

	private static string BuildLabel(ImageSheetLabelMode labelMode, string path, int index)
	{
		return labelMode switch
		{
			ImageSheetLabelMode.Name => Path.GetFileNameWithoutExtension(path),
			ImageSheetLabelMode.Index => index.ToString(CultureInfo.InvariantCulture),
			ImageSheetLabelMode.Path => path.Replace('\\', '/'),
			_ => string.Empty
		};
	}

	private static SKTypeface ResolveTypeface()
	{
		return SKTypeface.FromFamilyName("Segoe UI")
		       ?? SKTypeface.FromFamilyName("DejaVu Sans")
		       ?? SKTypeface.Default;
	}

	private static bool TryParseBackground(string value, out SKColor color)
	{
		color = SKColors.Black;
		string text = value.Trim().TrimStart('#');

		if (text.Length == 3)
		{
			text = string.Concat(text.Select(character => $"{character}{character}"));
		}

		if (text.Length != 6) return false;

		if (!byte.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red)) return false;
		if (!byte.TryParse(text[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green)) return false;
		if (!byte.TryParse(text[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) return false;

		color = new SKColor(red, green, blue, 255);
		return true;
	}

	private static bool TryParseLabelMode(string value, out ImageSheetLabelMode labelMode)
	{
		switch (value.Trim().ToLowerInvariant())
		{
			case "name":
				labelMode = ImageSheetLabelMode.Name;
				return true;
			case "index":
				labelMode = ImageSheetLabelMode.Index;
				return true;
			case "path":
				labelMode = ImageSheetLabelMode.Path;
				return true;
			case "none":
				labelMode = ImageSheetLabelMode.None;
				return true;
			default:
				labelMode = ImageSheetLabelMode.Name;
				return false;
		}
	}

	private enum ImageSheetLabelMode
	{
		Name,
		Index,
		Path,
		None
	}

	private class ImageSheetIndex
	{
		public string Sheet { get; set; } = string.Empty;

		public string? Title { get; set; }

		public int Cell { get; set; }

		public int Columns { get; set; }

		public int Rows { get; set; }

		public string Label { get; set; } = string.Empty;

		public List<ImageSheetCell> Cells { get; set; } = new();
	}

	private class ImageSheetCell
	{
		public int Index { get; set; }

		public int Row { get; set; }

		public int Column { get; set; }

		public string Label { get; set; } = string.Empty;

		public string Path { get; set; } = string.Empty;
	}
}
