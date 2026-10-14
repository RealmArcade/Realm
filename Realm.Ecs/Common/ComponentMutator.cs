namespace Realm.Ecs.Common;

/// <summary>
///     A delegate used by <see cref="WorldExtensions.Mutate{TComponent}"/> to perform
///     in-place mutations on a component without boxing or copying.
/// </summary>
public delegate void ComponentMutator<TComponent>(ref TComponent component)
	where TComponent : struct;