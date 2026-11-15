namespace Realm.Ecs;

/// <summary>
/// Marks a component or enum that is exposed to map scripts through <c>Realm.MapAPI.Components</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Enum)]
public class MapApiComponentAttribute : Attribute
{
}
