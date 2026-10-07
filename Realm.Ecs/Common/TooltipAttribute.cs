namespace Realm.Ecs.Common;

using System;

/// <summary>
/// Specifies a tooltip description for a property, field, or parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class TooltipAttribute : Attribute
{
	public string Text { get; }

	public TooltipAttribute(string text)
	{
		Text = text;
	}
}
