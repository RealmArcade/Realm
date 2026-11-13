using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Marks a unit the default hotkeys pick: F1 and F2 recall the player's tagged units, so a unit the map drives itself
///     stays out of a selection the player cannot command.
/// </summary>
[TagDefinition("IncludeInDefaultHotkeySelection", "IncludeInDefaultHotkeySelection", "Marks a unit the default hotkey selections include.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct IncludeInDefaultHotkeySelection;
