namespace Realm.Ecs.Services;

/// <summary>
/// Executable compiled representation of a mathematical formula.
/// </summary>
public class CompiledFormula
{
	private readonly IFormulaNode _root;

	public CompiledFormula(IFormulaNode root)
	{
		_root = root;
	}

	public float Evaluate(in FormulaContext context)
	{
		return _root.Evaluate(in context);
	}
}