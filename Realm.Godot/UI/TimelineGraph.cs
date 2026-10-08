using Godot;
using System;
using System.Collections.Generic;

public partial class TimelineGraph : Control
{
	private int _activeTab = 0;
	private float _drawProgress = 0f;


	private List<Vector2> _p1Overview = new List<Vector2> { new Vector2(0, 3), new Vector2(5, 5), new Vector2(15, 12), new Vector2(25, 20), new Vector2(35, 28) };
	private List<Vector2> _p2Overview = new List<Vector2> { new Vector2(0, 1), new Vector2(5, 3), new Vector2(15, 10), new Vector2(25, 25), new Vector2(35, 29) };


	private List<Vector2> _p1Economy = new List<Vector2> { new Vector2(0, 0), new Vector2(5, 300), new Vector2(15, 1200), new Vector2(25, 2000), new Vector2(35, 3000) };
	private List<Vector2> _p2Economy = new List<Vector2> { new Vector2(0, 0), new Vector2(5, 400), new Vector2(15, 1000), new Vector2(25, 1800), new Vector2(35, 2500) };


	private List<Vector2> _p1Military = new List<Vector2> { new Vector2(0, 1), new Vector2(5, 10), new Vector2(15, 32), new Vector2(25, 22), new Vector2(35, 45) };
	private List<Vector2> _p2Military = new List<Vector2> { new Vector2(0, 1), new Vector2(5, 6), new Vector2(15, 20), new Vector2(25, 29), new Vector2(35, 18) };

	public override void _Ready()
	{
		AnimateDrawing();
	}

	public void SetTab(int tabIndex)
	{
		_activeTab = tabIndex;
		AnimateDrawing();
	}

	private void AnimateDrawing()
	{
		_drawProgress = 0f;
		var tween = CreateTween();
		tween.TweenProperty(this, nameof(_drawProgress), 1.0f, 0.6f)
			 .SetTrans(Tween.TransitionType.Quad)
			 .SetEase(Tween.EaseType.Out);
		tween.StepFinished += (step) => QueueRedraw();
		

		for (int i = 1; i <= 10; i++)
		{
			var timer = GetTree().CreateTimer(i * 0.06f);
			timer.Timeout += () => QueueRedraw();
		}
	}

	public override void _Draw()
	{

		Vector2 size = Size;
		float paddingLeft = 50f;
		float paddingRight = 30f;
		float paddingTop = 15f;
		float paddingBottom = 30f;

		Vector2 graphOrigin = new Vector2(paddingLeft, size.Y - paddingBottom);
		Vector2 graphSize = new Vector2(size.X - paddingLeft - paddingRight, size.Y - paddingTop - paddingBottom);

		Color gridColor = new Color(0.2f, 0.2f, 0.25f, 0.3f);
		int gridLinesY = 4;
		for (int i = 0; i <= gridLinesY; i++)
		{
			float yRatio = (float)i / gridLinesY;
			float yPos = graphOrigin.Y - (yRatio * graphSize.Y);

			DrawLine(new Vector2(graphOrigin.X, yPos), new Vector2(graphOrigin.X + graphSize.X, yPos), gridColor, 1.0f);

			string labelVal = GetYLabel(yRatio);
			DrawString(ThemeDB.FallbackFont, new Vector2(10, yPos + 5), labelVal, HorizontalAlignment.Left, -1, 14, new Color(0.8f, 0.8f, 0.8f));
		}

		int gridLinesX = 5;
		for (int i = 0; i <= gridLinesX; i++)
		{
			float xRatio = (float)i / gridLinesX;
			float xPos = graphOrigin.X + (xRatio * graphSize.X);

			DrawLine(new Vector2(xPos, graphOrigin.Y), new Vector2(xPos, graphOrigin.Y - graphSize.Y), gridColor, 1.0f);

			string xLabel = $"{(int)(xRatio * 35)}";
			DrawString(ThemeDB.FallbackFont, new Vector2(xPos - 10, graphOrigin.Y + 20), xLabel, HorizontalAlignment.Left, -1, 14, new Color(0.8f, 0.8f, 0.8f));
		}


		Color axisColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
		DrawLine(graphOrigin, new Vector2(graphOrigin.X + graphSize.X, graphOrigin.Y), axisColor, 3.0f); // X axis
		DrawLine(graphOrigin, new Vector2(graphOrigin.X, graphOrigin.Y - graphSize.Y), axisColor, 3.0f); // Y axis


		List<Vector2> p1Points = GetActiveData(true);
		List<Vector2> p2Points = GetActiveData(false);

		float maxY = GetMaxY();

		DrawPlotLine(p1Points, graphOrigin, graphSize, maxY, new Color(0.2f, 0.8f, 0.3f), new Color(0.1f, 0.6f, 0.2f, 0.3f)); // P1: Green
		DrawPlotLine(p2Points, graphOrigin, graphSize, maxY, new Color(0.9f, 0.9f, 0.9f), new Color(0.6f, 0.6f, 0.6f, 0.2f)); // P2: White
	}

	private void DrawPlotLine(List<Vector2> data, Vector2 origin, Vector2 size, float maxY, Color lineColor, Color fillGradient)
	{
		if (data.Count < 2) return;

		Vector2[] screenPoints = new Vector2[data.Count];
		for (int i = 0; i < data.Count; i++)
		{
			float xRatio = data[i].X / 35f;
			float yRatio = data[i].Y / maxY;

			float px = origin.X + (xRatio * size.X);
			float py = origin.Y - (yRatio * size.Y);

			screenPoints[i] = new Vector2(px, py);
		}


		int numPointsToDraw = (int)Math.Max(2, Math.Ceiling(screenPoints.Length * _drawProgress));
		float subProgress = (screenPoints.Length * _drawProgress) % 1.0f;
		if (numPointsToDraw > screenPoints.Length)
		{
			numPointsToDraw = screenPoints.Length;
			subProgress = 1.0f;
		}

		Vector2[] activePoints = new Vector2[numPointsToDraw];
		for (int i = 0; i < numPointsToDraw - 1; i++)
		{
			activePoints[i] = screenPoints[i];
		}


		if (numPointsToDraw < screenPoints.Length)
		{
			Vector2 lastDrawPoint = screenPoints[numPointsToDraw - 2];
			Vector2 nextTargetPoint = screenPoints[numPointsToDraw - 1];
			activePoints[numPointsToDraw - 1] = lastDrawPoint.Lerp(nextTargetPoint, subProgress);
		}
		else
		{
			activePoints[numPointsToDraw - 1] = screenPoints[screenPoints.Length - 1];
		}


		for (int i = 0; i < activePoints.Length - 1; i++)
		{
			DrawLine(activePoints[i], activePoints[i + 1], lineColor, 3.0f, true);

			DrawCircle(activePoints[i], 4f, lineColor);
		}
		DrawCircle(activePoints[activePoints.Length - 1], 4f, lineColor);
	}

	private List<Vector2> GetActiveData(bool isP1)
	{
		if (_activeTab == 0) return isP1 ? _p1Overview : _p2Overview;
		if (_activeTab == 1) return isP1 ? _p1Economy : _p2Economy;
		return isP1 ? _p1Military : _p2Military;
	}

	private float GetMaxY()
	{
		if (_activeTab == 0) return 30f;    // Army value 0..30
		if (_activeTab == 1) return 3000f;  // Gold 0..3000
		return 50f;                         // Units 0..50
	}

	private string GetYLabel(float ratio)
	{
		float val = ratio * GetMaxY();
		if (_activeTab == 1) return $"{(int)val}";
		return $"{val:F0}";
	}
}
