namespace Realm.Client.UI.MapEditor;

public struct SocketDefinition
{
	public string SocketId;
	public string DisplayName;
	public bool IsPseudoSocket;
	public HumanoidBone? AssociatedBone;

	public SocketDefinition(string socketId, string displayName, bool isPseudo, HumanoidBone? bone = null)
	{
		SocketId = socketId;
		DisplayName = displayName;
		IsPseudoSocket = isPseudo;
		AssociatedBone = bone;
	}
}