using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using MemoryPack;
using Realm.Ecs;

namespace Realm.Shared.Network;

/// <summary>
///     The units one description carries, held field by field rather than unit by unit: every entity id, then every
///     owner, then each position axis, then the scalars, then the flags, and finally the definition each unit names. A
///     record per unit interleaves eleven different kinds of value, so an encoder looking for redundancy sees the same
///     owner and the same definition scattered through the frame at a stride of one record; grouped into columns, each
///     column is a run of values of one kind that repeats within itself. The same units of a three hundred unit room
///     measured 10.6 percent smaller on the wire and 14 percent less encoder time than the records they replaced.
///     Values are quantized on the way in (<see cref="UnitQuantization" />): a position to a sixty-fourth of a metre, a
///     facing to a sixty-fifthousandth of a turn, a hit point to a sixty-fourth of a point. An integer column carries the
///     trailing zero bytes an encoder drops, where a float carries mantissa noise it cannot, and no screen resolves
///     finer than the step.
///     It reads like the list it replaces — a count, an indexer and an enumerator — so a peer applies a description the
///     same way it did, and a reader can only ever see the columns it was given.
/// </summary>
public sealed class UnitSection : IReadOnlyList<UnitSnapshot>
{
	private const int InitialCapacity = 16;

	private const byte IsDeadFlag = 1;
	private const byte IsBuildingFlag = 2;

	private int[] _entityIds = Array.Empty<int>();
	private int[] _ownerPlayerEntityIds = Array.Empty<int>();
	private sbyte[] _ownerPlayerIndexes = Array.Empty<sbyte>();
	private int[] _positionX = Array.Empty<int>();
	private int[] _positionY = Array.Empty<int>();
	private int[] _positionZ = Array.Empty<int>();
	private ushort[] _rotations = Array.Empty<ushort>();
	private int[] _currentHealths = Array.Empty<int>();
	private int[] _maximumHealths = Array.Empty<int>();
	private byte[] _flags = Array.Empty<byte>();
	private string[] _unitIds = Array.Empty<string>();

	private int _count;

	/// <summary>An empty section, which allocates nothing until the first unit is added to it.</summary>
	public UnitSection()
	{
	}

	/// <summary>A section with room for <paramref name="capacity" /> units before its columns have to grow.</summary>
	public UnitSection(int capacity)
	{
		if (capacity > 0) Grow(capacity);
	}

	public int Count => _count;

	/// <summary>How many units the columns have room for, which is at least <see cref="Count" />.</summary>
	public int Capacity => _entityIds.Length;

	public UnitSnapshot this[int index]
	{
		get
		{
			if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));

