namespace Realm.Maps;

using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class Attribution_SourceMapAttribute : Attribute
{
	public Attribution_SourceMapAttribute(string sourceMap)
	{
		SourceMap = sourceMap;
	}

	public string SourceMap { get; }
}
