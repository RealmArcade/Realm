using Godot;

namespace Realm.Client.UI;

public class CardDragData
{
	public Control CardNode;
	public Button HeaderButton;
	public Control ContentControl;
	public string TitleText;
	public bool IsDragging;
	public bool HasMovedSincePress;
	public Vector2 DragStartMousePos;
	public Vector2 CardStartPos;
}