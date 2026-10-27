using System.Runtime.InteropServices;
using Realm.Ecs;

namespace Realm.MapAPI;

/// <summary>
/// Provides global access to the current <see cref="IGameAPI"/> instance within the WebAssembly or host runtime execution context.
/// </summary>
public static class GameAPI
{
	private static IGameAPI _current = null!;

	/// <summary>
	/// Gets or sets the active <see cref="IGameAPI"/> instance.
	/// </summary>
	/// <remarks>
	/// Setting the instance also lets the text table answer identifiers this process never registered, which only the
	/// sandbox can do: its host owns every text a map script may be asked to render. A process that hosts the world
	/// itself registers the text it writes and keeps no answerer.
	/// </remarks>
	public static IGameAPI Current
	{
		get => _current;
		set
		{
			_current = value;
			StringRegistry.UnregisteredTextSource =
				value != null && RuntimeInformation.ProcessArchitecture == Architecture.Wasm ? value.GetStringText : null;
		}
	}
}
