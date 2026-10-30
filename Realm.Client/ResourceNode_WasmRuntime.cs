using Godot;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client;

public class ResourceNode_WasmRuntime : IEcsPropWrapper
{
	private readonly Prop3D _prop;

	public ResourceNode_WasmRuntime(Prop3D prop)
	{
		_prop = prop;
	}

	Prop3D IEcsPropWrapper.Prop => _prop;

	public string ResourceType
	{
		get
		{
			if (!GodotObject.IsInstanceValid(_prop)) return string.Empty;
			return _prop.PropId switch
			{
				"goldmine" => "gold",
				"tree" => "wood",
				"rock" => "stone",
				_ => _prop.PropId ?? string.Empty
			};
		}
	}

	public Vector3 Position
	{
		get
		{
			if (!GodotObject.IsInstanceValid(_prop)) return Vector3.Zero;
			var pos = _prop.GlobalPosition;
			return new Vector3(pos.X, pos.Y, pos.Z);
		}
	}

	public float ResourceAmount
	{
		get
		{
			if (!GodotObject.IsInstanceValid(_prop)) return 0f;
			return _prop.ResourceAmount;
		}
		set
		{
			if (!GodotObject.IsInstanceValid(_prop)) return;
			_prop.ResourceAmount = value;
		}
	}

	public bool IsDepleted
	{
		get { return !GodotObject.IsInstanceValid(_prop) || _prop.ResourceAmount <= 0f; }
	}

	public void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f)
	{
		if (GodotObject.IsInstanceValid(_prop))
		{
			_prop.TriggerImpulse(strength, duration, frequency);
		}
	}
}