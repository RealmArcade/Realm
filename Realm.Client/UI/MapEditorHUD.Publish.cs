using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Distribution;
using static Godot.Control;

namespace Realm.Client.UI;

public partial class MapEditorHUD
{
	private string GetAuthorshipKeysDirectory()
	{
		return ProjectSettings.GlobalizePath("user://appdata/keys/");
	}

	private bool EnsureSignedInForPublishing()
	{
		if (GameContext.Account.IsSignedIn) return true;

		ShowFeedback(TranslationServer.Translate("Sign in to publish maps. Offline play and editing still work without an account."));
		return false;
	}

	private bool EnsureAuthorshipKeyForPublishing()
	{
		string keysDirectory = GetAuthorshipKeysDirectory();
		var keys = AuthorshipKeyHelper.EnumerateAuthorshipKeys(keysDirectory);
		string accountUserName = GameContext.Account.DisplayName;

		foreach (AuthorshipKeyInfo keyInfo in keys)
		{
			if (string.Equals(keyInfo.UserName, accountUserName, StringComparison.OrdinalIgnoreCase))
			{
				_publishAuthorshipKeyPath = keyInfo.Path;
				return false;
			}
		}

		if (keys.Count > 0)
		{
			_publishAuthorshipKeyPath = keys[0].Path;
			return false;
		}

		if (string.IsNullOrWhiteSpace(accountUserName))
		{
			ShowFeedback(TranslationServer.Translate("Your account has no user name, so no authorship key can be created."));
			return false;
		}

		var (_, _, keyPath, createdNew) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keysDirectory, accountUserName);
		_publishAuthorshipKeyPath = keyPath;

