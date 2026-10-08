using Arch.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Stats;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Realm.Ecs.Services;

/// <summary>
/// Evaluation context for dynamic formula resolution against units, spell metadata, and dynamic properties.
/// </summary>
internal struct FormulaContext
{
	public World? World;
	public Entity Caster;
	public Entity Target;
	public Dictionary<string, float>? SpellData;
	public Dictionary<string, float>? DynamicData;

	public FormulaContext(World? world, Entity caster = default, Entity target = default, Dictionary<string, float>? spellData = null, Dictionary<string, float>? dynamicData = null)
	{
		World = world;
		Caster = caster;
		Target = target;
		SpellData = spellData;
		DynamicData = dynamicData;
	}
}

/// <summary>
/// Represents an evaluable expression node in a formula AST.
/// </summary>
internal interface IFormulaNode
{
	float Evaluate(in FormulaContext context);
}

/// <summary>
/// Executable compiled representation of a mathematical formula.
/// </summary>
internal class CompiledFormula
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

/// <summary>
/// Compiles and evaluates dynamic mathematical expressions against entity stats and runtime contexts.
/// </summary>
internal static partial class FormulaEvaluator
{
	private static readonly Dictionary<string, CompiledFormula> Cache = new(StringComparer.OrdinalIgnoreCase);

	public static CompiledFormula Compile(string expression)
	{
		if (string.IsNullOrWhiteSpace(expression))
		{
			return new CompiledFormula(new ConstantNode(0f));
		}

		if (Cache.TryGetValue(expression, out var cached))
		{
			return cached;
		}

		var tokens = Tokenize(expression);
		int index = 0;
		var root = ParseExpression(tokens, ref index);
		var compiled = new CompiledFormula(root);
		Cache[expression] = compiled;
		return compiled;
	}

	public static float Evaluate(string expression, in FormulaContext context)
	{
		var compiled = Compile(expression);
		return compiled.Evaluate(in context);
	}

	[GeneratedRegex(@"([0-9]+(?:\.[0-9]+)?)|([a-zA-Z_][a-zA-Z0-9_\.]*)|(\+|-|\*|\/|\^|\(|\)|,)")]
	private static partial Regex TokenizerRegex();

	private static List<string> Tokenize(string expression)
	{
		var tokens = new List<string>();
		var matches = TokenizerRegex().Matches(expression);
		foreach (Match match in matches)
		{
			if (match.Success)
			{
				tokens.Add(match.Value);
			}
		}
		return tokens;
	}

	private static IFormulaNode ParseExpression(List<string> tokens, ref int index)
	{
		var left = ParseTerm(tokens, ref index);

		while (index < tokens.Count)
		{
			string op = tokens[index];
			if (op == "+" || op == "-")
			{
				index++;
				var right = ParseTerm(tokens, ref index);
				left = new BinaryOpNode(left, op[0], right);
			}
			else
			{
				break;
			}
		}

		return left;
	}

	private static IFormulaNode ParseTerm(List<string> tokens, ref int index)
	{
		var left = ParsePower(tokens, ref index);

		while (index < tokens.Count)
		{
			string op = tokens[index];
			if (op == "*" || op == "/")
			{
				index++;
				var right = ParsePower(tokens, ref index);
				left = new BinaryOpNode(left, op[0], right);
			}
			else
			{
				break;
			}
		}

		return left;
	}

	private static IFormulaNode ParsePower(List<string> tokens, ref int index)
	{
		var left = ParseFactor(tokens, ref index);

		while (index < tokens.Count && tokens[index] == "^")
		{
			index++;
			var right = ParseFactor(tokens, ref index);
			left = new BinaryOpNode(left, '^', right);
		}

		return left;
	}

