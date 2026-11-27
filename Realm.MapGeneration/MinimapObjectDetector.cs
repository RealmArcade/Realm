using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public static class MinimapObjectDetector
{
	private const float DeviationThreshold = 0.055f;
	private const int MinimumComponentPixels = 14;

	public static List<GeneratedProp> Detect(
		MinimapImage image,
		MinimapCellFeatures[] cells,
		bool[] waterMask,
		int width,
		int depth,
		float quadSize,
		PropCatalog catalog,
		TerrainReconstructionOptions options)
	{
		var props = new List<GeneratedProp>();
		if (catalog.IsEmpty || cells.Length != width * depth)
		{
			return props;
		}

		int imageWidth = image.Width;
		int imageHeight = image.Height;
		int pixelCount = imageWidth * imageHeight;
		int smoothingRadius = Math.Max(1, Math.Min(imageWidth, imageHeight) / 240);
		int backgroundRadius = Math.Max(6, Math.Min(imageWidth, imageHeight) / 46);

		float[] smoothRed = BoxBlur(image.Red, imageWidth, imageHeight, smoothingRadius);
		float[] smoothGreen = BoxBlur(image.Green, imageWidth, imageHeight, smoothingRadius);
		float[] smoothBlue = BoxBlur(image.Blue, imageWidth, imageHeight, smoothingRadius);
		float[] backgroundRed = BoxBlur(smoothRed, imageWidth, imageHeight, backgroundRadius);
		float[] backgroundGreen = BoxBlur(smoothGreen, imageWidth, imageHeight, backgroundRadius);
		float[] backgroundBlue = BoxBlur(smoothBlue, imageWidth, imageHeight, backgroundRadius);
		int coarseRadius = Math.Max(backgroundRadius * 2, imageWidth / 14);
		float[] coarseRed = BoxBlur(smoothRed, imageWidth, imageHeight, coarseRadius);
		float[] coarseGreen = BoxBlur(smoothGreen, imageWidth, imageHeight, coarseRadius);
		float[] coarseBlue = BoxBlur(smoothBlue, imageWidth, imageHeight, coarseRadius);

		var mask = new bool[pixelCount];
		var treeMask = new bool[pixelCount];
		for (int index = 0; index < pixelCount; index++)
		{
			float red = smoothRed[index];
			float green = smoothGreen[index];
			float blue = smoothBlue[index];
			CreateHsv(red, green, blue, out float hue, out float saturation, out float value);
			CreateHsv(backgroundRed[index], backgroundGreen[index], backgroundBlue[index], out float backgroundHue, out float backgroundSaturation, out float backgroundValue);
			CreateHsv(coarseRed[index], coarseGreen[index], coarseBlue[index], out float coarseHue, out float coarseSaturation, out float coarseValue);
			float hueDelta = MathF.Abs(hue - backgroundHue);
			if (hueDelta > 180f)
			{
				hueDelta = 360f - hueDelta;
			}

			if (hue >= 65f && hue <= 170f && saturation >= 0.27f && value < coarseValue - 0.09f)
			{
				treeMask[index] = true;
				continue;
			}

			float channelDelta = MathF.Max(MathF.Abs(red - backgroundRed[index]), MathF.Max(MathF.Abs(green - backgroundGreen[index]), MathF.Abs(blue - backgroundBlue[index])));
			bool chromatic = hueDelta > 20f && saturation > 0.20f && (saturation > backgroundSaturation + 0.10f || value < backgroundValue - 0.06f);
			bool desaturated = backgroundSaturation > 0.22f && saturation < 0.20f;
			bool valued = MathF.Abs(value - backgroundValue) > 0.10f;
			mask[index] = channelDelta > DeviationThreshold && (chromatic || desaturated || valued);
		}

		mask = Morph(mask, imageWidth, imageHeight, erode: true);
		mask = Morph(mask, imageWidth, imageHeight, erode: false);

		float metresPerPixelX = width * quadSize / imageWidth;
		float metresPerPixelY = depth * quadSize / imageHeight;
		float squareMetresPerPixel = metresPerPixelX * metresPerPixelY;

		List<Component> components = LabelComponents(mask, image, imageWidth, imageHeight, MinimumComponentPixels);
		for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
		{
			Component component = components[componentIndex];
			if (IsElongated(component) || IsRough(image, component, imageWidth))
			{
				continue;
			}

			PropCategory category = ClassifyComponent(component);
			float worldX = (component.CentreX / imageWidth - 0.5f) * width * quadSize;
			float worldZ = (component.CentreY / imageHeight - 0.5f) * depth * quadSize;
			if (IsWaterWorldPoint(waterMask, width, depth, quadSize, worldX, worldZ))
			{
				continue;
			}

			float areaSquareMetres = component.PixelCount * squareMetresPerPixel;
			if (areaSquareMetres > 26f)
			{
				continue;
			}

			float radiusMetres = MathF.Sqrt(areaSquareMetres / MathF.PI);
			float scale = Math.Clamp(radiusMetres * 0.7f, 0.7f, 2.6f);
			int centreCellX = Math.Clamp((int)(component.CentreX / imageWidth * width), 0, width - 1);
			int centreCellZ = Math.Clamp((int)(component.CentreY / imageHeight * depth), 0, depth - 1);
			float rotation = DeterministicHash.Jitter(centreCellX, centreCellZ, options.Seed, 9) * 360f;
			PropCatalogEntry entry = catalog.FindForCategory(category, (int)DeterministicHash.Combine(centreCellX, centreCellZ, options.Seed));
			if (string.IsNullOrWhiteSpace(entry.TemplateId))
			{
				continue;
			}

			props.Add(new GeneratedProp(entry.TemplateId, category, worldX, 0f, worldZ, rotation, scale));
		}

		PlaceTrees(treeMask, smoothGreen, coarseGreen, props, catalog, options, width, depth, quadSize, imageWidth, imageHeight);
		return props;
	}

	private static void PlaceTrees(
		bool[] treeMask,
		float[] smoothedGreen,
		float[] coarseGreen,
		List<GeneratedProp> props,
		PropCatalog catalog,
		TerrainReconstructionOptions options,
		int width,
		int depth,
		float quadSize,
		int imageWidth,
		int imageHeight)
	{
		int treeSpacingPixels = Math.Max(3, (int)MathF.Round(3.2f * imageWidth / (width * quadSize)));
		var suppressed = new bool[treeMask.Length];
		var candidates = new List<int>();
		var strengths = new List<float>();
		for (int index = 0; index < treeMask.Length; index++)
		{
			if (!treeMask[index])
			{
				continue;
			}

			candidates.Add(index);
			strengths.Add(coarseGreen[index] - smoothedGreen[index]);
		}

		int[] order = new int[candidates.Count];
		for (int index = 0; index < order.Length; index++)
		{
			order[index] = index;
		}

		Array.Sort(order, (left, right) => strengths[right].CompareTo(strengths[left]));
		int treeCount = 0;
		for (int orderIndex = 0; orderIndex < order.Length; orderIndex++)
		{
			int index = candidates[order[orderIndex]];
			if (suppressed[index])
			{
				continue;
			}

			int x = index % imageWidth;
			int y = index / imageWidth;
			{
				int cellX = Math.Clamp(x * width / imageWidth, 0, width - 1);
				int cellZ = Math.Clamp(y * depth / imageHeight, 0, depth - 1);
				float jitterX = DeterministicHash.Jitter(cellX, cellZ, options.Seed, 11);
				float jitterZ = DeterministicHash.Jitter(cellX, cellZ, options.Seed, 12);
				float scale = 0.8f + DeterministicHash.Jitter(cellX, cellZ, options.Seed, 13) * 0.55f;
				float rotation = DeterministicHash.Jitter(cellX, cellZ, options.Seed, 14) * 360f;
				PropCatalogEntry entry = catalog.FindForCategory(PropCategory.Tree, (int)DeterministicHash.Combine(cellX, cellZ, options.Seed));
				if (string.IsNullOrWhiteSpace(entry.TemplateId))
				{
					continue;
				}

				float worldX = ((x / (float)imageWidth) - 0.5f) * width * quadSize + jitterX * quadSize;
				float worldZ = ((y / (float)imageHeight) - 0.5f) * depth * quadSize + jitterZ * quadSize;
				props.Add(new GeneratedProp(entry.TemplateId, PropCategory.Tree, worldX, 0f, worldZ, rotation, scale));
				treeCount++;
				for (int offsetY = -treeSpacingPixels; offsetY <= treeSpacingPixels; offsetY++)
				{
					int sampleY = y + offsetY;
					if (sampleY < 0 || sampleY >= imageHeight) continue;
					for (int offsetX = -treeSpacingPixels; offsetX <= treeSpacingPixels; offsetX++)
					{
						if (offsetX * offsetX + offsetY * offsetY > treeSpacingPixels * treeSpacingPixels) continue;
						int sampleX = x + offsetX;
						if (sampleX < 0 || sampleX >= imageWidth) continue;
						suppressed[sampleY * imageWidth + sampleX] = true;
					}
				}

				if (treeCount > 6000)
				{
					return;
				}
			}
		}
	}

	private static int EstimateTreeSpacing(bool[] treeMask, int imageWidth, int imageHeight)
	{
		var areas = new List<int>();
		var labels = new int[treeMask.Length];
		var queue = new Queue<int>();
		int label = 0;
		for (int start = 0; start < treeMask.Length; start++)
		{
			if (!treeMask[start] || labels[start] != 0)
			{
				continue;
			}

			label++;
			labels[start] = label;
			queue.Enqueue(start);
			int count = 0;
			while (queue.Count > 0)
			{
				int pixel = queue.Dequeue();
				int pixelX = pixel % imageWidth;
				int pixelY = pixel / imageWidth;
				count++;
				VisitTree(pixelX - 1, pixelY);
				VisitTree(pixelX + 1, pixelY);
				VisitTree(pixelX, pixelY - 1);
				VisitTree(pixelX, pixelY + 1);
			}

			if (count >= 6)
			{
				areas.Add(count);
			}
		}

		if (areas.Count == 0)
		{
			return 4;
		}

		areas.Sort();
		int footprint = areas[areas.Count / 4];
		int spacing = (int)MathF.Round(MathF.Sqrt(footprint / MathF.PI) * 1.5f);
		return Math.Clamp(spacing, 2, 24);

		void VisitTree(int x, int y)
		{
			if (x < 0 || x >= imageWidth || y < 0 || y >= imageHeight) return;
			int index = y * imageWidth + x;
			if (!treeMask[index] || labels[index] != 0) return;
			labels[index] = label;
			queue.Enqueue(index);
		}
	}

	private static PropCategory ClassifyComponent(Component component)
	{
		CreateHsv(component.MeanRed, component.MeanGreen, component.MeanBlue, out float hue, out float saturation, out float value);
		if (hue >= 68f && hue <= 168f && saturation >= 0.22f)
		{
			return value < 0.50f ? PropCategory.Tree : PropCategory.Nature;
		}

		if (saturation < 0.24f)
		{
			return PropCategory.Rock;
		}

		if (hue >= 290f || hue <= 62f)
		{
			return PropCategory.Structure;
		}

		return PropCategory.Other;
	}

	private static bool IsRough(MinimapImage image, Component component, int imageWidth)
	{
		if (component.PixelCount < 8 || component.PixelCount > 400)
		{
			return false;
		}

		float total = 0f;
		int samples = 0;
		for (int y = component.MinimumY + 1; y < component.MaximumY; y++)
		{
			for (int x = component.MinimumX + 1; x < component.MaximumX; x++)
			{
				if (!component.Contains(x, y, imageWidth))
				{
					continue;
				}

				int centre = y * imageWidth + x;
				float luminance = Luminance(image, centre);
				float neighbourLuminance = (Luminance(image, centre - 1) + Luminance(image, centre + 1) + Luminance(image, centre - imageWidth) + Luminance(image, centre + imageWidth)) * 0.25f;
				total += MathF.Abs(luminance - neighbourLuminance);
				samples++;
			}
		}

		return samples > 0 && total / samples > 0.055f;
	}

	private static float Luminance(MinimapImage image, int index)
	{
		return image.Red[index] * 0.2126f + image.Green[index] * 0.7152f + image.Blue[index] * 0.0722f;
	}

	private static bool IsElongated(Component component)
	{
		int boundingWidth = component.MaximumX - component.MinimumX + 1;
		int boundingHeight = component.MaximumY - component.MinimumY + 1;
		int boundingArea = boundingWidth * boundingHeight;
		if (boundingArea <= 4)
		{
			return false;
		}

		float fill = component.PixelCount / (float)boundingArea;
		if (fill < 0.42f)
		{
			return true;
		}

		float aspect = MathF.Max(boundingWidth, boundingHeight) / (float)MathF.Min(boundingWidth, boundingHeight);
		return aspect > 2.6f;
	}

	private static void CreateHsv(float red, float green, float blue, out float hue, out float saturation, out float value)
	{
		float maximum = MathF.Max(red, MathF.Max(green, blue));
		float minimum = MathF.Min(red, MathF.Min(green, blue));
		float delta = maximum - minimum;
		saturation = maximum <= 0.0001f ? 0f : delta / maximum;
		value = maximum;
		if (delta <= 0.0001f)
		{
			hue = 0f;
			return;
		}

		if (maximum == red)
		{
			hue = 60f * (((green - blue) / delta) % 6f);
		}
		else if (maximum == green)
		{
			hue = 60f * ((blue - red) / delta + 2f);
		}
		else
		{
			hue = 60f * ((red - green) / delta + 4f);
		}

		if (hue < 0f)
		{
			hue += 360f;
		}
	}

	private static bool IsWaterWorldPoint(bool[] waterMask, int width, int depth, float quadSize, float worldX, float worldZ)
	{
		if (waterMask.Length != width * depth)
		{
			return false;
		}

		int cellX = (int)(worldX / quadSize + width * 0.5f);
		int cellZ = (int)(worldZ / quadSize + depth * 0.5f);
		if (cellX < 0 || cellX >= width || cellZ < 0 || cellZ >= depth)
		{
			return true;
		}

		return waterMask[cellZ * width + cellX];
	}

	private static float[] BoxBlur(float[] values, int width, int height, int radius)
	{
		var horizontal = new float[values.Length];
		var result = new float[values.Length];
		for (int y = 0; y < height; y++)
		{
			int rowStart = y * width;
			float sum = 0f;
			int count = 0;
			int windowStart = 0;
			for (int x = 0; x < width; x++)
			{
				int addIndex = Math.Min(width - 1, x + radius);
				while (windowStart <= addIndex)
				{
					sum += values[rowStart + windowStart];
					windowStart++;
					count++;
				}

				int removeIndex = x - radius - 1;
				if (removeIndex >= 0)
				{
					sum -= values[rowStart + removeIndex];
					count--;
				}

				horizontal[rowStart + x] = sum / Math.Max(1, count);
			}
		}

		for (int x = 0; x < width; x++)
		{
			float sum = 0f;
			int count = 0;
			int windowStart = 0;
			for (int y = 0; y < height; y++)
			{
				int addIndex = Math.Min(height - 1, y + radius);
				while (windowStart <= addIndex)
				{
					sum += horizontal[windowStart * width + x];
					windowStart++;
					count++;
				}

				int removeIndex = y - radius - 1;
				if (removeIndex >= 0)
				{
					sum -= horizontal[removeIndex * width + x];
					count--;
				}

				result[y * width + x] = sum / Math.Max(1, count);
			}
		}

		return result;
	}

	private static bool[] Morph(bool[] mask, int width, int height, bool erode)
	{
		var result = new bool[mask.Length];
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				bool value = erode;
				for (int offsetY = -1; offsetY <= 1; offsetY++)
				{
					int sampleY = y + offsetY;
					if (sampleY < 0 || sampleY >= height) continue;
					for (int offsetX = -1; offsetX <= 1; offsetX++)
					{
						int sampleX = x + offsetX;
						if (sampleX < 0 || sampleX >= width) continue;
						bool neighbour = mask[sampleY * width + sampleX];
						if (erode && !neighbour)
						{
							value = false;
						}
						else if (!erode && neighbour)
						{
							value = true;
						}
					}

					if (erode && !value)
					{
						break;
					}
				}

				result[y * width + x] = value;
			}
		}

		return result;
	}

	private static List<Component> LabelComponents(bool[] mask, MinimapImage image, int width, int height, int minimumPixels)
	{
		var labels = new int[mask.Length];
		var components = new List<Component>();
		var queue = new Queue<int>();
		int label = 0;

		for (int start = 0; start < mask.Length; start++)
		{
			if (!mask[start] || labels[start] != 0)
			{
				continue;
			}

			label++;
			queue.Enqueue(start);
			labels[start] = label;
			var component = new Component();
			while (queue.Count > 0)
			{
				int pixel = queue.Dequeue();
				int pixelX = pixel % width;
				int pixelY = pixel / width;
				component.Add(pixelX, pixelY, image, width);
				Visit(pixelX - 1, pixelY);
				Visit(pixelX + 1, pixelY);
				Visit(pixelX, pixelY - 1);
				Visit(pixelX, pixelY + 1);
			}

			if (component.PixelCount < minimumPixels)
			{
				continue;
			}

			components.Add(component);
		}

		return components;

		void Visit(int x, int y)
		{
			if (x < 0 || x >= width || y < 0 || y >= height) return;
			int index = y * width + x;
			if (!mask[index] || labels[index] != 0) return;
			labels[index] = label;
			queue.Enqueue(index);
		}
	}

	private class Component
	{
		public int PixelCount { get; private set; }

		public float CentreX { get; private set; }

		public float CentreY { get; private set; }

		public int MinimumX { get; private set; } = int.MaxValue;

		public int MaximumX { get; private set; } = int.MinValue;

		public int MinimumY { get; private set; } = int.MaxValue;

		public int MaximumY { get; private set; } = int.MinValue;

		public bool Contains(int x, int y, int imageWidth)
		{
			return _pixels.Contains(y * 65536 + x);
		}

		public float MeanRed => PixelCount > 0 ? _sumRed / PixelCount : 0f;

		public float MeanGreen => PixelCount > 0 ? _sumGreen / PixelCount : 0f;

		public float MeanBlue => PixelCount > 0 ? _sumBlue / PixelCount : 0f;

		private readonly List<int> _pixels = new();

		private float _sumX;
		private float _sumY;
		private float _sumRed;
		private float _sumGreen;
		private float _sumBlue;

		public void Add(int x, int y, MinimapImage image, int imageWidth)
		{
			PixelCount++;
			_sumX += x;
			_sumY += y;
			int pixelIndex = y * imageWidth + x;
			_sumRed += image.Red[pixelIndex];
			_sumGreen += image.Green[pixelIndex];
			_sumBlue += image.Blue[pixelIndex];
			MinimumX = Math.Min(MinimumX, x);
			MaximumX = Math.Max(MaximumX, x);
			MinimumY = Math.Min(MinimumY, y);
			MaximumY = Math.Max(MaximumY, y);
			_pixels.Add(y * 65536 + x);
			CentreX = _sumX / PixelCount;
			CentreY = _sumY / PixelCount;
		}
	}
}
