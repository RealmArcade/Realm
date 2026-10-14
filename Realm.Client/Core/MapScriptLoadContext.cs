using System;
using System.Reflection;
using System.Runtime.Loader;

namespace Realm.Client.Core;

public class MapScriptLoadContext : AssemblyLoadContext
{
	public MapScriptLoadContext() : base(isCollectible: true)
	{
		Resolving += OnResolving;
	}

	private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
	{
		if (assemblyName.Name == "Realm.MapAPI")
		{
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (asm.GetName().Name == "Realm.MapAPI")
					return asm;
			}
		}

		return null;
	}
}