	private static IFormulaNode ParseFactor(List<string> tokens, ref int index)
	{
		if (index >= tokens.Count) return new ConstantNode(0f);

		string token = tokens[index];

		if (token == "-")
		{
			index++;
			var operand = ParseFactor(tokens, ref index);
			return new BinaryOpNode(new ConstantNode(0f), '-', operand);
		}

		if (token == "+")
		{
			index++;
			return ParseFactor(tokens, ref index);
		}

		if (token == "(")
		{
			index++;
			var expr = ParseExpression(tokens, ref index);
			if (index < tokens.Count && tokens[index] == ")") index++;
			return expr;
		}

		if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float constantVal))
		{
			index++;
			return new ConstantNode(constantVal);
		}

		if (index + 1 < tokens.Count && tokens[index + 1] == "(")
		{
			return ParseFunctionNode(tokens, ref index, token);
		}

		index++;
		return new VariableNode(token);
	}

	private static FunctionNode ParseFunctionNode(List<string> tokens, ref int index, string token)
	{
		string funcName = token.ToLowerInvariant();
		index += 2;
		var args = new List<IFormulaNode>();

		if (index < tokens.Count && tokens[index] != ")")
		{
			while (index < tokens.Count)
			{
				args.Add(ParseExpression(tokens, ref index));
				
				if (index >= tokens.Count || tokens[index] != ",")
				{
					break;
				}
				
				index++;
			}
		}

		if (index < tokens.Count && tokens[index] == ")")
		{
			index++;
		}

		return new FunctionNode(funcName, args.ToArray());
	}

	private class ConstantNode : IFormulaNode
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

	private class BinaryOpNode : IFormulaNode
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

	private class FunctionNode : IFormulaNode
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

	private class VariableNode : IFormulaNode
	{
		private readonly string _scope;
		private readonly string _name;

		public VariableNode(string fullVariable)
		{
			int dotIndex = fullVariable.IndexOf('.');
			if (dotIndex > 0)
			{
				_scope = fullVariable.Substring(0, dotIndex).ToLowerInvariant();
				_name = fullVariable.Substring(dotIndex + 1).ToLowerInvariant();
			}
			else
			{
				_scope = string.Empty;
				_name = fullVariable.ToLowerInvariant();
			}
		}

		public float Evaluate(in FormulaContext context)
		{
			return _scope switch
			{
				"caster" => ResolveEntityStat(context.World, context.Caster, _name),
				"target" => ResolveEntityStat(context.World, context.Target, _name),
				"spell" => TryGetDictionaryValue(context.SpellData, _name, out float spellVal) ? spellVal : 0f,
				"dynamic" => TryGetDictionaryValue(context.DynamicData, _name, out float dynamicVal) ? dynamicVal : 0f,
				_ => ResolveFallbackScope(in context)
			};
		}

		private float ResolveFallbackScope(in FormulaContext context)
		{
			if (TryGetDictionaryValue(context.SpellData, _name, out float spellVal))
			{
				return spellVal;
			}

			if (TryGetDictionaryValue(context.DynamicData, _name, out float dynamicVal))
			{
				return dynamicVal;
			}

			if (context.Caster != default)
			{
				return ResolveEntityStat(context.World, context.Caster, _name);
			}

			return 0f;
		}

		private static bool TryGetDictionaryValue(Dictionary<string, float>? dict, string key, out float value)
		{
			value = 0f;
			if (dict == null) return false;

			foreach (var kvp in dict)
			{
				if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
				{
					value = kvp.Value;
					return true;
				}
			}

			return false;
		}

		private static float ResolveEntityStat(World? world, Entity entity, string statName)
		{
			if (world == null || entity == default || !world.IsAlive(entity))
			{
				return 0f;
			}

			if (TryResolveAttributeStat(world, entity, statName, out float attrVal)) return attrVal;
			if (TryResolveCombatStat(world, entity, statName, out float combatVal)) return combatVal;
			if (TryResolveResourceStat(world, entity, statName, out float resourceVal)) return resourceVal;

			if (statName is "hero_level" or "lvl")
				return world.Has<Level>(entity) ? world.Get<Level>(entity).Value : 1f;

			return 0f;
		}

		private static bool TryResolveAttributeStat(World world, Entity entity, string statName, out float value)
		{
			value = 0f;
			if (!world.Has<UnitAttributes>(entity)) return false;

			var attrs = world.Get<UnitAttributes>(entity);
			value = statName switch
			{
				"vitality" or "vit" => attrs.Vitality,
				"strength" or "str" or "might" or "mig" => attrs.Strength,
				"agility" or "agi" => attrs.Agility,
				"intelligence" or "int" or "focus" or "foc" => attrs.Intelligence,
				"wisdom" or "wis" or "willpower" or "wil" => attrs.Wisdom,
				"fortune" or "fort" or "for" or "finesse" or "fin" => attrs.Fortune,
				_ => float.NaN
			};

			if (float.IsNaN(value))
			{
				value = 0f;
				return false;
			}

			return true;
		}

		private static float GetPhysicalPower(World world, Entity entity, bool hasDerived, bool hasAttack)
			=> hasDerived ? world.Get<DerivedCombatStats>(entity).TotalAttackDamage : (hasAttack ? world.Get<Attack>(entity).Damage : 0f);

		private static float GetArmor(World world, Entity entity, bool hasDerived)
			=> hasDerived ? world.Get<DerivedCombatStats>(entity).TotalArmor : (world.Has<Armor>(entity) ? world.Get<Armor>(entity).FlatArmor : 0f);

		private static float GetAttackSpeed(World world, Entity entity, bool hasDerived, bool hasAttack)
			=> hasDerived && world.Get<DerivedCombatStats>(entity).AttackDelay > 0f ? (1f / world.Get<DerivedCombatStats>(entity).AttackDelay) : (hasAttack && world.Get<Attack>(entity).Cooldown > 0f ? (1f / world.Get<Attack>(entity).Cooldown) : 1f);

		private static float GetAttackDelay(World world, Entity entity, bool hasDerived, bool hasAttack)
			=> hasDerived ? world.Get<DerivedCombatStats>(entity).AttackDelay : (hasAttack ? world.Get<Attack>(entity).Cooldown : 1.5f);

		private static float GetMovementSpeed(World world, Entity entity, bool hasDerived)
			=> hasDerived ? world.Get<DerivedCombatStats>(entity).MovementSpeed : (world.Has<MovementStats>(entity) ? world.Get<MovementStats>(entity).Speed : 5f);

		private static float GetCritChance(World world, Entity entity, bool hasDerived, bool hasAttack)
			=> hasDerived ? world.Get<DerivedCombatStats>(entity).CritChance : (hasAttack ? world.Get<Attack>(entity).CritChance : 0f);

		private static float GetCritMultiplier(World world, Entity entity, bool hasDerived, bool hasAttack)
			=> hasDerived ? world.Get<DerivedCombatStats>(entity).CritMultiplier : (hasAttack ? world.Get<Attack>(entity).CritMultiplier : 1.5f);

		private static float GetArmorPenetration(World world, Entity entity, bool hasDerived, bool hasAttack)
			=> hasDerived ? world.Get<DerivedCombatStats>(entity).FlatArmorPenetration : (hasAttack ? world.Get<Attack>(entity).FlatArmorPenetration : 0f);

		private static float TryResolveOffenseStat(World world, Entity entity, string statName, bool hasDerived, bool hasAttack)
		{
			return statName switch
			{
				"physical_power" or "base_damage" => GetPhysicalPower(world, entity, hasDerived, hasAttack),
				"spell_power" or "magic_amp" => hasDerived ? world.Get<DerivedCombatStats>(entity).SpellPower : 0f,
				"crit_chance" => GetCritChance(world, entity, hasDerived, hasAttack),
				"crit_multiplier" => GetCritMultiplier(world, entity, hasDerived, hasAttack),
				"armor_penetration" => GetArmorPenetration(world, entity, hasDerived, hasAttack),
				_ => float.NaN
			};
		}

		private static float TryResolveDefenseStat(World world, Entity entity, string statName, bool hasDerived)
		{
			return statName switch
			{
				"armor" => GetArmor(world, entity, hasDerived),
				"spell_ward" or "magic_resistance" => hasDerived ? world.Get<DerivedCombatStats>(entity).SpellWard : 0f,
				"tenacity" or "cc_reduction" => hasDerived ? world.Get<DerivedCombatStats>(entity).Tenacity : 0f,
				_ => float.NaN
			};
		}

		private static float TryResolveSpeedStat(World world, Entity entity, string statName, bool hasDerived, bool hasAttack)
		{
			return statName switch
			{
				"attack_speed" => GetAttackSpeed(world, entity, hasDerived, hasAttack),
				"attack_delay" => GetAttackDelay(world, entity, hasDerived, hasAttack),
				"movement_speed" => GetMovementSpeed(world, entity, hasDerived),
				"cast_point" => hasDerived ? world.Get<DerivedCombatStats>(entity).CastPoint : 0.3f,
				"cooldown_reduction" or "cdr" => hasDerived ? world.Get<DerivedCombatStats>(entity).CooldownReduction : 0f,
				_ => float.NaN
			};
		}

		private static bool TryResolveCombatStat(World world, Entity entity, string statName, out float value)
		{
			bool hasDerived = world.Has<DerivedCombatStats>(entity);
			bool hasAttack = world.Has<Attack>(entity);

			value = TryResolveOffenseStat(world, entity, statName, hasDerived, hasAttack);
			if (!float.IsNaN(value)) return true;

			value = TryResolveDefenseStat(world, entity, statName, hasDerived);
			if (!float.IsNaN(value)) return true;

			value = TryResolveSpeedStat(world, entity, statName, hasDerived, hasAttack);
			if (!float.IsNaN(value)) return true;

			value = 0f;
			return false;
		}

		private static float TryResolveHealthStat(World world, Entity entity, string statName)
		{
			bool hasHealth = world.Has<Health>(entity);
			return statName switch
			{
				"hp" or "current_hp" => hasHealth ? world.Get<Health>(entity).Current : 0f,
				"max_hp" => hasHealth ? world.Get<Health>(entity).Max : 0f,
				"hp_regen" => hasHealth ? world.Get<Health>(entity).HpRegen : 0f,
				_ => float.NaN
			};
		}

		private static float TryResolveManaStat(World world, Entity entity, string statName)
		{
			bool hasMana = world.Has<Mana>(entity);
			return statName switch
			{
				"mana" or "current_mana" => hasMana ? world.Get<Mana>(entity).Current : 0f,
				"max_mana" => hasMana ? world.Get<Mana>(entity).Max : 0f,
				"mana_regen" => hasMana ? world.Get<Mana>(entity).ManaRegen : 0f,
				_ => float.NaN
			};
		}

		private static bool TryResolveResourceStat(World world, Entity entity, string statName, out float value)
		{
			value = TryResolveHealthStat(world, entity, statName);
			if (!float.IsNaN(value)) return true;

			value = TryResolveManaStat(world, entity, statName);
			if (!float.IsNaN(value)) return true;

			value = 0f;
			return false;
		}
	}
}
