namespace Realm.Ecs.Services;

public class FunctionNode : IFormulaNode
{
	private readonly string _funcName;
	private readonly IFormulaNode[] _args;

	public FunctionNode(string funcName, IFormulaNode[] args)
	{
		_funcName = funcName;
		_args = args;
	}

	public float Evaluate(in FormulaContext context)
	{
		return _funcName switch
		{
			"min" => EvaluateMin(in context),
			"max" => EvaluateMax(in context),
			"clamp" => EvaluateClamp(in context),
			"abs" => EvaluateAbs(in context),
			"pow" => EvaluatePow(in context),
			_ => 0f
		};
	}

	private float EvaluateMin(in FormulaContext context)
	{
		if (_args.Length < 2) return 0f;
		return MathF.Min(_args[0].Evaluate(in context), _args[1].Evaluate(in context));
	}

	private float EvaluateMax(in FormulaContext context)
	{
		if (_args.Length < 2) return 0f;
		return MathF.Max(_args[0].Evaluate(in context), _args[1].Evaluate(in context));
	}

	private float EvaluateClamp(in FormulaContext context)
	{
		if (_args.Length < 3) return 0f;
		float val = _args[0].Evaluate(in context);
		float min = _args[1].Evaluate(in context);
		float max = _args[2].Evaluate(in context);
		return Math.Clamp(val, min, max);
	}

	private float EvaluateAbs(in FormulaContext context)
	{
		if (_args.Length < 1) return 0f;
		return MathF.Abs(_args[0].Evaluate(in context));
	}

	private float EvaluatePow(in FormulaContext context)
	{
		if (_args.Length < 2) return 0f;
		return MathF.Pow(_args[0].Evaluate(in context), _args[1].Evaluate(in context));
	}
}