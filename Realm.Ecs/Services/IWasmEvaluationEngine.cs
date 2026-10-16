namespace Realm.Ecs.Services;

public interface IWasmEvaluationEngine
{
	float Evaluate(in FormulaContext context, float baseValue, float modifierValue, int level);
}
