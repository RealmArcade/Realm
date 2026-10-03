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
			string funcName = token.ToLowerInvariant();
			index += 2;
			var args = new List<IFormulaNode>();
			if (index < tokens.Count && tokens[index] != ")")
			{
				while (index < tokens.Count)
				{
					args.Add(ParseExpression(tokens, ref index));
					if (index < tokens.Count && tokens[index] == ",")
					{
						index++;
					}
					else
					{
						break;
					}
				}
			}
			if (index < tokens.Count && tokens[index] == ")") index++;
			return new FunctionNode(funcName, args.ToArray());
		}

		index++;
		return new VariableNode(token);
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
			if (_funcName == "min" && _args.Length >= 2)
				return MathF.Min(_args[0].Evaluate(in context), _args[1].Evaluate(in context));

			if (_funcName == "max" && _args.Length >= 2)
				return MathF.Max(_args[0].Evaluate(in context), _args[1].Evaluate(in context));

			if (_funcName == "clamp" && _args.Length >= 3)
			{
				float val = _args[0].Evaluate(in context);
				float min = _args[1].Evaluate(in context);
				float max = _args[2].Evaluate(in context);
				return Math.Clamp(val, min, max);
			}

			if (_funcName == "abs" && _args.Length >= 1)
				return MathF.Abs(_args[0].Evaluate(in context));

			if (_funcName == "pow" && _args.Length >= 2)
				return MathF.Pow(_args[0].Evaluate(in context), _args[1].Evaluate(in context));

			return 0f;
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
			if (_scope == "caster")
			{
				return ResolveEntityStat(context.World, context.Caster, _name);
			}

			if (_scope == "target")
			{
				return ResolveEntityStat(context.World, context.Target, _name);
			}

			if (_scope == "spell")
			{
				if (context.SpellData != null)
				{
					foreach (var kvp in context.SpellData)
					{
						if (string.Equals(kvp.Key, _name, StringComparison.OrdinalIgnoreCase))
							return kvp.Value;
					}
				}
				return 0f;
			}

			if (_scope == "dynamic")
			{
				if (context.DynamicData != null)
				{
					foreach (var kvp in context.DynamicData)
					{
						if (string.Equals(kvp.Key, _name, StringComparison.OrdinalIgnoreCase))
							return kvp.Value;
					}
				}
				return 0f;
			}

			if (context.SpellData != null)
			{
				foreach (var kvp in context.SpellData)
				{
					if (string.Equals(kvp.Key, _name, StringComparison.OrdinalIgnoreCase))
						return kvp.Value;
				}
			}

			if (context.DynamicData != null)
			{
				foreach (var kvp in context.DynamicData)
				{
					if (string.Equals(kvp.Key, _name, StringComparison.OrdinalIgnoreCase))
						return kvp.Value;
				}
			}

			if (context.Caster != default)
			{
				return ResolveEntityStat(context.World, context.Caster, _name);
			}

			return 0f;
		}

		private static float ResolveEntityStat(World? world, Entity entity, string statName)
		{
			if (world == null || entity == default || !world.IsAlive(entity))
			{
				return 0f;
			}

			return statName switch
			{
				"vitality" or "vit" => world.Has<UnitAttributes>(entity) ? world.Get<UnitAttributes>(entity).Vitality : 0f,
				"might" or "mig" => world.Has<UnitAttributes>(entity) ? world.Get<UnitAttributes>(entity).Might : 0f,
				"agility" or "agi" => world.Has<UnitAttributes>(entity) ? world.Get<UnitAttributes>(entity).Agility : 0f,
				"finesse" or "fin" => world.Has<UnitAttributes>(entity) ? world.Get<UnitAttributes>(entity).Finesse : 0f,
				"focus" or "foc" => world.Has<UnitAttributes>(entity) ? world.Get<UnitAttributes>(entity).Focus : 0f,
				"willpower" or "wil" => world.Has<UnitAttributes>(entity) ? world.Get<UnitAttributes>(entity).Willpower : 0f,

				"hero_level" or "lvl" => world.Has<Level>(entity) ? world.Get<Level>(entity).Value : 1f,

				"physical_power" or "base_damage" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).TotalAttackDamage
					: (world.Has<Attack>(entity) ? world.Get<Attack>(entity).Damage : 0f),

				"spell_power" or "magic_amp" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).SpellPower
					: 0f,

				"armor" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).TotalArmor
					: (world.Has<Armor>(entity) ? world.Get<Armor>(entity).FlatArmor : 0f),

				"spell_ward" or "magic_resistance" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).SpellWard
					: 0f,

				"tenacity" or "cc_reduction" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).Tenacity
					: 0f,

				"attack_speed" => world.Has<DerivedCombatStats>(entity) && world.Get<DerivedCombatStats>(entity).AttackDelay > 0f
					? (1f / world.Get<DerivedCombatStats>(entity).AttackDelay)
					: (world.Has<Attack>(entity) && world.Get<Attack>(entity).Cooldown > 0f ? (1f / world.Get<Attack>(entity).Cooldown) : 1f),

				"attack_delay" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).AttackDelay
					: (world.Has<Attack>(entity) ? world.Get<Attack>(entity).Cooldown : 1.5f),

				"movement_speed" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).MovementSpeed
					: (world.Has<MovementStats>(entity) ? world.Get<MovementStats>(entity).Speed : 5f),

				"cast_point" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).CastPoint
					: 0.3f,

				"crit_chance" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).CritChance
					: (world.Has<Attack>(entity) ? world.Get<Attack>(entity).CritChance : 0f),

				"crit_multiplier" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).CritMultiplier
					: (world.Has<Attack>(entity) ? world.Get<Attack>(entity).CritMultiplier : 1.5f),

				"armor_penetration" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).FlatArmorPenetration
					: (world.Has<Attack>(entity) ? world.Get<Attack>(entity).FlatArmorPenetration : 0f),

				"cooldown_reduction" or "cdr" => world.Has<DerivedCombatStats>(entity)
					? world.Get<DerivedCombatStats>(entity).CooldownReduction
					: 0f,

				"hp" or "current_hp" => world.Has<Health>(entity) ? world.Get<Health>(entity).Current : 0f,
				"max_hp" => world.Has<Health>(entity) ? world.Get<Health>(entity).Max : 0f,
				"hp_regen" => world.Has<Health>(entity) ? world.Get<Health>(entity).HpRegen : 0f,

				"mana" or "current_mana" => world.Has<Mana>(entity) ? world.Get<Mana>(entity).Current : 0f,
				"max_mana" => world.Has<Mana>(entity) ? world.Get<Mana>(entity).Max : 0f,
				"mana_regen" => world.Has<Mana>(entity) ? world.Get<Mana>(entity).ManaRegen : 0f,

				_ => 0f
			};
		}
	}
}
