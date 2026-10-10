using Arch.Core;

namespace Realm.Client;

public interface IEcsEntityWrapper
{
	Entity Entity { get; }
}