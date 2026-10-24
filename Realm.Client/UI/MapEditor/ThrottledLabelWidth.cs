using System;
using Godot;

namespace Realm.Client.UI.MapEditor;

public class ThrottledLabelWidth
{
	private const float WidthChangeTolerance = 0.5f;
	private const ulong ShrinkIntervalMsec = 1000;

	private float _pinnedWidth = 0f;
	private float _pendingShrinkWidth = float.NaN;
	private ulong _pendingShrinkSinceMsec = 0;
	private ulong _lastShrinkMsec = 0;
	private string _lastText = null;

	public void Apply(Label label, string text)
	{
		if (label == null || text == null) return;

		if (text != _lastText)
		{
			_lastText = text;
			label.Text = text;
		}

		float measuredWidth = MeasureTextWidth(label);
		if (measuredWidth > _pinnedWidth + WidthChangeTolerance)
		{
			_pendingShrinkWidth = float.NaN;
			SetPinnedWidth(label, measuredWidth);
			return;
		}

		if (measuredWidth >= _pinnedWidth - WidthChangeTolerance) return;

		ulong now = Time.GetTicksMsec();
		if (float.IsNaN(_pendingShrinkWidth) || Math.Abs(measuredWidth - _pendingShrinkWidth) > WidthChangeTolerance)
		{
			_pendingShrinkWidth = measuredWidth;
			_pendingShrinkSinceMsec = now;
			return;
		}

		if (now - _pendingShrinkSinceMsec < ShrinkIntervalMsec) return;
		if (now - _lastShrinkMsec < ShrinkIntervalMsec) return;

		_lastShrinkMsec = now;
		_pendingShrinkWidth = float.NaN;
		SetPinnedWidth(label, measuredWidth);
	}

	public void Reset(Label label)
	{
		_pinnedWidth = 0f;
		_pendingShrinkWidth = float.NaN;
		_pendingShrinkSinceMsec = 0;
		_lastShrinkMsec = 0;
		_lastText = null;
		if (label != null)
		{
			label.CustomMinimumSize = new Vector2(0f, label.CustomMinimumSize.Y);
		}
	}

	private static float MeasureTextWidth(Label label)
	{
		return Mathf.Ceil(label.GetMinimumSize().X);
	}

	private void SetPinnedWidth(Label label, float width)
	{
		_pinnedWidth = width;
		if (Math.Abs(label.CustomMinimumSize.X - width) <= WidthChangeTolerance) return;
		label.CustomMinimumSize = new Vector2(width, label.CustomMinimumSize.Y);
	}
}
