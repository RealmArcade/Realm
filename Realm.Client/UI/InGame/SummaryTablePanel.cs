using System.Collections.Generic;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.InGame;

public class SummaryTablePanel
{
	private PanelContainer _summaryPanel;
	private Label _titleLabel;
	private VBoxContainer _rowsContainer;

	private HBoxContainer? _headerRow;
	private readonly List<Label> _headerLabels = new();
	private HSeparator? _separator;
	private readonly List<HBoxContainer> _dataRows = new();
	private readonly List<List<Label>> _dataRowLabels = new();
	private string? _lastTitle;

	public SummaryTablePanel(PanelContainer summaryPanel, Label titleLabel, VBoxContainer rowsContainer)
	{
		_summaryPanel = summaryPanel;
		_titleLabel = titleLabel;
		_rowsContainer = rowsContainer;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		bool allowSummary = GameContext.InGameHud?.IsHudElementVisible(HudElement.SummaryTable, true) ?? true;
		bool visible = viewModel.SummaryTableVisible && allowSummary;
		if (_summaryPanel.Visible != visible)
		{
			_summaryPanel.Visible = visible;
		}
		if (!visible)
		{
			return;
		}

		string title = viewModel.SummaryTableTitle ?? string.Empty;
		if (_lastTitle != title)
		{
			_titleLabel.Text = title;
			_lastTitle = title;
		}

		string[] headers = GetHeaders(viewModel);
		UpdateHeaderRow(headers);
		EnsureSeparator();

		int expectedMetrics = headers.Length > 0 ? headers.Length - 1 : 0;
		UpdateDataRows(viewModel, expectedMetrics);
	}

	private string[] GetHeaders(InGameHUDViewModel viewModel)
	{
		string[] headers = viewModel.SummaryTableHeaders;
		if (headers == null || headers.Length == 0)
		{
			headers = new[]
			{
				TranslationServer.Translate("PLAYER").ToString(),
				TranslationServer.Translate("DAMAGE").ToString(),
				TranslationServer.Translate("INCOME").ToString(),
				TranslationServer.Translate("SCORE").ToString()
			};
		}

		return headers;
	}

	private void UpdateHeaderRow(string[] headers)
	{
		if (_headerRow == null || !GodotObject.IsInstanceValid(_headerRow))
		{
			_headerRow = new HBoxContainer();
			_headerRow.Name = "HeaderRow_HBoxContainer";
			_headerRow.AddThemeConstantOverride("separation", 15);
			_rowsContainer.AddChild(_headerRow);
			_headerLabels.Clear();
		}

		while (_headerLabels.Count < headers.Length)
		{
			int colIdx = _headerLabels.Count;
			var colLabel = new Label();
			colLabel.Name = "ColLabel_Label";
			colLabel.AddThemeFontSizeOverride("font_size", 12);
			colLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			if (colIdx == 0)
			{
				colLabel.CustomMinimumSize = new Vector2(100, 0);
			}
			else
			{
				colLabel.CustomMinimumSize = new Vector2(80, 0);
				colLabel.HorizontalAlignment = HorizontalAlignment.Right;
			}
			_headerRow.AddChild(colLabel);
			_headerLabels.Add(colLabel);
		}

		for (int i = 0; i < _headerLabels.Count; i++)
		{
			var lbl = _headerLabels[i];
			bool colVisible = i < headers.Length;
			if (lbl.Visible != colVisible)
			{
				lbl.Visible = colVisible;
			}
			if (colVisible)
			{
				string text = headers[i] ?? string.Empty;
				if (lbl.Text != text)
				{
					lbl.Text = text;
				}
			}
		}
	}

	private void EnsureSeparator()
	{
		if (_separator == null || !GodotObject.IsInstanceValid(_separator))
		{
			_separator = new HSeparator();
			_separator.AddThemeColorOverride("separator_color", UIStyle.ColorBronze);
			_rowsContainer.AddChild(_separator);
		}
	}

	private void UpdateDataRows(InGameHUDViewModel viewModel, int expectedMetrics)
	{
		int totalCols = 1 + expectedMetrics;
		int rowIdx = 0;

		foreach (var kvp in viewModel.SummaryTableRows)
		{
			if (rowIdx >= _dataRows.Count)
			{
				var newRow = new HBoxContainer();
				newRow.Name = "NewRow_HBoxContainer";
				newRow.AddThemeConstantOverride("separation", 15);
				_rowsContainer.AddChild(newRow);
				_dataRows.Add(newRow);
				_dataRowLabels.Add(new List<Label>());
			}

			var row = _dataRows[rowIdx];
			var labels = _dataRowLabels[rowIdx];
			if (!row.Visible)
			{
				row.Visible = true;
			}

			while (labels.Count < totalCols)
			{
				int colIdx = labels.Count;
				var cellLbl = new Label();
				cellLbl.Name = "CellLbl_Label";
				cellLbl.AddThemeFontSizeOverride("font_size", 12);
				if (colIdx == 0)
				{
					cellLbl.AddThemeColorOverride("font_color", Colors.White);
					cellLbl.CustomMinimumSize = new Vector2(100, 0);
				}
				else
				{
					cellLbl.CustomMinimumSize = new Vector2(80, 0);
					cellLbl.HorizontalAlignment = HorizontalAlignment.Right;
				}
				row.AddChild(cellLbl);
				labels.Add(cellLbl);
			}

			for (int c = 0; c < labels.Count; c++)
			{
				var cellLbl = labels[c];
				bool colVisible = c < totalCols;
				if (cellLbl.Visible != colVisible)
				{
					cellLbl.Visible = colVisible;
				}
				if (!colVisible) continue;

				if (c == 0)
				{
					string playerText = kvp.Key ?? string.Empty;
					if (cellLbl.Text != playerText)
					{
						cellLbl.Text = playerText;
					}
				}
				else
				{
					int metricIdx = c - 1;
					string[] values = kvp.Value;
					string cellText = (values != null && metricIdx < values.Length) ? (values[metricIdx] ?? string.Empty) : string.Empty;
					if (cellLbl.Text != cellText)
					{
						cellLbl.Text = cellText;
					}
					Color desiredColor = (metricIdx == expectedMetrics - 1) ? Colors.White : UIStyle.ColorGoldDull;
					cellLbl.AddThemeColorOverride("font_color", desiredColor);
				}
			}

			rowIdx++;
		}

		for (int i = rowIdx; i < _dataRows.Count; i++)
		{
			if (_dataRows[i].Visible)
			{
				_dataRows[i].Visible = false;
			}
		}
	}
}