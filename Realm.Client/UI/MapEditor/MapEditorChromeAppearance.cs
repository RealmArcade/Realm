using Godot;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The chrome's look under the editor's appearance preferences: whether the screen frame is drawn, how far the side
/// panels and minimap dim when the frame is hidden, and the panel opacity. The nodes are looked up by path because they
/// belong to the scene, so these passes take the editor as the lookup root.
/// </summary>
internal static class MapEditorChromeAppearance
{
	public const string ScreenFramePath = "MapEditorScreenFrame";
	private const string LeftPanelPath = "LeftSlidePanel";
	private const string RightPanelPath = "RightSlidePanel";
	private const string TopBarPath = "BottomCenterBar/TopBar";
	private const string MinimapPath = "MinimapFrame";

	public static void Apply(Node owner, EditorPreferencesData prefs)
	{
		ApplyScreenFrame(owner, prefs);
		ApplyPanelModulation(owner, prefs);

		if (!prefs.HideChromeBorderOverlay)
		{
			ApplyPanelOpacity(owner, prefs);
		}
	}

	/// <summary>
	/// The artwork frame is the editor's only border; hiding it is what switches the chrome into its frameless mode.
	/// </summary>
	private static void ApplyScreenFrame(Node owner, EditorPreferencesData prefs)
	{
		var screenFrame = owner.GetNodeOrNull<TextureRect>(ScreenFramePath);
		if (screenFrame != null)
		{
			screenFrame.Visible = !prefs.HideChromeBorderOverlay;
		}
	}

	/// <summary>
	/// With the frame hidden the panels and minimap dim to half so the map shows through, and the top bar disappears
	/// entirely because it would otherwise float without a border to sit in.
	/// </summary>
	private static void ApplyPanelModulation(Node owner, EditorPreferencesData prefs)
	{
		var leftPanel = owner.GetNodeOrNull<Panel>(LeftPanelPath);
		var rightPanel = owner.GetNodeOrNull<Panel>(RightPanelPath);
		var topBar = owner.GetNodeOrNull<PanelContainer>(TopBarPath);
		var minimap = owner.GetNodeOrNull<PanelContainer>(MinimapPath);

		Color panelTint = prefs.HideChromeBorderOverlay ? new Color(1, 1, 1, 0.45f) : Colors.White;
		Color topBarTint = prefs.HideChromeBorderOverlay ? new Color(1, 1, 1, 0.0f) : Colors.White;
		Color minimapTint = prefs.HideChromeBorderOverlay ? new Color(1, 1, 1, 0.5f) : Colors.White;

		if (leftPanel != null) leftPanel.SelfModulate = panelTint;
		if (rightPanel != null) rightPanel.SelfModulate = panelTint;
		if (topBar != null) topBar.SelfModulate = topBarTint;
		if (minimap != null) minimap.SelfModulate = minimapTint;
	}

	private static void ApplyPanelOpacity(Node owner, EditorPreferencesData prefs)
	{
		var leftPanel = owner.GetNodeOrNull<Panel>(LeftPanelPath);
		var rightPanel = owner.GetNodeOrNull<Panel>(RightPanelPath);
		var tint = new Color(1, 1, 1, prefs.PanelOpacity);

		if (leftPanel != null) leftPanel.Modulate = tint;
		if (rightPanel != null) rightPanel.Modulate = tint;
	}
}
