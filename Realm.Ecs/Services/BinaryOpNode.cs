namespace Realm.Ecs.Services;

public class BinaryOpNode : IFormulaNode
{
	private readonly IFormulaNode _left;
	private readonly char _op;
	private readonly IFormulaNode _right;

	public BinaryOpNode(IFormulaNode left, char op, IFormulaNode right)
	{
		_left = left;
		_op = op;
		_right = right;
	}

	public float Evaluate(in FormulaContext context)
	{
		float l = _left.Evaluate(in context);
		float r = _right.Evaluate(in context);

		return _op switch
		{
			'+' => l + r,
			'-' => l - r,
			'*' => l * r,
			'/' => MathF.Abs(r) < 0.0000001f ? 0f : l / r,
			'^' => MathF.Pow(l, r),
			_ => 0f
		};
	}
}