			byte flags = _flags[index];
			return new UnitSnapshot
			{
				EntityId = _entityIds[index],
				UnitId = _unitIds[index],
				OwnerPlayerEntityId = _ownerPlayerEntityIds[index],
				OwnerPlayerIndex = _ownerPlayerIndexes[index],
				Position = new NetworkVector3(
					UnitQuantization.DequantizePosition(_positionX[index]),
					UnitQuantization.DequantizePosition(_positionY[index]),
					UnitQuantization.DequantizePosition(_positionZ[index])),
				RotationY = UnitQuantization.DequantizeRotation(_rotations[index]),
				CurrentHp = UnitQuantization.DequantizeHealth(_currentHealths[index]),
				MaxHp = UnitQuantization.DequantizeHealth(_maximumHealths[index]),
				IsDead = (flags & IsDeadFlag) != 0,
				IsBuilding = (flags & IsBuildingFlag) != 0
			};
		}
	}

	/// <summary>Adds one described unit, quantizing it into the columns as it goes.</summary>
	public void Add(in UnitSnapshot unit)
	{
		if (_count == _entityIds.Length) Grow(_count + 1);

		int index = _count;
		_entityIds[index] = unit.EntityId;
		_ownerPlayerEntityIds[index] = unit.OwnerPlayerEntityId;
		_ownerPlayerIndexes[index] = (sbyte)unit.OwnerPlayerIndex;
		_positionX[index] = UnitQuantization.QuantizePosition(unit.Position.X);
		_positionY[index] = UnitQuantization.QuantizePosition(unit.Position.Y);
		_positionZ[index] = UnitQuantization.QuantizePosition(unit.Position.Z);
		_rotations[index] = UnitQuantization.QuantizeRotation(unit.RotationY);
		_currentHealths[index] = UnitQuantization.QuantizeHealth(unit.CurrentHp);
		_maximumHealths[index] = UnitQuantization.QuantizeHealth(unit.MaxHp);
		_flags[index] = (byte)((unit.IsDead ? IsDeadFlag : 0) | (unit.IsBuilding ? IsBuildingFlag : 0));
		_unitIds[index] = unit.UnitId ?? string.Empty;
		_count = index + 1;
	}

	/// <summary>Empties the section without giving up the room its columns already have, for a description rebuilt each tick.</summary>
	public void Clear() => _count = 0;

	private void Grow(int requiredCapacity)
	{
		int capacity = Math.Max(requiredCapacity, _entityIds.Length == 0 ? InitialCapacity : _entityIds.Length * 2);
		Array.Resize(ref _entityIds, capacity);
		Array.Resize(ref _ownerPlayerEntityIds, capacity);
		Array.Resize(ref _ownerPlayerIndexes, capacity);
		Array.Resize(ref _positionX, capacity);
		Array.Resize(ref _positionY, capacity);
		Array.Resize(ref _positionZ, capacity);
		Array.Resize(ref _rotations, capacity);
		Array.Resize(ref _currentHealths, capacity);
		Array.Resize(ref _maximumHealths, capacity);
		Array.Resize(ref _flags, capacity);
		Array.Resize(ref _unitIds, capacity);
	}

	public Enumerator GetEnumerator() => new(this);

	IEnumerator<UnitSnapshot> IEnumerable<UnitSnapshot>.GetEnumerator() => GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	///     Walks the columns in step, which is how a reader sees one unit at a time from a layout that stored one field
	///     at a time.
	/// </summary>
	public struct Enumerator : IEnumerator<UnitSnapshot>
	{
		private readonly UnitSection _section;
		private int _index;

		public Enumerator(UnitSection section)
		{
			_section = section;
			_index = -1;
		}

		public UnitSnapshot Current => _section[_index];

		object IEnumerator.Current => Current;

		public bool MoveNext() => ++_index < _section._count;

		public void Reset() => _index = -1;

		public void Dispose()
		{
		}
	}

	/// <summary>
	///     Writes the columns in the order they are held and reads them back into a section of the same shape, so the
	///     bytes a peer receives are the layout the encoder was measured on. Only the units the section holds travel: the
	///     columns keep room for the ones that will be added next, and that spare room is not the peer's business.
	/// </summary>
	public sealed class WireFormatter : MemoryPackFormatter<UnitSection>
	{
		public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref UnitSection? value)
		{
			if (value == null)
			{
				writer.WriteUnmanaged(-1);
				return;
			}

			int count = value._count;
			writer.WriteUnmanaged(count);
			writer.WriteUnmanagedSpan(value._entityIds.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._ownerPlayerEntityIds.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._ownerPlayerIndexes.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._positionX.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._positionY.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._positionZ.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._rotations.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._currentHealths.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._maximumHealths.AsSpan(0, count));
			writer.WriteUnmanagedSpan(value._flags.AsSpan(0, count));

			for (int index = 0; index < count; index++) writer.WriteString(value._unitIds[index]);
		}

		public override void Deserialize(ref MemoryPackReader reader, scoped ref UnitSection? value)
		{
			int declared = reader.ReadUnmanaged<int>();
			if (declared < 0)
			{
				value = null;
				return;
			}

			// A description cannot name more units than the world has entities, so a length above that is a corrupt
			// frame rather than a reason to reserve a column of that size.
			if (declared > EcsWorld.MAX_ENTITIES) throw new InvalidDataException($"A unit section declares {declared} units, more than a world holds.");

			int count = declared;
			var section = new UnitSection
			{
				_entityIds = reader.ReadUnmanagedArray<int>() ?? Array.Empty<int>(),
				_ownerPlayerEntityIds = reader.ReadUnmanagedArray<int>() ?? Array.Empty<int>(),
				_ownerPlayerIndexes = reader.ReadUnmanagedArray<sbyte>() ?? Array.Empty<sbyte>(),
				_positionX = reader.ReadUnmanagedArray<int>() ?? Array.Empty<int>(),
				_positionY = reader.ReadUnmanagedArray<int>() ?? Array.Empty<int>(),
				_positionZ = reader.ReadUnmanagedArray<int>() ?? Array.Empty<int>(),
				_rotations = reader.ReadUnmanagedArray<ushort>() ?? Array.Empty<ushort>(),
				_currentHealths = reader.ReadUnmanagedArray<int>() ?? Array.Empty<int>(),
				_maximumHealths = reader.ReadUnmanagedArray<int>() ?? Array.Empty<int>(),
				_flags = reader.ReadUnmanagedArray<byte>() ?? Array.Empty<byte>(),
				_unitIds = new string[count],
				_count = count
			};

			for (int index = 0; index < count; index++) section._unitIds[index] = reader.ReadString() ?? string.Empty;

			section.ValidateColumnLengths(count);
			value = section;
		}
	}

	/// <summary>
	///     A frame states how many units it describes before it states the columns, so every column has to hold exactly
	///     that many. A column that does not is a defect in the frame rather than something to pad or truncate: a record
	///     invented here would place a unit the authority never described.
	/// </summary>
	private void ValidateColumnLengths(int count)
	{
		if (_entityIds.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_entityIds.Length} entity ids.");
		if (_ownerPlayerEntityIds.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_ownerPlayerEntityIds.Length} owner entities.");
		if (_ownerPlayerIndexes.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_ownerPlayerIndexes.Length} owner indexes.");
		if (_positionX.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_positionX.Length} x positions.");
		if (_positionY.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_positionY.Length} y positions.");
		if (_positionZ.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_positionZ.Length} z positions.");
		if (_rotations.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_rotations.Length} rotations.");
		if (_currentHealths.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_currentHealths.Length} current healths.");
		if (_maximumHealths.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_maximumHealths.Length} maximum healths.");
		if (_flags.Length != count) throw new InvalidDataException($"A unit section describes {count} units but carries {_flags.Length} flags.");
	}
}

/// <summary>
///     Tells MemoryPack that a <see cref="UnitSection" /> is written by <see cref="UnitSection.WireFormatter" /> rather than
///     a record at a time. It is applied where a description is serialized, which is the one place that decides a frame's
///     layout.
/// </summary>
public sealed class UnitSectionFormatterAttribute : MemoryPackCustomFormatterAttribute<UnitSection>
{
	public override IMemoryPackFormatter<UnitSection> GetFormatter() => new UnitSection.WireFormatter();
}

/// <summary>
///     Makes the section's formatter known to MemoryPack for the whole process. The attribute above is read where a
///     serializer is generated for a description, but a section serialized on its own — which is how a test or a
///     measurement reads one — resolves its formatter through the provider, and a provider is filled in once rather
///     than looked at on every frame.
/// </summary>
public static class UnitSectionFormatterRegistration
{
	[ModuleInitializer]
	public static void Register() => MemoryPackFormatterProvider.Register(new UnitSection.WireFormatter());
}
