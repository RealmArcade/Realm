namespace Realm.Ecs.Services;

/// <summary>
/// Represents an evaluable expression node in a formula AST.
/// </summary>
public interface IFormulaNode
{
	float Evaluate(in FormulaContext context);
}