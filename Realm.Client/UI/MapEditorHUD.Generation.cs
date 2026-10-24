using System.IO;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;

namespace Realm.Client.UI;

public partial class MapEditorHUD
{
	private Button _btnRandomGen { get; set; }
	private PopupMenu _popupRandomGen { get; set; }
	private MapGenerationDialog _mapGenerationDialog { get; set; }

	private void SetupRandomGenerationMenu()
	{
		_btnRandomGen = GetNode<Button>("LeftSlidePanel/LeftScroll/LeftVBox/FileAccordion/ContentFile/BoxGenOps/FileGrid2_GridContainer/BtnRandomGen");
		SetupOptionButton(_btnRandomGen, $"{UnicodeIcons.DICE} " + TranslationServer.Translate("Random Gen"), ShowRandomGenerationPopup, 13, "Generate a new map from a seed, or import terrain from a reference image");

		_popupRandomGen = new PopupMenu { Name = "PopupRandomGen" };
		_popupRandomGen.AddItem($"{UnicodeIcons.DICE} " + TranslationServer.Translate("Procedural"), 0);
		_popupRandomGen.SetItemTooltip(0, TranslationServer.Translate("Generate a new map from a seed and terrain sliders"));
		_popupRandomGen.AddItem($"{UnicodeIcons.MAP_IMAGE} " + TranslationServer.Translate("From Image"), 1);
		_popupRandomGen.SetItemTooltip(1, TranslationServer.Translate("Replace the map with terrain classified from a reference image"));
		StylePopupMenu(_popupRandomGen);
		_popupRandomGen.IdPressed += OnRandomGenerationMenuItemPressed;
		AddChild(_popupRandomGen);
	}

	private void ShowRandomGenerationPopup()
	{
		if (_btnRandomGen == null)
		{
			return;
		}

		Vector2 popupPosition = _btnRandomGen.GetScreenPosition() + new Vector2(0, _btnRandomGen.Size.Y);
		_popupRandomGen.Popup(new Rect2I((Vector2I)popupPosition, Vector2I.Zero));
	}

	private void OnRandomGenerationMenuItemPressed(long itemId)
	{
		_popupRandomGen.Hide();

		if (itemId == 0)
		{
			OpenMapGenerationDialog();
			return;
		}

		ImportTerrainFromImageAction();
	}

	public void OpenMapGenerationDialog()
	{
		_mapGenerationDialog ??= new MapGenerationDialog(this);
		_mapGenerationDialog.OpenDialog();
	}

	private void ImportTerrainFromImageAction()
	{
		ShowConfirmationDialog(
			TranslationServer.Translate("Are you sure you want to replace the entire map with terrain imported from an image? This will delete all placed entities and overwrite the terrain."),
			OpenTerrainImagePicker);
	}

	private void OpenTerrainImagePicker()
	{
		string initialFolder = ResolveTerrainImageFolder();
		Error dialogError = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Minimap Image to Import Terrain"),
			initialFolder,
			string.Empty,
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[]
			{
				"*.png,*.jpg,*.jpeg,*.webp,*.gif ; Image Files (*.png, *.jpg, *.jpeg, *.webp, *.gif)",
				"*.png ; PNG Images (*.png)",
				"*.jpg,*.jpeg ; JPEG Images (*.jpg, *.jpeg)",
				"*.webp ; WebP Images (*.webp)",
				"*.gif ; GIF Images (*.gif)"
			},
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) => OnTerrainImageSelected(status, selectedPaths, initialFolder)));

		if (dialogError != Error.Ok)
		{
			ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private string ResolveTerrainImageFolder()
	{
		string lastFolder = GameContext.MapGenerationService.LastTerrainImageFolder;
		if (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
		{
			return lastFolder;
		}

		return defaultLoadFolder;
	}

	private void OnTerrainImageSelected(bool status, string[] selectedPaths, string fallbackFolder)
	{
		if (!status || selectedPaths == null || selectedPaths.Length == 0)
		{
			return;
		}

		string imagePath = selectedPaths[0];
		string directory = Path.GetDirectoryName(imagePath);
		GameContext.MapGenerationService.LastTerrainImageFolder = !string.IsNullOrWhiteSpace(directory) ? directory : fallbackFolder;
		GameContext.MapGenerationService.ImportTerrainFromImage(imagePath);
	}
}