		if (createdNew)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("A new authorship key has been generated at {0}. Please back up this file to keep ownership of your maps."), keyPath));
		}

		return createdNew;
	}

	private string GetSelectedAuthorshipKeyPath()
	{
		if (!string.IsNullOrEmpty(_publishAuthorshipKeyPath) && File.Exists(_publishAuthorshipKeyPath)) return _publishAuthorshipKeyPath;

		var keys = AuthorshipKeyHelper.EnumerateAuthorshipKeys(GetAuthorshipKeysDirectory());
		return keys.Count > 0 ? keys[0].Path : string.Empty;
	}

	private bool TryGetSelectedAuthorshipKey(out NSec.Cryptography.Key? key, out Realm.Shared.Metadata.AuthorshipKeyData? data, out string keyPath)
	{
		key = null;
		data = null;
		keyPath = GetSelectedAuthorshipKeyPath();

		return keyPath.Length > 0 && AuthorshipKeyHelper.TryLoadAuthorshipKey(keyPath, out key, out data);
	}

	private string GetPublishAuthorName()
	{
		if (AuthorshipKeyHelper.TryReadKeyFile(GetSelectedAuthorshipKeyPath(), out Realm.Shared.Metadata.AuthorshipKeyData? authorsKeyData) && authorsKeyData != null)
		{
			return authorsKeyData.UserName;
		}

		string accountUserName = GameContext.Account.DisplayName;
		return string.IsNullOrWhiteSpace(accountUserName) ? "MapAuthor" : accountUserName;
	}

	public void PublishMapActionExternal()
	{
		if (!EnsureSignedInForPublishing()) return;

		EnsureMapHasRealName(_ => OpenPublishMapPanel());
	}

	private void OpenPublishMapPanel()
	{
		string wsPath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory) ? GameContext.EditorService._editingDirectory : MapWorkspaceService.GetActiveWorkspacePath();

		if (!TryValidateMapPublishRequirements(wsPath)) return;

		bool createdAuthorshipKey = EnsureAuthorshipKeyForPublishing();

		var overlay = new ColorRect { Name = "PublishInstructionsOverlay", Color = new Color(0, 0, 0, 0.7f) };
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = CreatePublishMapPanel();
		center.AddChild(panel);

		var vbox = BuildPublishMapUI(panel);

		var title = new Label { Text = TranslationServer.Translate("Publish Map Instructions"), HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 24);
		vbox.AddChild(title);

		var optType = new OptionButton();
		optType.Name = "OptType_OptionButton";
		optType.AddItem(TranslationServer.Translate("Custom Arcade Map"), 0);
		optType.AddItem(TranslationServer.Translate("Reusable Asset Pack"), 1);
		optType.Selected = _mapSettingsDialog?.SelectedMapTypeIndex ?? 0;
		vbox.AddChild(optType);

		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		var instructionsText = new RichTextLabel { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, BbcodeEnabled = true };
		scroll.AddChild(instructionsText);
		vbox.AddChild(scroll);

		BindPublishMapInstructions(optType, instructionsText);

		CreatePublishMapOptimizationTip(wsPath, vbox);

		var chkFullExport = new CheckBox { Text = TranslationServer.Translate("Full Export (Include all physical assets in package, bypassing greenlit deduplication)"), ButtonPressed = false };
		chkFullExport.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyCheckboxStyle(chkFullExport);
		vbox.AddChild(chkFullExport);

		CreatePublishMapIdentityPanel(vbox);

		AddPublishMapActionButtons(vbox, overlay, chkFullExport);

		if (createdAuthorshipKey && TryGetSelectedAuthorshipKey(out NSec.Cryptography.Key? createdPublishKey, out _, out _) && createdPublishKey != null)
		{
			CheckCreatorRegistrationAndPrompt(createdPublishKey);
		}
	}

	private bool TryValidateMapPublishRequirements(string wsPath)
	{
		var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(wsPath);
		if (!assetsValid)
		{
			ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
			AppendWasmConsoleLog($"[ERROR] Publish failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
			return false;
		}

		if (!CheckAssetSizeValidation(wsPath))
		{
			return false;
		}

		return true;
	}

	private bool CheckAssetSizeValidation(string workspace)
	{
		var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(workspace);
		if (!sizesValid)
		{
			string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
			ShowFeedback(string.Format(TranslationServer.Translate("Publish failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
			AppendWasmConsoleLog($"[ERROR] Publish failed. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
			return false;
		}
		return true;
	}

	private PanelContainer CreatePublishMapPanel()
	{
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(1200, 800) };
		panel.AddThemeStyleboxOverride("panel",
			UIStyle.CreateButtonPlate(new Color(0.1f, 0.1f, 0.15f, 0.95f), new Color(0.3f, 0.3f, 0.35f, 1f), 4, 2));
		return panel;
	}

	private VBoxContainer BuildPublishMapUI(PanelContainer panel)
	{
		var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(1180, 780) };
		vbox.AddThemeConstantOverride("separation", 12);
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddChild(vbox);
		panel.AddChild(margin);

		return vbox;
	}

	private void BindPublishMapInstructions(OptionButton optType, RichTextLabel instructionsText)
	{
		string textArcade = TranslationServer.Translate("🚀 [b]Publishing Your Custom Map[/b]\nTo maintain a high-quality community arcade, all new maps start in a Beta-Testing Phase.\n\nOnce your map hits our community play-time metrics (gaining enough unique players, ratings, & community playtime), it will become available for official publishing to community maps screens.\n\nUntil then, it is up to you to share it with the community & market it until you hit that threshold.\nYour map is ready to play right now! Click the 'Export' button to share it. Host a lobby with your map & wait for players to join.\nMessage the community via discord channels, etc to explain your map & convince them to try it. If they enjoy it, they will probably re-host it, which will help you hit the graduation threshold more quickly.\n\nWhile in testing, your map name will include a prefix [Beta-Testing] so players know it's an active work-in-progress. However, you should still do as much personal testing as possible before public hosting to avoid a frustrating experience for your testers.\n\nAfter graduation, your map name will be permanently reserved to your creator profile so no one else can use that same name.");
		string textAssetPack = TranslationServer.Translate("📦 [b]Publishing a Reusable Asset Pack[/b]\nWant to share your custom 3D models, audio, or code scripts with other map makers?\n\nIn Realm, Asset Packs are published as playable Showcase/Demo Maps.\n\n[b]Build a Playground:[/b] Turn your asset pack into a map where players can preview the functionality provided by your systems, view your models, etc.\n\n[b]Gather Community Metrics:[/b] Just like a regular map, your asset pack will start in a \"beta\" phase before being promoted on community discovery pages. Read the \"Custom Arcade Map\" section for more information.\n\n[b]Easy Importing:[/b] Once a player has a copy of your map, they can import assets from it into their maps via the map editor.\n\n[b]Automatic Credit:[/b] When creators import from you, the system tracks your files' signatures and automatically adds your info to their map credits.");

		Action updateText = () => { instructionsText.Text = optType.Selected == 0 ? textArcade : textAssetPack; };
		updateText();
		optType.ItemSelected += (_) => updateText();
	}

	private void CreatePublishMapOptimizationTip(string wsPath, VBoxContainer vbox)
	{
		var (unprunedCount, unnormalizedPotentialCount, potentialSavedBytes) = MapNormalizationHelper.CheckOptimizationState(wsPath);
		if (unprunedCount <= 0 && unnormalizedPotentialCount <= 0) return;

		var tipPanel = new PanelContainer();
		tipPanel.Name = "TipPanel_PanelContainer";
		tipPanel.AddThemeStyleboxOverride("panel",
			UIStyle.CreateButtonPlate(new Color(0.2f, 0.17f, 0.1f, 0.95f), UIStyle.ColorGoldDull));

		var tipMargin = new MarginContainer();
		tipMargin.Name = "TipMargin_MarginContainer";
		tipMargin.AddThemeConstantOverride("margin_top", 6);
		tipMargin.AddThemeConstantOverride("margin_bottom", 6);
		tipMargin.AddThemeConstantOverride("margin_left", 10);
		tipMargin.AddThemeConstantOverride("margin_right", 10);
		tipPanel.AddChild(tipMargin);

		var tipLabel = new Label { Text = "💡 " + string.Format(TranslationServer.Translate("Optimization Advisory: We recommend running 'Prune Unused Assets' and 'Normalize References' in the Map Utilities dialog before publishing to remove unused files and reference shared greenlit assets (potential savings: {0})."), MapStorageService.FormatBytes(potentialSavedBytes)), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1160, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		tipLabel.AddThemeFontSizeOverride("font_size", 12);
		tipLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		tipMargin.AddChild(tipLabel);
		vbox.AddChild(tipPanel);
	}

	private void CreatePublishMapIdentityPanel(VBoxContainer vbox)
	{
		var identityPanel = new PanelContainer();
		identityPanel.Name = "IdentityPanel_PanelContainer";
		identityPanel.AddThemeStyleboxOverride("panel",
			UIStyle.CreateButtonPlate(new Color(0.12f, 0.15f, 0.22f, 0.95f), new Color(0.3f, 0.5f, 0.8f, 0.9f)));
		var idMargin = new MarginContainer();
		idMargin.Name = "IdMargin_MarginContainer";
		idMargin.AddThemeConstantOverride("margin_top", 8);
		idMargin.AddThemeConstantOverride("margin_bottom", 8);
		idMargin.AddThemeConstantOverride("margin_left", 12);
		idMargin.AddThemeConstantOverride("margin_right", 12);
		identityPanel.AddChild(idMargin);

		var idVBox = new VBoxContainer();
		idVBox.Name = "IdVBox_VBoxContainer";
		idVBox.AddThemeConstantOverride("separation", 6);
		idMargin.AddChild(idVBox);

		var idHeaderHBox = new HBoxContainer();
		idHeaderHBox.Name = "IdHeaderHBox_HBoxContainer";
		idHeaderHBox.AddThemeConstantOverride("separation", 8);
		var idHeaderLabel = new Label { Text = "🔑 " + TranslationServer.Translate("AUTHOR IDENTITY & KEY VERIFICATION"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		idHeaderLabel.AddThemeFontSizeOverride("font_size", 13);
		idHeaderLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		idHeaderHBox.AddChild(idHeaderLabel);
		idVBox.AddChild(idHeaderHBox);

		var keyRow = new HBoxContainer();
		keyRow.Name = "KeyRow_HBoxContainer";
		keyRow.AddThemeConstantOverride("separation", 8);

		var lblKeyCaption = new Label { Text = TranslationServer.Translate("Publishing Key:") };
		lblKeyCaption.AddThemeFontSizeOverride("font_size", 11);
		lblKeyCaption.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		keyRow.AddChild(lblKeyCaption);

		var optKeys = new OptionButton { Name = "PublishKeyOption_OptionButton", CustomMinimumSize = new Vector2(320, 26), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		optKeys.AddThemeFontSizeOverride("font_size", 11);
		optKeys.AddThemeConstantOverride("icon_max_width", 0);
		keyRow.AddChild(optKeys);

		Font? faFont = GetFontAwesomeFont();

		var btnViewKey = new Button { Name = "ViewKey_Button", Text = Realm.Client.Utils.UnicodeIcons.EYE, TooltipText = TranslationServer.Translate("Open the selected key"), CustomMinimumSize = new Vector2(34, 26) };
		btnViewKey.AddThemeConstantOverride("icon_max_width", 0);
		if (faFont != null) btnViewKey.AddThemeFontOverride("font", faFont);
		keyRow.AddChild(btnViewKey);

		var btnAddKey = new Button { Name = "AddKey_Button", Text = Realm.Client.Utils.UnicodeIcons.PLUS, TooltipText = TranslationServer.Translate("Create a new authorship key"), CustomMinimumSize = new Vector2(34, 26) };
		btnAddKey.AddThemeConstantOverride("icon_max_width", 0);
		if (faFont != null) btnAddKey.AddThemeFontOverride("font", faFont);
		keyRow.AddChild(btnAddKey);

		idVBox.AddChild(keyRow);

		var idDetailsLabel = new Label { Name = "IdDetailsLabel_Label", Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1156, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		idDetailsLabel.AddThemeFontSizeOverride("font_size", 11);
		idDetailsLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 1.0f));
		idVBox.AddChild(idDetailsLabel);

		string keysDirectory = GetAuthorshipKeysDirectory();

		void RefreshSelectedKeyDetails()
		{
			if (AuthorshipKeyHelper.TryReadKeyFile(_publishAuthorshipKeyPath, out Realm.Shared.Metadata.AuthorshipKeyData? selectedKeyData) && selectedKeyData != null)
			{
				string shortPubKey = selectedKeyData.PublicKey.Length > 16 ? $"{selectedKeyData.PublicKey[..8]}...{selectedKeyData.PublicKey[^8..]}" : selectedKeyData.PublicKey;
				idDetailsLabel.Text = string.Format(TranslationServer.Translate("Author: {0} | Public Key: {1}\nKey File: {2}"), selectedKeyData.UserName, shortPubKey, _publishAuthorshipKeyPath);
				return;
			}

			idDetailsLabel.Text = TranslationServer.Translate("No authorship key is selected.");
		}

		void RefreshKeyOptions()
		{
			var keys = AuthorshipKeyHelper.EnumerateAuthorshipKeys(keysDirectory);
			optKeys.Clear();

			int selectedIndex = 0;
			for (int index = 0; index < keys.Count; index++)
			{
				AuthorshipKeyInfo keyInfo = keys[index];
				optKeys.AddItem($"{keyInfo.UserName} — {Path.GetFileName(keyInfo.Path)}", index);
				if (string.Equals(keyInfo.Path, _publishAuthorshipKeyPath, StringComparison.OrdinalIgnoreCase)) selectedIndex = index;
			}

			if (keys.Count == 0) return;

			_publishAuthorshipKeyPath = keys[selectedIndex].Path;
			optKeys.Selected = selectedIndex;
		}

		optKeys.ItemSelected += selectedIndex =>
		{
			var keys = AuthorshipKeyHelper.EnumerateAuthorshipKeys(keysDirectory);
			if (selectedIndex < 0 || selectedIndex >= keys.Count) return;

			_publishAuthorshipKeyPath = keys[(int)selectedIndex].Path;
			RefreshSelectedKeyDetails();
		};

		btnViewKey.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			_authorSignatureDialog ??= new AuthorSignatureDialog(this);
			_authorSignatureDialog.OpenForKey(_publishAuthorshipKeyPath);
		};

		btnAddKey.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();

			if (!GameContext.Account.IsSignedIn)
			{
				ShowFeedback(TranslationServer.Translate("Sign in to create an authorship key."));
				return;
			}

			string accountUserName = GameContext.Account.DisplayName;
			if (string.IsNullOrWhiteSpace(accountUserName))
			{
				ShowFeedback(TranslationServer.Translate("Your account has no user name, so no authorship key can be created."));
				return;
			}

			var (createdKey, _, createdKeyPath, createdNew) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keysDirectory, accountUserName);
			_publishAuthorshipKeyPath = createdKeyPath;
			RefreshKeyOptions();
			RefreshSelectedKeyDetails();

			ShowFeedback(createdNew
				? string.Format(TranslationServer.Translate("A new authorship key has been generated for {0} at {1}. Please back up this file to keep ownership of your maps."), accountUserName, createdKeyPath)
				: string.Format(TranslationServer.Translate("Using the existing authorship key for {0} at {1}."), accountUserName, createdKeyPath));

			if (createdNew && createdKey != null)
			{
				CheckCreatorRegistrationAndPrompt(createdKey);
			}
		};

		RefreshKeyOptions();
		RefreshSelectedKeyDetails();

		var idBackupWarn = new Label { Text = "⚠️ " + TranslationServer.Translate("Important: Back up your key file! Your authorship key proves ownership of your map name and allows future updates. If you lose your key file, you will permanently lose ownership and the ability to update this map."), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(1156, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		idBackupWarn.AddThemeFontSizeOverride("font_size", 11);
		idBackupWarn.AddThemeColorOverride("font_color", new Color(1.0f, 0.75f, 0.35f));
		idVBox.AddChild(idBackupWarn);

		vbox.AddChild(identityPanel);
	}

	private void AddPublishMapActionButtons(VBoxContainer vbox, ColorRect overlay, CheckBox chkFullExport)
	{
		var hbox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		hbox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hbox);

		var btnPublish = new Button { Text = TranslationServer.Translate("Publish Map"), CustomMinimumSize = new Vector2(140, 40) };
		btnPublish.AddThemeConstantOverride("icon_max_width", 0);
		btnPublish.Pressed += () =>
		{
			bool isFullExport = chkFullExport.ButtonPressed;
			overlay.QueueFree();
			PublishMapActionView(isFullExport);
		};
		hbox.AddChild(btnPublish);

		var btnClose = new Button { Text = TranslationServer.Translate("Close"), CustomMinimumSize = new Vector2(120, 40) };
		btnClose.AddThemeConstantOverride("icon_max_width", 0);
		btnClose.Pressed += () => overlay.QueueFree();
		hbox.AddChild(btnClose);
	}
}