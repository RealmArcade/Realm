using System;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
/// The spectator's team switcher: one button per player colour plus ALL, which follows the action rather than a team.
/// The panel only reports the chosen perspective; what the camera and the shroud do with it belongs to the host.
/// </summary>
public sealed class SpectatorPerspectiveOverlay
{
	// The node names are spelled out rather than composed from the labels: they are part of the scene's surface, and a
	// composed name would change if a label ever did. The labels are translated where they are written because the
	// localization catalog is built from literals, so a label carried as a variable would drop out of it.
	private static (string Label, int Perspective, string NodeName)[] BuildOptions() => new[]
	{
		((string)TranslationServer.Translate("ALL"), -1, "BtnAll_Button"),
		((string)TranslationServer.Translate("BLUE"), 1, "BtnBlue_Button"),
		((string)TranslationServer.Translate("RED"), 2, "BtnRed_Button"),
		((string)TranslationServer.Translate("GREEN"), 3, "BtnGreen_Button"),
		((string)TranslationServer.Translate("YELLOW"), 4, "BtnYellow_Button"),
	};

	public PanelContainer? Root { get; private set; }
	/// <summary>
	/// Centres the switcher along the top edge. It is a fixed 300 wide, so the viewport alone decides the left offset.
	/// </summary>
	public void PlaceCentered(float viewportWidth)
	{
		if (Root == null || !GodotObject.IsInstanceValid(Root)) return;
		Root.Position = new Vector2((viewportWidth - 300f) / 2.0f, 10f);
	}



	public void Build(Node owner, Action<int> setPerspective)
	{
		Root = new PanelContainer();
		Root.Name = "SpectatorPanel";
		Root.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());

		var specVBox = new VBoxContainer();
		specVBox.Name = "SpecVBox_VBoxContainer";
		Root.AddChild(specVBox);

		var title = new Label();
		title.Name = "Title_Label";
		title.Text = TranslationServer.Translate("SPECTATOR MODE");
		UIStyle.ApplyTitle(title, TranslationServer.Translate("SPECTATOR MODE"), 12);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		specVBox.AddChild(title);

		var specHBox = new HBoxContainer();
		specHBox.Name = "SpecHBox_HBoxContainer";
		specHBox.Alignment = BoxContainer.AlignmentMode.Center;
		specHBox.AddThemeConstantOverride("separation", 10);
		specVBox.AddChild(specHBox);

		foreach (var (label, perspective, nodeName) in BuildOptions())
		{
			var button = new Button();
			button.Name = nodeName;
			button.Text = label;
			int chosen = perspective;
			button.Pressed += () => setPerspective(chosen);
			specHBox.AddChild(button);
		}

		owner.AddChild(Root);
	}
}
