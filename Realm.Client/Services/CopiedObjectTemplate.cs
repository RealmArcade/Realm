namespace Realm.Client.Services;

/// <summary>What a clipboard entry places. Editor state only: never written to a file.</summary>
public enum CopiedObjectKind
{
	None,
	Unit,
	Prop,
	Decal
}

public struct CopiedObjectTemplate
{
	public CopiedObjectKind Type;
	public string Id;
	public float Rotation;
	public float Scale;
	public int Player;
}