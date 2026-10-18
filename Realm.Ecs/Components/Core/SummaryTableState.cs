using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents the current multiplayer summary statistics table display state.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct SummaryTableState
	{
		public bool Visible;
		public int TitleStringId;
		public int HeadersHandle;
		public int RowsHandle;

		public SummaryTableState(bool visible, string title, string[] headers, Dictionary<string, string[]> rows)
		{
			Visible = visible;
			TitleStringId = StringRegistry.GetOrAdd(title);
			HeadersHandle = ColdPayloadRegistry<string[]>.Register(headers);
			RowsHandle = ColdPayloadRegistry<Dictionary<string, string[]>>.Register(rows);
		}

		public string Title
		{
			get => StringRegistry.Get(TitleStringId);
			set => TitleStringId = StringRegistry.GetOrAdd(value);
		}

		public string[] Headers
		{
			get => ColdPayloadRegistry<string[]>.Get(HeadersHandle);
			set => HeadersHandle = ColdPayloadRegistry<string[]>.Register(value);
		}

		public Dictionary<string, string[]> Rows
		{
			get => ColdPayloadRegistry<Dictionary<string, string[]>>.Get(RowsHandle);
			set => RowsHandle = ColdPayloadRegistry<Dictionary<string, string[]>>.Register(value);
		}
	}
}
