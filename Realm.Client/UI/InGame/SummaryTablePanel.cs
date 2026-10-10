using Godot;

namespace Realm.Client.UI.InGame;

public class SummaryTablePanel
{
	private PanelContainer _summaryPanel;
	private Label _titleLabel;
	private VBoxContainer _rowsContainer;

	public SummaryTablePanel(PanelContainer summaryPanel, Label titleLabel, VBoxContainer rowsContainer)
	{
		_summaryPanel = summaryPanel;
		_titleLabel = titleLabel;
		_rowsContainer = rowsContainer;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		_summaryPanel.Visible = viewModel.SummaryTableVisible;
		if (!viewModel.SummaryTableVisible)
		{
			return;
		}

		_titleLabel.Text = viewModel.SummaryTableTitle;

		ClearRows();

		string[] headers = GetHeaders(viewModel);
		
		CreateHeaderRow(headers);
		CreateSeparator();

		int expectedMetrics = headers.Length > 0 ? headers.Length - 1 : 0;
		CreateDataRows(viewModel, expectedMetrics);
	}

	private void ClearRows()
	{
		foreach (Node child in _rowsContainer.GetChildren())
		{
			_rowsContainer.RemoveChild(child);
			child.QueueFree();
		}
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

	private void CreateHeaderRow(string[] headers)
	{
		var headerRow = new HBoxContainer();
		headerRow.AddThemeConstantOverride("separation", 15);

		for (int i = 0; i < headers.Length; i++)
		{
			var colLabel = new Label { Text = headers[i] };
			colLabel.AddThemeFontSizeOverride("font_size", 12);
			colLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

			if (i == 0)
			{
				colLabel.CustomMinimumSize = new Vector2(100, 0);
			}
			else
			{
				colLabel.CustomMinimumSize = new Vector2(80, 0);
				colLabel.HorizontalAlignment = HorizontalAlignment.Right;
			}

			headerRow.AddChild(colLabel);
		}

		_rowsContainer.AddChild(headerRow);
	}

	private void CreateSeparator()
	{
		var sep = new HSeparator();
		sep.AddThemeColorOverride("separator_color", UIStyle.ColorBronze);
		_rowsContainer.AddChild(sep);
	}

	private void CreateDataRows(InGameHUDViewModel viewModel, int expectedMetrics)
	{
		foreach (var kvp in viewModel.SummaryTableRows)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 15);

			var playerLbl = new Label { Text = kvp.Key };
			playerLbl.AddThemeFontSizeOverride("font_size", 12);
			playerLbl.AddThemeColorOverride("font_color", Colors.White);
			playerLbl.CustomMinimumSize = new Vector2(100, 0);
			row.AddChild(playerLbl);

			string[] values = kvp.Value;
			for (int i = 0; i < expectedMetrics; i++)
			{
				string cellText = (values != null && i < values.Length) ? (values[i] ?? "") : "";
				var cellLbl = new Label { Text = cellText };
				cellLbl.AddThemeFontSizeOverride("font_size", 12);
				cellLbl.AddThemeColorOverride("font_color", (i == expectedMetrics - 1) ? Colors.White : UIStyle.ColorGoldDull);
				cellLbl.CustomMinimumSize = new Vector2(80, 0);
				cellLbl.HorizontalAlignment = HorizontalAlignment.Right;
				row.AddChild(cellLbl);
			}

			_rowsContainer.AddChild(row);
		}
	}
}