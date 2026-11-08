using Godot;

namespace Realm.Client.UI;

public class CardDragData
{
	public Control CardNode = null!;
	public Button HeaderButton = null!;
	public Control? ContentControl;
	public string? TitleText;
	public bool IsDragging;
	public bool HasMovedSincePress;
	public Vector2 DragStartMousePos;
	public Vector2 CardStartPos;
}