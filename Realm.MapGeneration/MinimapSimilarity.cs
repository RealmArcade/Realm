using System;

namespace Realm.MapGeneration;

public readonly record struct MinimapSimilarityResult(float StructuralSimilarity, float ColourError)
{
	public float Percentage => StructuralSimilarity * 100f;
}

public static class MinimapSimilarity
{
	public static MinimapSimilarityResult Compare(MinimapImage left, MinimapImage right)
	{
		int width = Math.Min(left.Width, right.Width);
		int height = Math.Min(left.Height, right.Height);
		int count = width * height;
		if (count == 0)
		{
			return new MinimapSimilarityResult(0f, 1f);
		}

		double sumLeft = 0;
		double sumRight = 0;
		double colourError = 0;
		for (int y = 0; y < height; y++)
		{
			float v = (y + 0.5f) / height;
			for (int x = 0; x < width; x++)
			{
				float u = (x + 0.5f) / width;
				left.SampleBilinear(u, v, out float leftRed, out float leftGreen, out float leftBlue);
				right.SampleBilinear(u, v, out float rightRed, out float rightGreen, out float rightBlue);
				double leftLuminance = leftRed * 0.2126 + leftGreen * 0.7152 + leftBlue * 0.0722;
				double rightLuminance = rightRed * 0.2126 + rightGreen * 0.7152 + rightBlue * 0.0722;
				sumLeft += leftLuminance;
				sumRight += rightLuminance;
				colourError += (Math.Abs(leftRed - rightRed) + Math.Abs(leftGreen - rightGreen) + Math.Abs(leftBlue - rightBlue)) / 3.0;
			}
		}

		double meanLeft = sumLeft / count;
		double meanRight = sumRight / count;
		double varianceLeft = 0;
		double varianceRight = 0;
		double covariance = 0;
		for (int y = 0; y < height; y++)
		{
			float v = (y + 0.5f) / height;
			for (int x = 0; x < width; x++)
			{
				float u = (x + 0.5f) / width;
				left.SampleBilinear(u, v, out float leftRed, out float leftGreen, out float leftBlue);
				right.SampleBilinear(u, v, out float rightRed, out float rightGreen, out float rightBlue);
				double leftLuminance = leftRed * 0.2126 + leftGreen * 0.7152 + leftBlue * 0.0722;
				double rightLuminance = rightRed * 0.2126 + rightGreen * 0.7152 + rightBlue * 0.0722;
				varianceLeft += (leftLuminance - meanLeft) * (leftLuminance - meanLeft);
				varianceRight += (rightLuminance - meanRight) * (rightLuminance - meanRight);
				covariance += (leftLuminance - meanLeft) * (rightLuminance - meanRight);
			}
		}

		varianceLeft /= count;
		varianceRight /= count;
		covariance /= count;
		const double c1 = 0.01 * 0.01;
		const double c2 = 0.03 * 0.03;
		double numerator = (2 * meanLeft * meanRight + c1) * (2 * covariance + c2);
		double denominator = (meanLeft * meanLeft + meanRight * meanRight + c1) * (varianceLeft + varianceRight + c2);
		float ssim = denominator <= 0 ? 0f : (float)(numerator / denominator);
		return new MinimapSimilarityResult(ssim, (float)(colourError / count));
	}
}
