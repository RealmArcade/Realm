using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;

namespace Realm.Client.Testing;

public partial class MultiplayerSimulationHarness
{
	private const int AuthorityOrderStartTick = 120;
	private const int AuthorityOrderSpacingTicks = 26;
	private const int AuthorityOrderCount = 16;

	private int _authorityOrderTick;
	private int _authorityOrderIndex;

	private bool _authorityOrderReportedNoUnit;

}
