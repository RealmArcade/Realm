namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Holds network synchronization, snapshot sequences, command sequencing, and latency tracking state.
	/// </summary>
	public struct NetworkState
	{
		public int NextCommandId;
		public float CommandSendTimer;
		public ulong LastSnapshotReceivedTime;
		public int LastAppliedSnapshotSequence;
		public int LastReceivedBaselineSeq;
		public bool HasReceivedInitialBaseline;
		public int SnapshotSequence;
		public int LocalPeerId;
		public float DynamicInterpolationFactor;

		public NetworkState(
			int nextCommandId,
			float commandSendTimer,
			ulong lastSnapshotReceivedTime,
			int lastAppliedSnapshotSequence,
			int lastReceivedBaselineSeq,
			bool hasReceivedInitialBaseline,
			int snapshotSequence,
			int localPeerId)
		{
			NextCommandId = nextCommandId;
			CommandSendTimer = commandSendTimer;
			LastSnapshotReceivedTime = lastSnapshotReceivedTime;
			LastAppliedSnapshotSequence = lastAppliedSnapshotSequence;
			LastReceivedBaselineSeq = lastReceivedBaselineSeq;
			HasReceivedInitialBaseline = hasReceivedInitialBaseline;
			SnapshotSequence = snapshotSequence;
			LocalPeerId = localPeerId;
			DynamicInterpolationFactor = 10f;
		}
	}
}
