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
	private static readonly (string Label, int Perspective)[] Options =
	{
		("ALL", -1),
		("BLUE", 1),
		("RED", 2),
		("GREEN", 3),
		("YELLOW", 4),
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

		foreach (var (label, perspective) in Options)
		{
			var button = new Button();
			button.Name = $"Btn{label}_Button";
			button.Text = TranslationServer.Translate(label);
			int chosen = perspective;
			button.Pressed += () => setPerspective(chosen);
			specHBox.AddChild(button);
		}

		owner.AddChild(Root);
	}
}
