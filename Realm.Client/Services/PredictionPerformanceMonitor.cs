using System;

namespace Realm.Client.Services;

public class PredictionPerformanceMonitor
{
	public const int SampleWindowSize = 120;
	public const int WarmupSampleCount = 60;
	public const int EvaluationIntervalSamples = 40;
	public const int RequiredConsecutiveOverBudgetEvaluations = 2;
	public const float DefaultPredictionCostThresholdMs = 2.0f;
	public const float DefaultFrameTimeThresholdMs = 28.0f;

	private readonly float[] _predictionCostSamples = new float[SampleWindowSize];
	private readonly float[] _frameTimeSamples = new float[SampleWindowSize];

	private int _sampleCount;
	private int _nextSampleIndex;
	private float _predictionCostSum;
	private float _frameTimeSum;
	private int _samplesUntilEvaluation = EvaluationIntervalSamples;
	private int _consecutiveOverBudgetEvaluations;

	public float PredictionCostThresholdMs { get; set; } = DefaultPredictionCostThresholdMs;
	public float FrameTimeThresholdMs { get; set; } = DefaultFrameTimeThresholdMs;
	public bool IsMachineTooSlow { get; private set; }
	public bool HasEnoughSamples => _sampleCount >= WarmupSampleCount;
	public float AveragePredictionCostMs { get; private set; }
	public float AverageFrameTimeMs { get; private set; }

	public void Sample(float predictionCostMs, float frameTimeMs)
	{
		if (!float.IsFinite(frameTimeMs) || frameTimeMs < 0f) return;

		if (_sampleCount < SampleWindowSize)
		{
			_sampleCount++;
		}

		AppendSample(_predictionCostSamples, float.IsFinite(predictionCostMs) && predictionCostMs >= 0f ? predictionCostMs : 0f, ref _predictionCostSum);
		AppendSample(_frameTimeSamples, frameTimeMs, ref _frameTimeSum);

		if (_sampleCount < WarmupSampleCount) return;

		_samplesUntilEvaluation--;
		if (_samplesUntilEvaluation > 0) return;

		Evaluate();
	}

	private void AppendSample(float[] samples, float value, ref float runningSum)
	{
		int index = _nextSampleIndex;
		runningSum -= samples[index];
		samples[index] = value;
		runningSum += value;

		_nextSampleIndex = (index + 1) % SampleWindowSize;
	}

	private void Evaluate()
	{
		_samplesUntilEvaluation = EvaluationIntervalSamples;

		float sampleCount = Math.Max(1, _sampleCount);
		AveragePredictionCostMs = _predictionCostSum / sampleCount;
		AverageFrameTimeMs = _frameTimeSum / sampleCount;

		bool overBudget = AveragePredictionCostMs >= PredictionCostThresholdMs || AverageFrameTimeMs >= FrameTimeThresholdMs;
		if (overBudget)
		{
			_consecutiveOverBudgetEvaluations++;
			if (_consecutiveOverBudgetEvaluations >= RequiredConsecutiveOverBudgetEvaluations)
			{
				IsMachineTooSlow = true;
			}
		}
		else
		{
			_consecutiveOverBudgetEvaluations = 0;
			IsMachineTooSlow = false;
		}
	}

	public void Reset()
	{
		Array.Clear(_predictionCostSamples);
		Array.Clear(_frameTimeSamples);
		_sampleCount = 0;
		_nextSampleIndex = 0;
		_predictionCostSum = 0f;
		_frameTimeSum = 0f;
		_samplesUntilEvaluation = EvaluationIntervalSamples;
		_consecutiveOverBudgetEvaluations = 0;
		IsMachineTooSlow = false;
		AveragePredictionCostMs = 0f;
		AverageFrameTimeMs = 0f;
	}
}
