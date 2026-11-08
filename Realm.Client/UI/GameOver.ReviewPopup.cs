// The write-a-review popup: star rating, tag chips, the text box and the submit path, kept together
// because the submit reads every control the popup built.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;


namespace Realm.Client.UI;

public partial class GameOver
{
	private void ShowReviewPopup()
	{
		string mapName = GetActiveMapName();
		bool hasExisting = _loadedReviews.ContainsKey(mapName);
		var existingReview = hasExisting ? _loadedReviews[mapName] : null;


		var rootPopup = new ColorRect();
		rootPopup.Name = "RootPopup_ColorRect";
		rootPopup.Color = new Color(0, 0, 0, 0.6f);
		rootPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(rootPopup);


		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(550, 420);
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		rootPopup.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);


		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, hasExisting ? Tr("EDIT YOUR REVIEW") : Tr("WRITE A REVIEW"), 22);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);


		var mapLabel = new Label();
		mapLabel.Name = "MapLabel_Label";
		mapLabel.Text = $"{Tr("Map")}: {mapName.ToUpper()}";
		mapLabel.HorizontalAlignment = HorizontalAlignment.Center;
		mapLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		mapLabel.AddThemeFontSizeOverride("font_size", 14);
		vbox.AddChild(mapLabel);


		var ratingHBox = new HBoxContainer();
		ratingHBox.Name = "RatingHBox_HBoxContainer";
		ratingHBox.Alignment = BoxContainer.AlignmentMode.Center;
		ratingHBox.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(ratingHBox);

		var ratingTitle = new Label();
		ratingTitle.Name = "RatingTitle_Label";
		ratingTitle.Text = Tr("Rating:");
		ratingTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		ratingTitle.AddThemeFontSizeOverride("font_size", 16);
		ratingHBox.AddChild(ratingTitle);

		var ratingOption = new OptionButton();
		ratingOption.Name = "RatingOption_OptionButton";
		ratingOption.CustomMinimumSize = new Vector2(180, 36);
		ratingOption.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		ratingOption.AddThemeStyleboxOverride("hover", UIStyle.CreateTextInput(true));
		ratingOption.AddThemeStyleboxOverride("pressed", UIStyle.CreateTextInput(true));
		ratingOption.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
		ratingOption.AddItem(Tr("5 Stars (Excellent)"), 5);
		ratingOption.AddItem(Tr("4 Stars (Good)"), 4);
		ratingOption.AddItem(Tr("3 Stars (Average)"), 3);
		ratingOption.AddItem(Tr("2 Stars (Poor)"), 2);
		ratingOption.AddItem(Tr("1 Star (Terrible)"), 1);

		if (hasExisting && existingReview != null)
		{
			int idx = 5 - existingReview.Rating;
			ratingOption.Select(idx);
		}
		else
		{
			ratingOption.Select(FiveStarRatingOptionIndex);
		}

		ratingHBox.AddChild(ratingOption);


		var commentsLabel = new Label();
		commentsLabel.Name = "CommentsLabel_Label";
		commentsLabel.Text = Tr("Your Review comments:");
		commentsLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		commentsLabel.AddThemeFontSizeOverride("font_size", 15);
		vbox.AddChild(commentsLabel);

		var commentsEdit = new TextEdit();
		commentsEdit.Name = "CommentsEdit_TextEdit";
		commentsEdit.SizeFlagsVertical = SizeFlags.ExpandFill;
		commentsEdit.PlaceholderText = Tr("Write your thoughts about this map here...");
		commentsEdit.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		commentsEdit.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		commentsEdit.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
		commentsEdit.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.5f, 0.5f));

		if (hasExisting && existingReview != null)
		{
			commentsEdit.Text = existingReview.Comments;
		}

		vbox.AddChild(commentsEdit);


		var actionsHBox = new HBoxContainer();
		actionsHBox.Name = "ActionsHBox_HBoxContainer";
		actionsHBox.Alignment = BoxContainer.AlignmentMode.Center;
		actionsHBox.AddThemeConstantOverride("separation", 25);
		vbox.AddChild(actionsHBox);

		var btnSubmit = new Button();
		btnSubmit.Name = "BtnSubmit_Button";
		btnSubmit.AddThemeConstantOverride("icon_max_width", 0);
		btnSubmit.CustomMinimumSize = new Vector2(150, 42);
		UIStyle.ApplyButtonText(btnSubmit, TranslationServer.Translate("Submit"), 16);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(btnSubmit);
		actionsHBox.AddChild(btnSubmit);

		var btnCancel = new Button();
		btnCancel.Name = "BtnCancel_Button";
		btnCancel.AddThemeConstantOverride("icon_max_width", 0);
		btnCancel.CustomMinimumSize = new Vector2(150, 42);
		UIStyle.ApplyButtonText(btnCancel, TranslationServer.Translate("Cancel"), 16);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(btnCancel);
		actionsHBox.AddChild(btnCancel);


		btnCancel.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			rootPopup.QueueFree();
		};
		btnCancel.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		btnSubmit.Pressed += async () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			int selectedRating = ratingOption.GetSelectedId();
			string commentText = commentsEdit.Text;

			var entry = new ReviewEntry { Rating = selectedRating, Comments = commentText };
			_loadedReviews[mapName] = entry;


			UIStyle.ApplyButtonText(_btnWriteReview, TranslationServer.Translate("Edit Your Review"), 18);
			rootPopup.QueueFree();

			double playtimeMin = 0.0;
			if (GodotObject.IsInstanceValid(GameContext.Lobby) && GameContext.Lobby.GameSessionStartTime != null)
			{
				playtimeMin = (DateTime.UtcNow - GameContext.Lobby.GameSessionStartTime.Value).TotalMinutes;
			}

			await ReportMetricsAsync(mapName, playtimeMin, selectedRating, true);
		};
		btnSubmit.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

}