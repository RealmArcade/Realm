namespace Realm.Ecs.Services;

public class ConstantNode : IFormulaNode
{
	private readonly float _value;

	public ConstantNode(float value)
	{
		_value = value;
	}

	public float Evaluate(in FormulaContext context)
	{
		return _value;
	}
}