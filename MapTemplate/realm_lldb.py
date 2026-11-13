import lldb
import re
import struct

# Constants auto-discovered from EcsSharedMemory
ACTIVE_ENTITY_BITSET_OFFSET = 2048
ALIVE_OFFSET = 3072
ARMOR_OFFSET = 1223680
ATTACK_OFFSET = 633856
BITSET_CHUNKS = 128
COMPONENTS_START_OFFSET = 273408
COMPONENT_ARRAY_BASE_OFFSETS_OFFSET = 0
COMPONENT_STRIDES_OFFSET = 1024
ENTITY_MASKS_OFFSET = 11264
EXPERIENCE_OFFSET = 1551360
HEADER_SIZE = 2048
HEALTH_OFFSET = 371712
LEVEL_OFFSET = 1518592
LOBBY_FACTION_NAMES_OFFSET = 1651840
LOBBY_FACTION_NAME_CHARACTERS = 24
LOBBY_ROSTER_HEADER_OFFSET = 1651820
MANA_OFFSET = 535552
MAX_COMPONENT_TYPES = 256
MAX_ENTITIES = 8192
MAX_LOBBY_FACTIONS = 16
MAX_PLAYERS = 25
MAX_PLAYER_ENTITIES = 26
MODEL_SCALE_OFFSET = 1584128
MOVEMENT_STATS_OFFSET = 1321984
PLAYER_DAMAGE_EFFECTIVENESS_OFFSET = 1784320
PLAYER_POPULATION_OFFSET = 1651612
PLAYER_RESOURCES_OFFSET = 1649664
PLAYER_ROSTER_SLOTS_OFFSET = 1652608
POSITION_OFFSET = 273408
RANDOM_SEED_OFFSET = 2044
ROTATION_Y_OFFSET = 1616896
SCRIPT_PLAYER_OFFSET = 1650912
TOTAL_SIZE_BYTES = 4194304
UNIT_OWNER_PLAYER_OFFSET = 1485824

# Component array offsets auto-discovered from EcsWorld.BindSharedMemory
COMPONENT_OFFSETS = {
    "Attack": 732160,
    "Experience": 1616896,
    "Health": 470016,
    "Level": 1584128,
    "Mana": 633856,
    "ModelScale": 1649664,
    "MovementStats": 1387520,
    "PlayerDamageEffectiveness": 1784320,
    "PlayerPopulation": 1750116,
    "PlayerResources": 1747968,
    "Position": 273408,
    "PreviousPosition": 371712,
    "PreviousRotationY": 1715200,
    "Resistances": 1321984,
    "RotationY": 1682432,
    "ScriptPlayer": 1749216,
    "UnitOwnerPlayer": 1551360,
}

# Component element sizes auto-calculated from struct layouts
COMPONENT_SIZES = {
    "Attack": 72,
    "Experience": 4,
    "Health": 20,
    "Level": 4,
    "Mana": 12,
    "ModelScale": 4,
    "MovementStats": 20,
    "PlayerDamageEffectiveness": 100,
    "PlayerPopulation": 8,
    "PlayerResources": 48,
    "Position": 12,
    "PreviousPosition": 12,
    "PreviousRotationY": 4,
    "Resistances": 8,
    "RotationY": 4,
    "ScriptPlayer": 36,
    "UnitOwnerPlayer": 4,
}

# Component identifiers auto-discovered from ComponentIds
COMPONENT_IDS = {
    "AnimationsFrozen": 127,
    "Attack": 45,
    "Building": 94,
    "CommandsFrozen": 125,
    "Dead": 97,
    "Dormant": 135,
    "Experience": 106,
    "Frozen": 125,
    "Health": 2,
    "Hero": 98,
    "Invulnerable": 99,
    "Level": 108,
    "Mana": 4,
    "ModelScale": 110,
    "MovementStats": 59,
    "OverheadBarsHidden": 126,
    "PlayerDamageEffectiveness": 133,
    "PlayerOrdersLocked": 134,
    "PlayerPopulation": 26,
    "PlayerResources": 71,
    "Position": 0,
    "PreviousPosition": 1,
    "PreviousRotationY": 115,
    "RepairableBuilding": 136,
    "Resistances": 50,
    "RotationY": 114,
    "ScriptPlayer": 33,
    "Unit": 105,
    "UnitOwnerPlayer": 39,
    "Worker": 139,
}

# Handle types and their readable properties auto-discovered from Realm.Ecs and Realm.MapAPI
HANDLE_TYPES = {
    "LootItemId": {
        "name": "LootItemId",
        "label": "LootItem",
        "value_field": "Value",
        "min_value": 1,
        "max_value": 8192,
        "fields": [
            { "name": "Value", "kind": "value", "type": "int" },
            { "name": "IsValid", "kind": "valid", "type": "bool", "min": 1, "max": 8192 },
            { "name": "UniqueId", "kind": "value", "type": "int" },
            { "name": "Id", "kind": "value", "type": "int" },
            { "name": "ItemTypeId", "kind": "host", "type": "string" },
            { "name": "Position", "kind": "host", "type": "string" },
            { "name": "Charges", "kind": "host", "type": "string" },
            { "name": "Lifetime", "kind": "host", "type": "string" },
            { "name": "PickupRadius", "kind": "host", "type": "string" },
            { "name": "AutoPickup", "kind": "host", "type": "string" },
            { "name": "OwnerPlayerIndex", "kind": "host", "type": "string" },
            { "name": "IsDespawned", "kind": "host", "type": "string" },
        ],
        "summary_fields": [],
    },
    "PlayerId": {
        "name": "PlayerId",
        "label": "Player",
        "value_field": "Value",
        "min_value": 0,
        "max_value": 25,
        "fields": [
            { "name": "Value", "kind": "value", "type": "int" },
            { "name": "IsValid", "kind": "valid", "type": "bool", "min": 0, "max": 25 },
            { "name": "Index", "kind": "value", "type": "int" },
            { "name": "Id", "kind": "value", "type": "int" },
            { "name": "IsComputer", "kind": "component", "type": "bool", "component": "ScriptPlayer", "component_id": 33, "sub_offset": 13, "default": False },
            { "name": "Team", "kind": "component", "type": "int", "component": "ScriptPlayer", "component_id": 33, "sub_offset": 16, "default": 0 },
            { "name": "Gold", "kind": "component", "type": "int", "component": "PlayerResources", "component_id": 71, "index_offset": 1, "default": 0 },
            { "name": "Wood", "kind": "component", "type": "int", "component": "PlayerResources", "component_id": 71, "sub_offset": 4, "index_offset": 1, "default": 0 },
            { "name": "Stone", "kind": "component", "type": "int", "component": "PlayerResources", "component_id": 71, "sub_offset": 8, "index_offset": 1, "default": 0 },
            { "name": "MaxPopulation", "kind": "component", "type": "int", "component": "PlayerPopulation", "component_id": 26, "sub_offset": 4, "index_offset": 1, "default": 0 },
            { "name": "Kills", "kind": "component", "type": "int", "component": "ScriptPlayer", "component_id": 33, "sub_offset": 24, "default": 0 },
            { "name": "Name", "kind": "host", "type": "string" },
            { "name": "IsActive", "kind": "component", "type": "bool", "component": "ScriptPlayer", "component_id": 33, "sub_offset": 12, "default": False },
            { "name": "IsHost", "kind": "host", "type": "string" },
            { "name": "CurrentPopulation", "kind": "component", "type": "int", "component": "PlayerPopulation", "component_id": 26, "index_offset": 1, "default": 0 },
            { "name": "StartLocation", "kind": "host", "type": "string" },
            { "name": "Language", "kind": "host", "type": "string" },
            { "name": "ColorHex", "kind": "host", "type": "string" },
            { "name": "Color", "kind": "host", "type": "string" },
            { "name": "ColoredName", "kind": "host", "type": "string" },
        ],
        "summary_fields": ["Gold", "Wood", "CurrentPopulation", "MaxPopulation"],
    },
    "ResourceNodeId": {
        "name": "ResourceNodeId",
        "label": "ResourceNode",
        "value_field": "Value",
        "min_value": 0,
        "max_value": 2147483647,
        "fields": [
            { "name": "Value", "kind": "value", "type": "int" },
            { "name": "IsValid", "kind": "valid", "type": "bool", "min": 0, "max": 2147483647 },
            { "name": "Index", "kind": "value", "type": "int" },
            { "name": "Id", "kind": "value", "type": "int" },
            { "name": "ResourceAmount", "kind": "host", "type": "string" },
            { "name": "ResourceType", "kind": "host", "type": "string" },
            { "name": "Position", "kind": "host", "type": "string" },
            { "name": "IsDepleted", "kind": "host", "type": "string" },
        ],
        "summary_fields": [],
    },
    "UnitId": {
        "name": "UnitId",
        "label": "Unit",
        "value_field": "Value",
        "min_value": 1,
        "max_value": 8192,
        "fields": [
            { "name": "Value", "kind": "value", "type": "int" },
            { "name": "IsValid", "kind": "valid", "type": "bool", "min": 1, "max": 8192 },
            { "name": "UniqueId", "kind": "value", "type": "int" },
            { "name": "Id", "kind": "value", "type": "int" },
            { "name": "Health", "kind": "component", "type": "float", "component": "Health", "component_id": 2, "requires_alive": True, "default": 0 },
            { "name": "MaxHealth", "kind": "component", "type": "float", "component": "Health", "component_id": 2, "sub_offset": 4, "requires_alive": True, "default": 0 },
            { "name": "Mana", "kind": "component", "type": "float", "component": "Mana", "component_id": 4, "requires_alive": True, "default": 0 },
            { "name": "MaxMana", "kind": "component", "type": "float", "component": "Mana", "component_id": 4, "sub_offset": 4, "requires_alive": True, "default": 0 },
            { "name": "ManaRegen", "kind": "component", "type": "float", "component": "Mana", "component_id": 4, "sub_offset": 8, "requires_alive": True, "default": 0 },
            { "name": "Damage", "kind": "component", "type": "float", "component": "Attack", "component_id": 45, "requires_alive": True, "default": 0 },
            { "name": "Range", "kind": "component", "type": "float", "component": "Attack", "component_id": 45, "sub_offset": 4, "requires_alive": True, "default": 0 },
            { "name": "Armor", "kind": "component", "type": "float", "component": "Resistances", "component_id": 50, "requires_alive": True, "default": 0 },
            { "name": "Speed", "kind": "component", "type": "float", "component": "MovementStats", "component_id": 59, "requires_alive": True, "default": 0 },
            { "name": "AttackSpeed", "kind": "component", "type": "float", "component": "Attack", "component_id": 45, "sub_offset": 8, "requires_alive": True, "default": 0 },
            { "name": "Name", "kind": "host", "type": "string" },
            { "name": "Player", "kind": "component", "type": "int", "component": "UnitOwnerPlayer", "component_id": 39, "requires_alive": True, "default": -1 },
            { "name": "Level", "kind": "component", "type": "int", "component": "Level", "component_id": 108, "requires_alive": True, "default": 0 },
            { "name": "Experience", "kind": "component", "type": "float", "component": "Experience", "component_id": 106, "requires_alive": True, "default": 0 },
            { "name": "Scale", "kind": "component", "type": "float", "component": "ModelScale", "component_id": 110, "requires_alive": True, "default": 1 },
            { "name": "Invulnerable", "kind": "tag", "type": "bool", "component": "Invulnerable", "component_id": 99, "requires_alive": True, "tags": ["Invulnerable"], "tag_ids": [99], "default": False },
            { "name": "AnimationsFrozen", "kind": "tag", "type": "bool", "component": "AnimationsFrozen", "component_id": 127, "requires_alive": True, "tags": ["AnimationsFrozen"], "tag_ids": [127], "default": False },
            { "name": "CommandsFrozen", "kind": "tag", "type": "bool", "component": "CommandsFrozen", "component_id": 125, "requires_alive": True, "tags": ["CommandsFrozen"], "tag_ids": [125], "default": False },
            { "name": "PlayerOrdersLocked", "kind": "tag", "type": "bool", "component": "PlayerOrdersLocked", "component_id": 134, "requires_alive": True, "tags": ["PlayerOrdersLocked"], "tag_ids": [134], "default": False },
            { "name": "Frozen", "kind": "tag_any", "type": "bool", "component": "CommandsFrozen", "component_id": 125, "requires_alive": True, "tags": ["CommandsFrozen", "AnimationsFrozen"], "tag_ids": [125, 127], "default": False },
            { "name": "HideHealthAndManaBars", "kind": "tag", "type": "bool", "component": "OverheadBarsHidden", "component_id": 126, "requires_alive": True, "tags": ["OverheadBarsHidden"], "tag_ids": [126], "default": False },
            { "name": "Facing", "kind": "component", "type": "float", "component": "RotationY", "component_id": 114, "requires_alive": True, "default": 0 },
            { "name": "MagicResistance", "kind": "component", "type": "float", "component": "Resistances", "component_id": 50, "sub_offset": 4, "requires_alive": True, "default": 0 },
            { "name": "Position", "kind": "component", "type": "Vector3", "component": "Position", "component_id": 0, "requires_alive": True, "default": 0 },
            { "name": "TemplateID", "kind": "host", "type": "string" },
            { "name": "TypeId", "kind": "host", "type": "string" },
            { "name": "UnitTypeId", "kind": "host", "type": "string" },
            { "name": "UnitIdString", "kind": "host", "type": "string" },
            { "name": "IsBuilding", "kind": "tag", "type": "bool", "component": "Building", "component_id": 94, "requires_alive": True, "tags": ["Building"], "tag_ids": [94], "default": False },
            { "name": "IsHero", "kind": "tag", "type": "bool", "component": "Hero", "component_id": 98, "requires_alive": True, "tags": ["Hero"], "tag_ids": [98], "default": False },
            { "name": "XpBounty", "kind": "constant", "type": "float", "constant": 0 },
            { "name": "GoldBounty", "kind": "constant", "type": "float", "constant": 0 },
            { "name": "IsDead", "kind": "alive", "type": "bool", "invert": True, "default": False },
            { "name": "IsDormant", "kind": "tag", "type": "bool", "component": "Dormant", "component_id": 135, "requires_alive": True, "tags": ["Dormant"], "tag_ids": [135], "default": False },
        ],
        "summary_fields": ["Health", "MaxHealth", "Position", "Mana"],
    },
}

# Host wrapper types whose properties cross the WASM boundary and cannot be inspected without running threads
HOST_TYPES = {
    "GameAPI_WasmModule": {
        "name": "GameAPI_WasmModule",
        "label": "GameAPI",
        "fields": [
            { "name": "CheatsEnabled" },
            { "name": "GameElapsedTime" },
            { "name": "PlayerCount" },
            { "name": "ResourceNodeCount" },
        ],
    },
    "IGameAPI": {
        "name": "IGameAPI",
        "label": "IGameAPI",
        "fields": [
            { "name": "CheatsEnabled" },
            { "name": "GameElapsedTime" },
            { "name": "PlayerCount" },
            { "name": "ResourceNodeCount" },
        ],
    },
}

# Generated debugger script metadata
REALM_LLDB_SCRIPT_VERSION = "1"
REALM_LLDB_SCRIPT_HASH = "9f1594e9592b15151644ff9803ed70e8aa19fa3697e3b47d85ddaf6af687a0a4"
REALM_LLDB_SOURCE_COMMIT = "unknown"

INSPECTION_REQUIRES_THREADS = "Inspection requires threads to execute. Copy value to local variable to inspect"

_CACHED_BASE_ADDR = 0
_CACHED_DEBUG_METADATA = None
_CACHED_DEBUG_METADATA_PID = 0

def get_shared_memory_base(target):
    global _CACHED_BASE_ADDR
    if _CACHED_BASE_ADDR != 0:
        err = lldb.SBError()
        target.GetProcess().ReadMemory(_CACHED_BASE_ADDR, 4, err)
        if err.Success():
            return _CACHED_BASE_ADDR
        _CACHED_BASE_ADDR = 0

    metadata = get_debug_metadata(target)
    if metadata:
        ptr = metadata.get("sharedMemoryPointer", 0)
        if ptr != 0:
            err = lldb.SBError()
            target.GetProcess().ReadMemory(ptr, 4, err)
            if err.Success():
                _CACHED_BASE_ADDR = ptr
                return ptr

    candidate_names = [
        "g_realmSharedMemoryBase",
        "Realm_Ecs_EcsSharedMemory_g_realmSharedMemoryBase",
        "Realm.Ecs.EcsSharedMemory.g_realmSharedMemoryBase",
        "Realm::Ecs::EcsSharedMemory::g_realmSharedMemoryBase",
    ]

    process = target.GetProcess()

    raw_base = 0
    for name in candidate_names:
        var = target.FindFirstGlobalVariable(name)
        if var.IsValid():
            val = var.GetValueAsUnsigned(0)
            if val != 0:
                raw_base = val
                break
            load_addr = var.GetLoadAddress()
            if load_addr != lldb.LLDB_INVALID_ADDRESS and load_addr != 0:
                err = lldb.SBError()
                ptr_val = process.ReadPointerFromAddress(load_addr, err)
                if err.Success() and ptr_val != 0:
                    raw_base = ptr_val
                    break

    if raw_base == 0:
        for name in candidate_names:
            symbols = target.FindSymbols(name)
            if symbols.IsValid() and symbols.GetSize() > 0:
                for i in range(symbols.GetSize()):
                    sym = symbols.GetContextAtIndex(i).GetSymbol()
                    if sym.IsValid():
                        load_addr = sym.GetStartAddress().GetLoadAddress(target)
                        if load_addr != lldb.LLDB_INVALID_ADDRESS and load_addr != 0:
                            err = lldb.SBError()
                            ptr_val = process.ReadPointerFromAddress(load_addr, err)
                            if err.Success() and ptr_val != 0:
                                raw_base = ptr_val
                                break
                if raw_base != 0:
                    break

    if raw_base == 0:
        for expr in ["g_realmSharedMemoryBase", "&g_realmSharedMemoryBase"]:
            val = target.EvaluateExpression(expr)
            if val.IsValid():
                val_num = val.GetValueAsUnsigned(0)
                if val_num != 0:
                    raw_base = val_num
                    break

    if raw_base == 0:
        return 0

    err = lldb.SBError()
    process.ReadMemory(raw_base, 4, err)
    if err.Success():
        _CACHED_BASE_ADDR = raw_base
        return raw_base

    thread = process.GetSelectedThread()
    if thread.IsValid():
        frame = thread.GetSelectedFrame()
        if frame.IsValid():
            for reg_name in ["r14", "r15", "r13", "r12", "rbx"]:
                reg = frame.FindRegister(reg_name)
                if reg.IsValid():
                    reg_val = reg.GetValueAsUnsigned(0)
                    if reg_val > 0x100000000:
                        test_addr = reg_val + raw_base
                        err = lldb.SBError()
                        process.ReadMemory(test_addr, 4, err)
                        if err.Success():
                            _CACHED_BASE_ADDR = test_addr
                            return test_addr

    return raw_base

def resolve_array_base(process, base_addr, comp_id, default_offset):
    if base_addr == 0:
        return 0
    if comp_id >= 0:
        err = lldb.SBError()
        val_bytes = process.ReadMemory(base_addr + (comp_id * 4), 4, err)
        if err.Success() and len(val_bytes) == 4:
            val = struct.unpack_from("<I", val_bytes, 0)[0]
            if val != 0:
                t0_bytes = process.ReadMemory(base_addr, 4, err)
                if err.Success() and len(t0_bytes) == 4:
                    t0 = struct.unpack_from("<I", t0_bytes, 0)[0]
                    if val >= t0:
                        c_start = globals().get("COMPONENTS_START_OFFSET", 273408)
                        return base_addr + (val - t0) + c_start
                if val >= base_addr:
                    return val
                return base_addr + val
    return base_addr + default_offset

def resolve_component_stride(process, base_addr, comp_id, default_stride):
    if base_addr == 0 or comp_id < 0:
        return default_stride
    strides_offset = globals().get("COMPONENT_STRIDES_OFFSET", 1024)
    err = lldb.SBError()
    val_bytes = process.ReadMemory(base_addr + strides_offset + (comp_id * 4), 4, err)
    if err.Success() and len(val_bytes) == 4:
        val = struct.unpack_from("<I", val_bytes, 0)[0]
        if val > 0:
            return val
    return default_stride

def get_debug_metadata(target):
    global _CACHED_DEBUG_METADATA, _CACHED_DEBUG_METADATA_PID
    if not target or not target.IsValid():
        return None

    process = target.GetProcess()
    pid = process.GetProcessID()
    if _CACHED_DEBUG_METADATA is not None and _CACHED_DEBUG_METADATA_PID == pid:
        return _CACHED_DEBUG_METADATA

    import json
    import os
    for directory in workspace_search_dirs(target):
        path = os.path.join(directory, "MapScript.debug.json")
        if not os.path.exists(path):
            continue
        try:
            with open(path, "r") as debug_file:
                data = json.load(debug_file)
            if data.get("pid") != pid:
                continue
            _CACHED_DEBUG_METADATA = data
            _CACHED_DEBUG_METADATA_PID = pid
            return data
        except Exception:
            pass

    return None

def get_wasm_memory_base(target):
    metadata = get_debug_metadata(target)
    if metadata:
        wasm_base = metadata.get("wasmMemoryBase", 0)
        if wasm_base:
            return wasm_base
        shared_pointer = metadata.get("sharedMemoryPointer", 0)
        guest_offset = metadata.get("guestBaseOffset", 0)
        if shared_pointer and guest_offset is not None:
            return shared_pointer - guest_offset

    return get_shared_memory_base(target)

def guest_pointer_to_host(target, guest_pointer):
    if guest_pointer == 0:
        return 0
    wasm_base = get_wasm_memory_base(target)
    if wasm_base == 0:
        return 0
    return wasm_base + guest_pointer

def extract_template_inner_type(type_name):
    if not type_name:
        return None
    start = type_name.find("<")
    end = type_name.rfind(">")
    if start < 0 or end <= start:
        return None
    return type_name[start + 1:end]

def extract_array_element_type(type_name):
    if not type_name or not type_name.startswith("__Array<"):
        return None
    return extract_template_inner_type(type_name)

def get_type_field(type_obj, field_name):
    if not type_obj or not type_obj.IsValid():
        return None
    try:
        for index in range(type_obj.GetNumberOfFields()):
            field = type_obj.GetFieldAtIndex(index)
            if field and field.GetName() == field_name:
                return field
    except Exception:
        pass
    return None

def get_type_field_offset(type_obj, field_name, fallback):
    field = get_type_field(type_obj, field_name)
    if field:
        try:
            return field.GetOffsetInBytes()
        except Exception:
            pass
    return fallback

def type_is_managed_class(type_obj):
    if not type_obj or not type_obj.IsValid():
        return False
    try:
        return type_obj.GetNumberOfDirectBaseClasses() > 0
    except Exception:
        return False

def get_pointer_pointee_type(target, wrapper_type):
    try:
        inner = wrapper_type.GetTemplateArgumentType(0)
        if inner and inner.IsValid():
            return inner
    except Exception:
        pass

    inner_name = extract_template_inner_type(wrapper_type.GetName())
    if not inner_name:
        return None
    return target.FindFirstType(inner_name)

def read_wrapper_guest_pointer(valobj):
    child = valobj.GetChildMemberWithName("__ptr")
    if child and child.IsValid():
        return child.GetValueAsUnsigned(0)

    address = valobj.GetLoadAddress()
    if address and address != lldb.LLDB_INVALID_ADDRESS:
        return read_int(valobj.GetTarget().GetProcess(), address)
    return 0

def read_list_state(target, list_host_address, list_type):
    process = target.GetProcess()
    size_offset = get_type_field_offset(list_type, "_size", 8)
    items_offset = get_type_field_offset(list_type, "_items", 4)
    count = read_int(process, list_host_address + size_offset)
    items_guest_pointer = read_int(process, list_host_address + items_offset)
    if items_guest_pointer == 0:
        return count, 0, 0, 4, None

    items_host = guest_pointer_to_host(target, items_guest_pointer)
    if items_host == 0:
        return count, 0, 0, 4, None

    capacity = read_int(process, items_host + 4)
    data_host = items_host + 8
    element_type_name = extract_template_inner_type(list_type.GetName())
    element_type = target.FindFirstType(element_type_name) if element_type_name else None
    element_size = 4
    if element_type and element_type.IsValid():
        if type_is_managed_class(element_type):
            element_size = 4
        else:
            try:
                element_size = element_type.GetByteSize() or 4
            except Exception:
                element_size = 4

    return count, capacity, data_host, element_size, element_type

def read_array_state(target, array_host_address, array_type):
    process = target.GetProcess()
    length_offset = get_type_field_offset(array_type, "Length", 4)
    data_offset = get_type_field_offset(array_type, "Data", 8)
    length = read_int(process, array_host_address + length_offset)
    data_host = array_host_address + data_offset
    element_type_name = extract_array_element_type(array_type.GetName())
    element_type = target.FindFirstType(element_type_name) if element_type_name else None
    element_size = 4
    if element_type and element_type.IsValid():
        if type_is_managed_class(element_type):
            element_size = 4
        else:
            try:
                element_size = element_type.GetByteSize() or 4
            except Exception:
                element_size = 4

    return length, data_host, element_size, element_type

def read_utf16_string(process, address, length):
    if length <= 0 or length > 1000000:
        return ""
    data = read_bytes(process, address, length * 2)
    if data is None:
        return ""
    return data.decode("utf-16-le", errors="replace")

def quote_string(text):
    escaped = text.replace(chr(92), chr(92) + chr(92))
    escaped = escaped.replace('"', chr(92) + '"')
    escaped = escaped.replace(chr(10), chr(92) + "n")
    escaped = escaped.replace(chr(13), chr(92) + "r")
    escaped = escaped.replace(chr(9), chr(92) + "t")
    return '"' + escaped + '"'

def make_element_value(valobj, name, address, element_type, element_is_class):
    if not element_type or not element_type.IsValid():
        return None
    if element_is_class:
        guest_pointer = read_int(valobj.GetTarget().GetProcess(), address)
        host_address = guest_pointer_to_host(valobj.GetTarget(), guest_pointer)
        if host_address == 0:
            return None
        return valobj.CreateValueFromAddress(name, host_address, element_type)

    return valobj.CreateValueFromAddress(name, address, element_type)

def read_bytes(process, address, size):
    if address <= 0:
        return None
    err = lldb.SBError()
    data = process.ReadMemory(address, size, err)
    if not err.Success() or len(data) != size:
        return None
    return data

def read_byte(process, address):
    data = read_bytes(process, address, 1)
    if data is None:
        return 0
    return data[0]

def read_int(process, address):
    data = read_bytes(process, address, 4)
    if data is None:
        return 0
    return struct.unpack_from("<i", data, 0)[0]

def read_float(process, address):
    data = read_bytes(process, address, 4)
    if data is None:
        return 0.0
    return struct.unpack_from("<f", data, 0)[0]

def read_ulong(process, address):
    data = read_bytes(process, address, 8)
    if data is None:
        return 0
    return struct.unpack_from("<Q", data, 0)[0]

def read_alive(process, base_addr, entity_id):
    if entity_id <= 0 or entity_id >= globals().get("MAX_ENTITIES", 8192):
        return False
    return read_byte(process, base_addr + globals().get("ALIVE_OFFSET", 3072) + entity_id) != 0

def has_component(process, base_addr, entity_id, component_id):
    if entity_id <= 0 or entity_id >= globals().get("MAX_ENTITIES", 8192) or component_id < 0:
        return False
    word = component_id >> 6
    bit = component_id & 63
    address = base_addr + globals().get("ENTITY_MASKS_OFFSET", 11264) + entity_id * 32 + word * 8
    return (read_ulong(process, address) & (1 << bit)) != 0

def extract_id_value(valobj):
    if not valobj or not valobj.IsValid():
        return -1
    t = valobj.GetType()
    if t.IsReferenceType() or t.IsPointerType():
        valobj = valobj.Dereference()
    raw_val = valobj.GetNonSyntheticValue() if hasattr(valobj, "GetNonSyntheticValue") and valobj.GetNonSyntheticValue().IsValid() else valobj
    for candidate in ["Value", "value", "<Value>k__BackingField", "_value"]:
        child = raw_val.GetChildMemberWithName(candidate)
        if child.IsValid():
            return child.GetValueAsSigned(-1)
    val = raw_val.GetValueAsSigned(-1)
    if val != -1:
        return val
    data = raw_val.GetData()
    if data and data.GetByteSize() >= 4:
        err = lldb.SBError()
        return data.GetSignedInt32(err, 0)
    return -1

def resolve_field_type(target, f_type_name):
    f_type = target.FindFirstType(f_type_name)
    if not f_type.IsValid() and f_type_name == "Vector3":
        for candidate in ("S_P_CoreLib_System_Numerics_Vector3", "System.Numerics.Vector3"):
            f_type = target.FindFirstType(candidate)
            if f_type.IsValid():
                break
        if not f_type.IsValid():
            f_type = target.GetBasicType(lldb.eBasicTypeFloat).GetArrayType(3)
    if not f_type.IsValid():
        if f_type_name == "float":
            f_type = target.GetBasicType(lldb.eBasicTypeFloat)
        elif f_type_name == "bool":
            f_type = target.GetBasicType(lldb.eBasicTypeBool)
        else:
            f_type = target.GetBasicType(lldb.eBasicTypeInt)
    return f_type

def make_int_value(valobj, name, value):
    target = valobj.GetTarget()
    data = lldb.SBData.CreateDataFromSInt32Array(lldb.eByteOrderLittle, 4, [int(value)])
    return valobj.CreateValueFromData(name, data, target.GetBasicType(lldb.eBasicTypeInt))

def make_float_value(valobj, name, value):
    target = valobj.GetTarget()
    bits = struct.unpack("<I", struct.pack("<f", float(value)))[0]
    data = lldb.SBData.CreateDataFromUInt32Array(lldb.eByteOrderLittle, 4, [bits])
    return valobj.CreateValueFromData(name, data, target.GetBasicType(lldb.eBasicTypeFloat))

def make_bool_value(valobj, name, value):
    target = valobj.GetTarget()
    data = lldb.SBData.CreateDataFromUInt64Array(lldb.eByteOrderLittle, 1, [1 if value else 0])
    return valobj.CreateValueFromData(name, data, target.GetBasicType(lldb.eBasicTypeBool))

def make_string_value(valobj, name, text):
    target = valobj.GetTarget()
    data = lldb.SBData.CreateDataFromCString(lldb.eByteOrderLittle, 1, text)
    char_type = target.GetBasicType(lldb.eBasicTypeChar)
    return valobj.CreateValueFromData(name, data, char_type.GetArrayType(len(text) + 1))

def make_value(valobj, name, type_name, value):
    if type_name == "float":
        return make_float_value(valobj, name, value)
    if type_name == "bool":
        return make_bool_value(valobj, name, value)
    if type_name == "string":
        return make_string_value(valobj, name, value)
    if type_name == "Vector3":
        return make_string_value(valobj, name, str(value))
    return make_int_value(valobj, name, value)

def find_handle_spec(type_name):
    if not type_name:
        return None
    short_name = type_name.split(".")[-1]
    if short_name in HANDLE_TYPES:
        return HANDLE_TYPES[short_name]
    for name, spec in HANDLE_TYPES.items():
        if type_name.endswith(name):
            return spec
    return None

def find_host_spec(type_name):
    if not type_name:
        return None
    short_name = type_name.split(".")[-1]
    if short_name in HOST_TYPES:
        return HOST_TYPES[short_name]
    for name, spec in HOST_TYPES.items():
        if type_name.endswith(name):
            return spec
    return None

def find_field_spec(handle_spec, field_name):
    for field in handle_spec.get("fields", []):
        if field.get("name") == field_name:
            return field
    return None

def read_field_value(valobj, handle_spec, field_spec):
    kind = field_spec.get("kind", "host")
    value = extract_id_value(valobj)

    if kind == "value":
        return value
    if kind == "valid":
        return field_spec.get("min", 0) <= value < field_spec.get("max", 2147483647)
    if kind == "constant":
        return field_spec.get("constant")
    if kind == "host":
        return INSPECTION_REQUIRES_THREADS

    target = valobj.GetTarget()
    base_addr = get_shared_memory_base(target)
    if base_addr == 0:
        return field_spec.get("default")

    process = target.GetProcess()

    if kind == "alive":
        alive = read_alive(process, base_addr, value)
        return (not alive) if field_spec.get("invert") else alive

    if kind == "tag":
        if not (handle_spec.get("min_value", 0) <= value < handle_spec.get("max_value", 2147483647)):
            return field_spec.get("default", False)
        if field_spec.get("requires_alive") and not read_alive(process, base_addr, value):
            return field_spec.get("default", False)
        return has_component(process, base_addr, value, field_spec.get("component_id", -1))

    if kind == "tag_any":
        if not (handle_spec.get("min_value", 0) <= value < handle_spec.get("max_value", 2147483647)):
            return field_spec.get("default", False)
        if field_spec.get("requires_alive") and not read_alive(process, base_addr, value):
            return field_spec.get("default", False)
        for tag_id in field_spec.get("tag_ids", []):
            if has_component(process, base_addr, value, tag_id):
                return True
        return False

    if kind == "component":
        if not (handle_spec.get("min_value", 0) <= value < handle_spec.get("max_value", 2147483647)):
            return field_spec.get("default")
        if field_spec.get("requires_alive") and not read_alive(process, base_addr, value):
            return field_spec.get("default")
        index = value + field_spec.get("index_offset", 0)
        component_name = field_spec.get("component")
        component_id = field_spec.get("component_id", -1)
        default_offset = COMPONENT_OFFSETS.get(component_name, 0)
        default_stride = COMPONENT_SIZES.get(component_name, 0)
        array_base = resolve_array_base(process, base_addr, component_id, default_offset)
        stride = resolve_component_stride(process, base_addr, component_id, default_stride)
        address = array_base + index * stride + field_spec.get("sub_offset", 0)
        type_name = field_spec.get("type", "int")
        if type_name == "float":
            return read_float(process, address)
        if type_name == "bool":
            return read_byte(process, address) != 0
        if type_name == "Vector3":
            return (read_float(process, address), read_float(process, address + 4), read_float(process, address + 8))
        return read_int(process, address)

    return None

def format_summary_value(value):
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, float):
        return f"{value:.2f}"
    if isinstance(value, tuple):
        return "(" + ", ".join(f"{part:.1f}" for part in value) + ")"
    return str(value)

def workspace_search_dirs(target=None):
    import os
    dirs = [os.getcwd()]
    if target and target.IsValid():
        exe = target.GetExecutable()
        if exe and exe.IsValid():
            dirs.append(exe.GetDirectory())
    if "__file__" in globals():
        dirs.append(os.path.dirname(__file__))
    return [directory for directory in dirs if directory]

def find_workspace_file(name, target=None):
    import os
    for directory in workspace_search_dirs(target):
        path = os.path.join(directory, name)
        if os.path.exists(path):
            return path
    return None

def verify_component_tables(target):
    if not target or not target.IsValid():
        return ["no target is selected"]
    base_addr = get_shared_memory_base(target)
    if base_addr == 0:
        return ["shared memory base could not be resolved"]

    process = target.GetProcess()
    drift = []
    for component_name in sorted(COMPONENT_OFFSETS):
        component_id = COMPONENT_IDS.get(component_name, -1)
        if component_id < 0:
            drift.append(f"{component_name}: no component id")
            continue

        expected_offset = COMPONENT_OFFSETS[component_name]
        expected_stride = COMPONENT_SIZES.get(component_name, 0)
        actual_base = resolve_array_base(process, base_addr, component_id, expected_offset)
        actual_offset = actual_base - base_addr
        actual_stride = resolve_component_stride(process, base_addr, component_id, expected_stride)

        if actual_offset != expected_offset:
            drift.append(f"{component_name}: offset generated {expected_offset}, runtime {actual_offset}")
        if expected_stride and actual_stride != expected_stride:
            drift.append(f"{component_name}: stride generated {expected_stride}, runtime {actual_stride}")

    return drift

def realm_version(debugger, command, result, internal_dict):
    result.AppendMessage(f"realm_lldb.py version: {globals().get('REALM_LLDB_SCRIPT_VERSION', 'unknown')}")
    result.AppendMessage(f"script hash: {globals().get('REALM_LLDB_SCRIPT_HASH', 'unknown')}")
    result.AppendMessage(f"source commit: {globals().get('REALM_LLDB_SOURCE_COMMIT', 'unknown')}")
    result.AppendMessage(f"script path: {globals().get('__file__', 'unknown')}")
    result.AppendMessage(f"handle types: {len(HANDLE_TYPES)}")
    result.AppendMessage(f"host types: {len(HOST_TYPES)}")
    result.AppendMessage(f"components: {len(COMPONENT_OFFSETS)}")

def realm_verify(debugger, command, result, internal_dict):
    target = debugger.GetSelectedTarget()
    drift = verify_component_tables(target)
    if not drift:
        result.AppendMessage(f"realm_verify: {len(COMPONENT_OFFSETS)} component tables match the generated script")
        return

    result.AppendMessage(f"realm_verify: {len(drift)} drift(s) found")
    for line in drift:
        result.AppendMessage("  " + line)

def realm_doctor(debugger, command, result, internal_dict):
    import json
    target = debugger.GetSelectedTarget()
    lines = []

    def add(ok, message):
        lines.append(("[OK] " if ok else "[FAIL] ") + message)

    pid_path = find_workspace_file("authority.pid", target)
    if pid_path:
        try:
            with open(pid_path, "r") as pid_file:
                pid_text = pid_file.read().strip()
            add(pid_text.isdigit(), f"authority.pid: {pid_path} ({pid_text})")
        except Exception as exception:
            add(False, f"authority.pid: {pid_path} could not be read: {exception}")
    else:
        add(False, "authority.pid: not found")

    debug_path = find_workspace_file("MapScript.debug.json", target)
    if debug_path:
        try:
            with open(debug_path, "r") as debug_file:
                data = json.load(debug_file)
            pointer = data.get("sharedMemoryPointer", 0)
            add(pointer != 0, f"MapScript.debug.json: {debug_path} (pid {data.get('pid')}, shared memory {pointer})")
        except Exception as exception:
            add(False, f"MapScript.debug.json: {debug_path} could not be parsed: {exception}")
    else:
        add(False, "MapScript.debug.json: not found")

    elf_path = find_workspace_file("MapScript.jit.elf", target)
    if elf_path:
        try:
            with open(elf_path, "rb") as elf_file:
                magic = elf_file.read(4)
            add(magic == b"ELF", f"MapScript.jit.elf: {elf_path}")
        except Exception as exception:
            add(False, f"MapScript.jit.elf: {elf_path} could not be read: {exception}")
    else:
        add(False, "MapScript.jit.elf: not found")

    base_addr = get_shared_memory_base(target) if target and target.IsValid() else 0
    add(base_addr != 0, f"shared memory base: {base_addr}")
    add(True, f"script version: {globals().get('REALM_LLDB_SCRIPT_VERSION', 'unknown')}")
    add(True, f"script hash: {globals().get('REALM_LLDB_SCRIPT_HASH', 'unknown')}")
    add(True, f"source commit: {globals().get('REALM_LLDB_SOURCE_COMMIT', 'unknown')}")

    if base_addr != 0:
        drift = verify_component_tables(target)
        add(not drift, f"component tables: {len(drift)} drift(s)")
        for line in drift:
            lines.append("       " + line)

    for line in lines:
        result.AppendMessage(line)

_WATCHES = []
_WATCH_HOOK_INSTALLED = False
_WATCH_SAMPLE_LIMIT = 2000

def sparkline(values):
    if not values:
        return ""
    minimum = min(values)
    maximum = max(values)
    if maximum == minimum:
        return "=" * len(values)
    characters = " .:-=+*#%@"
    return "".join(characters[int((value - minimum) / (maximum - minimum) * (len(characters) - 1))] for value in values)

def record_watch_samples(frame):
    if not frame or not frame.IsValid():
        return
    for watch in _WATCHES:
        try:
            valobj = frame.EvaluateExpression(watch["expression"])
            if not valobj or not valobj.IsValid():
                continue
            value = read_field_value(valobj, watch["handle_spec"], watch["field_spec"])
            watch["samples"].append(value)
            if len(watch["samples"]) > _WATCH_SAMPLE_LIMIT:
                del watch["samples"][0]
        except Exception:
            pass

class RealmWatchStopHook:
    def handle_stop(self, exe_ctx, stream):
        record_watch_samples(exe_ctx.GetFrame())

def install_watch_hook(debugger):
    global _WATCH_HOOK_INSTALLED
    if _WATCH_HOOK_INSTALLED:
        return
    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        return
    debugger.HandleCommand('target stop-hook add -o "realm_watch_tick"')
    _WATCH_HOOK_INSTALLED = True

def realm_inspect(debugger, command, result, internal_dict):
    expression = command.strip()
    if not expression:
        result.SetError("usage: realm_inspect <handle-expression>")
        return

    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        result.SetError("no target is selected")
        return

    thread = target.GetProcess().GetSelectedThread()
    if not thread or not thread.IsValid():
        result.SetError("no thread is selected")
        return

    frame = thread.GetSelectedFrame()
    if not frame or not frame.IsValid():
        result.SetError("no frame is selected")
        return

    valobj = frame.EvaluateExpression(expression)
    if not valobj or not valobj.IsValid():
        result.SetError(f"could not evaluate '{expression}'")
        return

    type_name = valobj.GetType().GetName()
    if "WebAssemblyRefWrapper<" in type_name or "WebAssemblyPtrWrapper<" in type_name:
        provider = WebAssemblyRefWrapperSyntheticProvider(valobj, {})
        result.AppendMessage(f"{expression} = {WebAssemblyRefWrapperSummaryProvider(valobj, {})}")
        for index in range(provider.num_children()):
            child = provider.get_child_at_index(index)
            if child:
                value = child.GetValue()
                if value is None:
                    value = child.GetSummary()
                result.AppendMessage(f"  {provider.children[index][0]} = {value}")
        return

    handle_spec = find_handle_spec(type_name)
    if handle_spec:
        result.AppendMessage(f"{expression} = {RealmHandleSummaryProvider(valobj, {})}")
        for field in handle_spec.get("fields", []):
            value = read_field_value(valobj, handle_spec, field)
            result.AppendMessage(f"  {field.get('name')} = {format_summary_value(value)}")
        return

    host_spec = find_host_spec(type_name)
    if host_spec:
        result.AppendMessage(f"{expression} = {HostTypeSummaryProvider(valobj, {})}")
        for field in host_spec.get("fields", []):
            result.AppendMessage(f"  {field.get('name')} = {INSPECTION_REQUIRES_THREADS}")
        return

    value = valobj.GetValue()
    if value is None:
        value = valobj.GetSummary()
    result.AppendMessage(f"{expression} = {value}")

def realm_watch(debugger, command, result, internal_dict):
    arguments = command.strip()
    if not arguments or arguments == "list":
        if not _WATCHES:
            result.AppendMessage("realm_watch: no watches")
            return
        for index, watch in enumerate(_WATCHES):
            result.AppendMessage(f"{index}: {watch['expression']}.{watch['field_spec']['name']} ({len(watch['samples'])} samples)")
        return

    if arguments == "clear":
        _WATCHES.clear()
        result.AppendMessage("realm_watch: cleared")
        return

    if arguments.startswith("remove "):
        index_text = arguments[len("remove "):].strip()
        if not index_text.isdigit():
            result.SetError("usage: realm_watch remove <index>")
            return
        index = int(index_text)
        if 0 <= index < len(_WATCHES):
            del _WATCHES[index]
            result.AppendMessage(f"realm_watch: removed {index}")
        else:
            result.SetError(f"realm_watch: no watch at index {index}")
        return

    if arguments == "tick":
        target = debugger.GetSelectedTarget()
        if target and target.IsValid():
            record_watch_samples(target.GetProcess().GetSelectedThread().GetSelectedFrame())
        return

    parts = arguments.rsplit(None, 1)
    if len(parts) < 2:
        result.SetError("usage: realm_watch <handle-expression> <field>")
        return

    expression, field_name = parts
    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        result.SetError("no target is selected")
        return

    frame = target.GetProcess().GetSelectedThread().GetSelectedFrame()
    if not frame or not frame.IsValid():
        result.SetError("no frame is selected")
        return

    valobj = frame.EvaluateExpression(expression)
    if not valobj or not valobj.IsValid():
        result.SetError(f"could not evaluate '{expression}'")
        return

    handle_spec = find_handle_spec(valobj.GetType().GetName())
    if not handle_spec:
        result.SetError(f"'{expression}' is not a Realm handle")
        return

    field_spec = find_field_spec(handle_spec, field_name)
    if not field_spec:
        result.SetError(f"'{field_name}' is not a field of {handle_spec.get('name')}")
        return

    _WATCHES.append({"expression": expression, "handle_spec": handle_spec, "field_spec": field_spec, "samples": []})
    install_watch_hook(debugger)
    result.AppendMessage(f"realm_watch: watching {expression}.{field_name}")

def realm_plot(debugger, command, result, internal_dict):
    if not _WATCHES:
        result.AppendMessage("realm_plot: no watches")
        return

    for watch in _WATCHES:
        name = f"{watch['expression']}.{watch['field_spec']['name']}"
        samples = [value for value in watch["samples"] if isinstance(value, (int, float)) and not isinstance(value, bool)]
        if not samples:
            result.AppendMessage(f"{name}: no numeric samples")
            continue
        minimum = min(samples)
        maximum = max(samples)
        average = sum(samples) / len(samples)
        result.AppendMessage(f"{name}: n={len(samples)} min={minimum:.3f} max={maximum:.3f} avg={average:.3f}")
        result.AppendMessage("  " + sparkline(samples[-40:]))

def realm_export(debugger, command, result, internal_dict):
    import json
    path = command.strip()
    if not _WATCHES:
        result.AppendMessage("realm_export: no watches")
        return

    rows = []
    for watch in _WATCHES:
        name = f"{watch['expression']}.{watch['field_spec']['name']}"
        for index, value in enumerate(watch["samples"]):
            rows.append({"watch": name, "index": index, "value": value})

    if path.lower().endswith(".json"):
        content = json.dumps(rows, indent=2)
    else:
        lines = ["watch,index,value"]
        for row in rows:
            lines.append(f"{row['watch']},{row['index']},{row['value']}")
        content = chr(10).join(lines)

    if path:
        with open(path, "w") as output:
            output.write(content)
        result.AppendMessage(f"realm_export: wrote {len(rows)} sample(s) to {path}")
    else:
        result.AppendMessage(content)

HOST_FRAME_REGEX = re.compile(r"(__wasm|wasmtime|cabi_|[rR]hp|System\.|System_|System_Private_CoreLib|S_P_CoreLib_|Internal\.|GameClientWorld|Realm\.MapAPI\.|Realm_MapAPI_|Realm\.Ecs\.|Realm_Ecs_|Microsoft\.|GameAPI_WasmModule|.*_WasmModule|wasmImport|wasm_import|__import|dotnet_|mono_|wasi_|env\.)")

_STEP_UNTIL_USER = False
_STEP_RETRIES = 0
_STEP_HOOK_INSTALLED = False
_STOP_ON_WASM_TRAPS = False
_USER_SOURCE_FILES = None

def discover_user_source_files():
    global _USER_SOURCE_FILES
    if _USER_SOURCE_FILES is not None:
        return _USER_SOURCE_FILES

    import os
    workspace = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else os.getcwd()
    skipped_directories = {
        "bin", "obj", "lib", ".git", ".godot", "map_backups", "map_upgrades", "backups",
        ".backups", ".dotnet", ".wasi", ".sidecarcache", ".cache", ".vs", ".idea", "node_modules"
    }
    sources = set()
    for root, directories, files in os.walk(workspace):
        directories[:] = [directory for directory in directories if directory.lower() not in skipped_directories]
        for name in files:
            if not name.endswith(".cs"):
                continue
            if name.endswith(".g.cs") or name == "Attribution_SourceMapAttribute.cs":
                continue
            path = os.path.normcase(os.path.abspath(os.path.join(root, name))).replace("\\", "/")
            sources.add(path)

    _USER_SOURCE_FILES = sources
    return sources

def is_user_frame(frame):
    if not frame or not frame.IsValid():
        return False

    line_entry = frame.GetLineEntry()
    if line_entry and line_entry.IsValid():
        file_spec = line_entry.GetFileSpec()
        path = file_spec.fullpath if file_spec and file_spec.IsValid() else ""
        if path:
            import os
            normalized = os.path.normcase(os.path.abspath(path)).replace("\\", "/")
            return normalized in discover_user_source_files()

    function_name = frame.GetFunctionName() or ""
    if HOST_FRAME_REGEX.search(function_name):
        return False
    return bool(function_name)

def format_frame(frame):
    if not frame or not frame.IsValid():
        return "<no frame>"

    function_name = frame.GetFunctionName() or "<unknown>"
    line_entry = frame.GetLineEntry()
    if line_entry and line_entry.IsValid():
        file_spec = line_entry.GetFileSpec()
        path = file_spec.fullpath if file_spec and file_spec.IsValid() else ""
        line = line_entry.GetLine()
        if path:
            return f"{function_name} at {path}:{line}"

    return function_name

def collect_statement_entries(target):
    entries = []
    for module_index in range(target.GetNumModules()):
        module = target.GetModuleAtIndex(module_index)
        if not module or not module.IsValid():
            continue
        for compile_unit_index in range(module.GetNumCompileUnits()):
            compile_unit = module.GetCompileUnitAtIndex(compile_unit_index)
            if not compile_unit or not compile_unit.IsValid():
                continue
            try:
                line_entry_count = compile_unit.GetNumLineEntries()
            except Exception:
                continue
            for line_entry_index in range(line_entry_count):
                line_entry = compile_unit.GetLineEntryAtIndex(line_entry_index)
                if not line_entry or not line_entry.IsValid():
                    continue
                try:
                    if not line_entry.IsStatement():
                        continue
                except Exception:
                    continue
                file_spec = line_entry.GetFileSpec()
                path = file_spec.fullpath if file_spec and file_spec.IsValid() else ""
                if not path:
                    continue
                address = line_entry.GetStartAddress().GetLoadAddress(target)
                if not address or address == lldb.LLDB_INVALID_ADDRESS:
                    continue
                entries.append((path, line_entry.GetLine(), address))
    return entries

def select_statement_addresses(entries, file_name, line_number):
    import os
    addresses = []
    for path, line, address in entries:
        if line != line_number:
            continue
        if os.path.basename(path) != file_name:
            continue
        addresses.append(address)
    return sorted(set(addresses))

def resolve_line_addresses(target, file_name, line_number):
    entries = collect_statement_entries(target)
    addresses = select_statement_addresses(entries, file_name, line_number)

    try:
        breakpoint = target.BreakpointCreateByLocation(lldb.SBFileSpec(file_name), line_number)
        if breakpoint and breakpoint.IsValid():
            for index in range(breakpoint.GetNumLocations()):
                location = breakpoint.GetLocationAtIndex(index)
                address = location.GetAddress().GetLoadAddress(target)
                if not address or address == lldb.LLDB_INVALID_ADDRESS:
                    continue
                resolved_line = location.GetAddress().GetLineEntry().GetLine()
                if resolved_line == line_number and address not in addresses:
                    addresses.append(address)
            target.BreakpointDelete(breakpoint.GetID())
    except Exception:
        pass

    return sorted(set(addresses))

_JUST_MY_CODE_ENABLED = False
_JUST_MY_CODE_INSTALLED = False

def select_user_frame(thread):
    if not thread or not thread.IsValid():
        return None

    for index in range(thread.GetNumFrames()):
        frame = thread.GetFrameAtIndex(index)
        if is_user_frame(frame):
            thread.SetSelectedFrame(index)
            return frame

    return None

class RealmJustMyCodeStopHook:
    def handle_stop(self, exe_ctx, stream):
        if not _JUST_MY_CODE_ENABLED or _STEP_UNTIL_USER:
            return

        thread = exe_ctx.GetThread()
        if not thread or not thread.IsValid():
            return

        select_user_frame(thread)

def realm_just_my_code_tick(debugger, command, result, internal_dict):
    if not _JUST_MY_CODE_ENABLED or _STEP_UNTIL_USER:
        return

    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        return

    thread = target.GetProcess().GetSelectedThread()
    if thread and thread.IsValid():
        select_user_frame(thread)

def realm_step_tick(debugger, command, result, internal_dict):
    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        return

    thread = target.GetProcess().GetSelectedThread()
    if thread and thread.IsValid():
        select_user_frame(thread)

def realm_watch_tick(debugger, command, result, internal_dict):
    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        return

    thread = target.GetProcess().GetSelectedThread()
    if thread and thread.IsValid():
        record_watch_samples(thread.GetSelectedFrame())

def install_just_my_code_hook(debugger):
    global _JUST_MY_CODE_INSTALLED
    if _JUST_MY_CODE_INSTALLED:
        return
    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        return
    debugger.HandleCommand('target stop-hook add -o "realm_just_my_code_tick"')
    _JUST_MY_CODE_INSTALLED = True

def realm_just_my_code(debugger, command, result, internal_dict):
    global _JUST_MY_CODE_ENABLED
    argument = command.strip().lower()
    if argument == "status":
        result.AppendMessage(f"realm_just_my_code: {'on' if _JUST_MY_CODE_ENABLED else 'off'}")
        return

    if argument == "on":
        _JUST_MY_CODE_ENABLED = True
        install_just_my_code_hook(debugger)
        result.AppendMessage("realm_just_my_code: on")
        return

    if argument == "off":
        _JUST_MY_CODE_ENABLED = False
        result.AppendMessage("realm_just_my_code: off")
        return

    result.SetError("usage: realm_just_my_code [on|off|status]")

def realm_step(debugger, command, result, internal_dict):
    result.SetError("realm_step is disabled because CodeLLDB cannot resume from a Python command; use F10.")

def realm_break(debugger, command, result, internal_dict):
    import os

    parts = command.strip().rsplit(None, 1)
    if len(parts) != 2 or not parts[1].isdigit():
        result.SetError("usage: realm_break <file> <line>")
        return

    file_name = os.path.basename(parts[0].strip('"'))
    line_number = int(parts[1])
    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        result.SetError("no target is selected")
        return

    addresses = resolve_line_addresses(target, file_name, line_number)
    if not addresses:
        result.SetError(f"no statement address for {file_name}:{line_number}")
        return

    for address in addresses:
        debugger.HandleCommand(f"breakpoint set --address 0x{address:X}")
    result.AppendMessage(f"realm_break: {file_name}:{line_number} at {len(addresses)} address(es), first 0x{addresses[0]:X}")

def realm_recover(debugger, command, result, internal_dict):
    global _JUST_MY_CODE_ENABLED

    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        result.SetError("no target is selected")
        return

    thread = target.GetProcess().GetSelectedThread()
    if not thread or not thread.IsValid():
        result.SetError("no thread is selected")
        return

    frame = select_user_frame(thread)
    if frame is None:
        result.SetError("realm_recover: no user map-script frame found")
        return

    result.AppendMessage(f"realm_recover: selected {format_frame(frame)}")

def realm_reload(debugger, command, result, internal_dict):
    import importlib
    import os
    import sys

    module = sys.modules.get("realm_lldb")
    if module is None:
        result.SetError("realm_lldb is not loaded")
        return

    path = getattr(module, "__file__", "")
    if not path or not os.path.exists(path):
        result.SetError("realm_lldb.py path is unknown")
        return

    preserved_names = (
        "_JUST_MY_CODE_ENABLED",
        "_JUST_MY_CODE_INSTALLED",
        "_STEP_HOOK_INSTALLED",
        "_WATCH_HOOK_INSTALLED",
        "_WATCHES",
        "_STOP_ON_WASM_TRAPS",
        "_STEP_UNTIL_USER",
        "_STEP_RETRIES",
    )
    preserved = {name: getattr(module, name) for name in preserved_names if hasattr(module, name)}

    spec = getattr(module, "__spec__", None)
    if spec is not None and getattr(spec, "loader", None) is not None:
        spec.loader.exec_module(module)
    else:
        importlib.reload(module)

    for name, value in preserved.items():
        setattr(module, name, value)

    debugger.HandleCommand("type category delete Realm")
    module.__lldb_init_module(debugger, {})
    result.AppendMessage(f"realm_reload: reloaded {path}")

def find_authority_pid():
    import csv
    import io
    import subprocess
    import sys

    pid_path = find_workspace_file("authority.pid")
    if pid_path:
        try:
            with open(pid_path, "r") as pid_file:
                text = pid_file.read().strip()
            if text.isdigit():
                return int(text)
        except Exception:
            pass

    try:
        if sys.platform.startswith("win"):
            output = subprocess.check_output(
                ["tasklist", "/FI", "IMAGENAME eq Realm.ListenServerHost.exe", "/FO", "CSV", "/NH"],
                text=True,
                stderr=subprocess.DEVNULL)
            for row in csv.reader(io.StringIO(output)):
                if row and row[0].lower().startswith("realm.listenserverhost") and len(row) > 1 and row[1].isdigit():
                    return int(row[1])
        else:
            output = subprocess.check_output(["pgrep", "-f", "Realm.ListenServerHost"], text=True, stderr=subprocess.DEVNULL)
            for line in output.splitlines():
                if line.strip().isdigit():
                    return int(line.strip())
    except Exception:
        pass

    return None

def realm_attach(debugger, command, result, internal_dict):
    pid = command.strip()
    if pid and not pid.isdigit():
        result.SetError("usage: realm_attach [pid]")
        return

    if not pid:
        found = find_authority_pid()
        if found is None:
            result.SetError("authority.pid was not found and no Realm.ListenServerHost process was found.")
            return
        pid = str(found)

    result.AppendMessage(f"realm_attach: attaching to {pid}")
    debugger.HandleCommand(f"process attach --pid {pid}")

def realm_where(debugger, command, result, internal_dict):
    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        result.SetError("no target is selected")
        return

    thread = target.GetProcess().GetSelectedThread()
    if not thread or not thread.IsValid():
        result.SetError("no thread is selected")
        return

    for index in range(thread.GetNumFrames()):
        frame = thread.GetFrameAtIndex(index)
        if is_user_frame(frame):
            result.AppendMessage(f"realm_where: {format_frame(frame)}")
            return

    result.AppendMessage(f"realm_where: no user map-script frame found; current frame is {format_frame(thread.GetSelectedFrame())}")

def realm_exception(debugger, command, result, internal_dict):
    global _STOP_ON_WASM_TRAPS
    argument = command.strip().lower()
    if argument not in ("", "on", "off", "status"):
        result.SetError("usage: realm_exception [on|off|status]")
        return

    if argument == "status":
        result.AppendMessage(f"realm_exception: stopping on WASM traps is {'on' if _STOP_ON_WASM_TRAPS else 'off'}")
        return

    if argument == "on":
        _STOP_ON_WASM_TRAPS = True
    elif argument == "off":
        _STOP_ON_WASM_TRAPS = False
    else:
        _STOP_ON_WASM_TRAPS = not _STOP_ON_WASM_TRAPS

    debugger.HandleCommand(f"process handle SIGTRAP -s {'true' if _STOP_ON_WASM_TRAPS else 'false'} -n false -p false")
    result.AppendMessage(f"realm_exception: stopping on WASM traps is {'on' if _STOP_ON_WASM_TRAPS else 'off'}")

class RealmHandleSyntheticProvider:
    def __init__(self, valobj, internal_dict):
        self.valobj = valobj
        self.spec = find_handle_spec(valobj.GetType().GetName())
        self.fields = self.spec.get("fields", []) if self.spec else []

    def update(self):
        return False

    def num_children(self):
        return len(self.fields)

    def has_children(self):
        return len(self.fields) > 0

    def get_child_index(self, name):
        for index, field in enumerate(self.fields):
            if field.get("name") == name:
                return index
        return -1

    def get_child_at_index(self, index):
        if index < 0 or index >= len(self.fields):
            return None
        field = self.fields[index]
        value = read_field_value(self.valobj, self.spec, field)
        if value is None:
            return None
        return make_value(self.valobj, field.get("name", "Value"), field.get("type", "int"), value)

class WebAssemblyRefWrapperSyntheticProvider:
    def __init__(self, valobj, internal_dict):
        self.valobj = valobj
        self.target = valobj.GetTarget()
        self.inner_type = get_pointer_pointee_type(self.target, valobj.GetType())
        self.guest_pointer = 0
        self.host_pointer = 0
        self.mode = "invalid"
        self.children = []
        self.update()

    def update(self):
        self.guest_pointer = read_wrapper_guest_pointer(self.valobj)
        self.host_pointer = guest_pointer_to_host(self.target, self.guest_pointer)
        self.children = []
        self.mode = "invalid"

        if not self.inner_type or not self.inner_type.IsValid() or self.host_pointer == 0:
            return False

        type_name = self.inner_type.GetName()
        if type_name == "System_String":
            self.mode = "string"
            self.build_string()
        elif type_name.startswith("__Array<"):
            self.mode = "array"
            self.build_array()
        elif "System_Collections_Generic_List_1<" in type_name:
            self.mode = "list"
            self.build_list()
        else:
            self.mode = "object"
            self.build_object()

        return False

    def build_string(self):
        length_offset = get_type_field_offset(self.inner_type, "_stringLength", 4)
        char_offset = get_type_field_offset(self.inner_type, "_firstChar", 8)
        process = self.target.GetProcess()
        length = read_int(process, self.host_pointer + length_offset)
        text = read_utf16_string(process, self.host_pointer + char_offset, length)
        self.children.append(("Value", "string", text))
        self.children.append(("Length", "int", length))

    def build_list(self):
        count, capacity, data_host, element_size, element_type = read_list_state(self.target, self.host_pointer, self.inner_type)
        self.children.append(("Count", "int", count))
        self.children.append(("Capacity", "int", capacity))
        if data_host and element_type and element_type.IsValid():
            element_is_class = type_is_managed_class(element_type)
            for index in range(min(count, 1000)):
                self.children.append((f"[{index}]", "element", data_host + index * element_size, element_type, element_is_class))

    def build_array(self):
        length, data_host, element_size, element_type = read_array_state(self.target, self.host_pointer, self.inner_type)
        self.children.append(("Length", "int", length))
        if element_type and element_type.IsValid():
            element_is_class = type_is_managed_class(element_type)
            for index in range(min(length, 1000)):
                self.children.append((f"[{index}]", "element", data_host + index * element_size, element_type, element_is_class))

    def build_object(self):
        try:
            field_count = self.inner_type.GetNumberOfFields()
        except Exception:
            field_count = 0

        for index in range(field_count):
            field = self.inner_type.GetFieldAtIndex(index)
            if not field:
                continue
            name = field.GetName()
            field_type = field.GetType()
            offset = field.GetOffsetInBytes()
            self.children.append((name, "field", self.host_pointer + offset, field_type))

    def num_children(self):
        return len(self.children)

    def has_children(self):
        return len(self.children) > 0

    def get_child_index(self, name):
        for index, child in enumerate(self.children):
            if child[0] == name:
                return index
        return -1

    def get_child_at_index(self, index):
        if index < 0 or index >= len(self.children):
            return None

        child = self.children[index]
        name = child[0]
        kind = child[1]
        if kind == "int":
            return make_int_value(self.valobj, name, child[2])
        if kind == "string":
            return make_string_value(self.valobj, name, child[2])
        if kind == "element":
            return make_element_value(self.valobj, name, child[2], child[3], child[4])
        if kind == "field":
            return self.valobj.CreateValueFromAddress(name, child[2], child[3])
        return None

class ManagedListSyntheticProvider:
    def __init__(self, valobj, internal_dict):
        self.valobj = valobj
        self.target = valobj.GetTarget()
        self.list_type = valobj.GetType()
        self.list_host = valobj.GetLoadAddress()
        self.children = []
        self.build()

    def build(self):
        if not self.list_host or self.list_host == lldb.LLDB_INVALID_ADDRESS:
            return
        count, capacity, data_host, element_size, element_type = read_list_state(self.target, self.list_host, self.list_type)
        self.children.append(("Count", "int", count))
        self.children.append(("Capacity", "int", capacity))
        if data_host and element_type and element_type.IsValid():
            element_is_class = type_is_managed_class(element_type)
            for index in range(min(count, 1000)):
                self.children.append((f"[{index}]", "element", data_host + index * element_size, element_type, element_is_class))

    def update(self):
        self.list_host = self.valobj.GetLoadAddress()
        self.children = []
        self.build()
        return False

    def num_children(self):
        return len(self.children)

    def has_children(self):
        return len(self.children) > 0

    def get_child_index(self, name):
        for index, child in enumerate(self.children):
            if child[0] == name:
                return index
        return -1

    def get_child_at_index(self, index):
        if index < 0 or index >= len(self.children):
            return None
        child = self.children[index]
        if child[1] == "int":
            return make_int_value(self.valobj, child[0], child[2])
        if child[1] == "element":
            return make_element_value(self.valobj, child[0], child[2], child[3], child[4])
        return None

class ManagedArraySyntheticProvider:
    def __init__(self, valobj, internal_dict):
        self.valobj = valobj
        self.target = valobj.GetTarget()
        self.array_type = valobj.GetType()
        self.array_host = valobj.GetLoadAddress()
        self.children = []
        self.build()

    def build(self):
        if not self.array_host or self.array_host == lldb.LLDB_INVALID_ADDRESS:
            return
        length, data_host, element_size, element_type = read_array_state(self.target, self.array_host, self.array_type)
        self.children.append(("Length", "int", length))
        if element_type and element_type.IsValid():
            element_is_class = type_is_managed_class(element_type)
            for index in range(min(length, 1000)):
                self.children.append((f"[{index}]", "element", data_host + index * element_size, element_type, element_is_class))

    def update(self):
        self.array_host = self.valobj.GetLoadAddress()
        self.children = []
        self.build()
        return False

    def num_children(self):
        return len(self.children)

    def has_children(self):
        return len(self.children) > 0

    def get_child_index(self, name):
        for index, child in enumerate(self.children):
            if child[0] == name:
                return index
        return -1

    def get_child_at_index(self, index):
        if index < 0 or index >= len(self.children):
            return None
        child = self.children[index]
        if child[1] == "int":
            return make_int_value(self.valobj, child[0], child[2])
        if child[1] == "element":
            return make_element_value(self.valobj, child[0], child[2], child[3], child[4])
        return None

class ManagedStringSyntheticProvider:
    def __init__(self, valobj, internal_dict):
        self.valobj = valobj
        self.target = valobj.GetTarget()
        self.string_type = valobj.GetType()
        self.string_host = valobj.GetLoadAddress()
        self.children = []
        self.build()

    def build(self):
        if not self.string_host or self.string_host == lldb.LLDB_INVALID_ADDRESS:
            return
        length_offset = get_type_field_offset(self.string_type, "_stringLength", 4)
        char_offset = get_type_field_offset(self.string_type, "_firstChar", 8)
        process = self.target.GetProcess()
        length = read_int(process, self.string_host + length_offset)
        text = read_utf16_string(process, self.string_host + char_offset, length)
        self.children.append(("Value", "string", text))
        self.children.append(("Length", "int", length))

    def update(self):
        self.string_host = self.valobj.GetLoadAddress()
        self.children = []
        self.build()
        return False

    def num_children(self):
        return len(self.children)

    def has_children(self):
        return len(self.children) > 0

    def get_child_index(self, name):
        for index, child in enumerate(self.children):
            if child[0] == name:
                return index
        return -1

    def get_child_at_index(self, index):
        if index < 0 or index >= len(self.children):
            return None
        child = self.children[index]
        if child[1] == "string":
            return make_string_value(self.valobj, child[0], child[2])
        if child[1] == "int":
            return make_int_value(self.valobj, child[0], child[2])
        return None

def ManagedStringSummaryProvider(valobj, internal_dict):
    string_type = valobj.GetType()
    string_host = valobj.GetLoadAddress()
    if not string_host or string_host == lldb.LLDB_INVALID_ADDRESS:
        return '""'
    length_offset = get_type_field_offset(string_type, "_stringLength", 4)
    char_offset = get_type_field_offset(string_type, "_firstChar", 8)
    process = valobj.GetTarget().GetProcess()
    length = read_int(process, string_host + length_offset)
    text = read_utf16_string(process, string_host + char_offset, length)
    return quote_string(text)

def friendly_type_name(name):
    if not name:
        return "?"
    return (name
        .replace("S_P_CoreLib_System_Collections_Generic_", "")
        .replace("Realm_Ecs_Realm_Ecs_", "")
        .replace("S_P_CoreLib_", ""))

def WebAssemblyRefWrapperSummaryProvider(valobj, internal_dict):
    target = valobj.GetTarget()
    guest_pointer = read_wrapper_guest_pointer(valobj)
    inner_type = get_pointer_pointee_type(target, valobj.GetType())
    if not inner_type or not inner_type.IsValid():
        return f"WebAssemblyRefWrapper<...> (guest 0x{guest_pointer:X})"

    inner_name = inner_type.GetName()
    host_pointer = guest_pointer_to_host(target, guest_pointer)
    if host_pointer == 0:
        return f"{friendly_type_name(inner_name)} (guest 0x{guest_pointer:X})"

    if inner_name == "System_String":
        length_offset = get_type_field_offset(inner_type, "_stringLength", 4)
        char_offset = get_type_field_offset(inner_type, "_firstChar", 8)
        process = target.GetProcess()
        length = read_int(process, host_pointer + length_offset)
        text = read_utf16_string(process, host_pointer + char_offset, length)
        return quote_string(text)

    if inner_name.startswith("__Array<"):
        length, _, _, _ = read_array_state(target, host_pointer, inner_type)
        return f"Array<{friendly_type_name(extract_array_element_type(inner_name))}> ({length} items, host 0x{host_pointer:X})"

    if "System_Collections_Generic_List_1<" in inner_name:
        count, capacity, _, _, _ = read_list_state(target, host_pointer, inner_type)
        return f"List<{friendly_type_name(extract_template_inner_type(inner_name))}> ({count}/{capacity} items, host 0x{host_pointer:X})"

    return f"{friendly_type_name(inner_name)} (host 0x{host_pointer:X})"

def ManagedListSummaryProvider(valobj, internal_dict):
    address = valobj.GetLoadAddress()
    return f"List (host 0x{address:X})" if address and address != lldb.LLDB_INVALID_ADDRESS else "List"

def ManagedArraySummaryProvider(valobj, internal_dict):
    address = valobj.GetLoadAddress()
    return f"Array (host 0x{address:X})" if address and address != lldb.LLDB_INVALID_ADDRESS else "Array"

class HostTypeSyntheticProvider:
    def __init__(self, valobj, internal_dict):
        self.valobj = valobj
        self.spec = find_host_spec(valobj.GetType().GetName())
        self.fields = self.spec.get("fields", []) if self.spec else []

    def update(self):
        return False

    def num_children(self):
        return len(self.fields)

    def has_children(self):
        return len(self.fields) > 0

    def get_child_index(self, name):
        for index, field in enumerate(self.fields):
            if field.get("name") == name:
                return index
        return -1

    def get_child_at_index(self, index):
        if index < 0 or index >= len(self.fields):
            return None
        field = self.fields[index]
        return make_string_value(self.valobj, field.get("name", "Value"), INSPECTION_REQUIRES_THREADS)

def HostTypeSummaryProvider(valobj, internal_dict):
    spec = find_host_spec(valobj.GetType().GetName())
    if not spec:
        return ""
    return f"{spec.get('label', spec.get('name', 'Host'))} (host boundary)"

def RealmHandleSummaryProvider(valobj, internal_dict):
    spec = find_handle_spec(valobj.GetType().GetName())
    if not spec:
        return ""
    value = extract_id_value(valobj)
    parts = []
    for field_name in spec.get("summary_fields", []):
        field = find_field_spec(spec, field_name)
        if not field:
            continue
        field_value = read_field_value(valobj, spec, field)
        if field_value is None:
            continue
        parts.append(f"{field_name}: {format_summary_value(field_value)}")
    label = spec.get("label", spec.get("name", "Handle"))
    if parts:
        return f"{label} {value} [{', '.join(parts)}]"
    return f"{label} {value}"

def realm_load_modules(debugger, command, result, internal_dict):
    import os, glob
    target = debugger.GetSelectedTarget()
    if not target or not target.IsValid():
        return

    search_dirs = [os.getcwd()]
    exe = target.GetExecutable()
    if exe and exe.IsValid():
        search_dirs.append(exe.GetDirectory())
    if "__file__" in globals():
        search_dirs.append(os.path.dirname(__file__))

    def load_elf(elf_path):
        norm = os.path.abspath(elf_path).replace("\\", "/")
        fs = lldb.SBFileSpec(norm)
        mod = target.FindModule(fs)
        if not mod or not mod.IsValid():
            mod = target.AddModule(norm, "x86_64", "")
        if mod and mod.IsValid():
            debugger.HandleCommand(f'target modules load --file "{norm}" --slide 0')
            return True
        return False

    loaded = 0
    seen = set()
    for d in search_dirs:
        if not d or not os.path.exists(d):
            continue
        for elf_path in glob.glob(os.path.join(d, "*.jit.elf")):
            norm = os.path.abspath(elf_path).replace("\\", "/")
            if norm in seen:
                continue
            seen.add(norm)
            if load_elf(norm):
                loaded += 1

    if loaded == 0:
        default_elf = os.path.join(os.getcwd(), "MapScript.jit.elf").replace("\\", "/")
        if os.path.exists(default_elf):
            load_elf(default_elf)

def __lldb_init_module(debugger, internal_dict):
    category = "Realm"

    for type_name in HANDLE_TYPES:
        regex = f"(^|.*[.:_ ]){type_name}(\\s*[*&])?$"
        debugger.HandleCommand(f"type synthetic add -l realm_lldb.RealmHandleSyntheticProvider -x \"{regex}\" -w {category}")
        debugger.HandleCommand(f"type summary add -F realm_lldb.RealmHandleSummaryProvider -x \"{regex}\" -w {category}")

    for type_name in HOST_TYPES:
        regex = f"(^|.*[.:_ ]){type_name}(\\s*[*&])?$"
        debugger.HandleCommand(f"type synthetic add -l realm_lldb.HostTypeSyntheticProvider -x \"{regex}\" -w {category}")
        debugger.HandleCommand(f"type summary add -F realm_lldb.HostTypeSummaryProvider -x \"{regex}\" -w {category}")

    debugger.HandleCommand(f"type synthetic add -l realm_lldb.WebAssemblyRefWrapperSyntheticProvider -x \"(^|.*[.:_ ])WebAssembly(Ref|Ptr)Wrapper\" -w {category}")
    debugger.HandleCommand(f"type summary add -F realm_lldb.WebAssemblyRefWrapperSummaryProvider -x \"(^|.*[.:_ ])WebAssembly(Ref|Ptr)Wrapper\" -w {category}")
    debugger.HandleCommand(f"type synthetic add -l realm_lldb.ManagedListSyntheticProvider -x \"(^|.*[.:_ ])S_P_CoreLib_System_Collections_Generic_List_1\" -w {category}")
    debugger.HandleCommand(f"type summary add -F realm_lldb.ManagedListSummaryProvider -x \"(^|.*[.:_ ])S_P_CoreLib_System_Collections_Generic_List_1\" -w {category}")
    debugger.HandleCommand(f"type synthetic add -l realm_lldb.ManagedArraySyntheticProvider -x \"(^|.*[.:_ ])__Array\" -w {category}")
    debugger.HandleCommand(f"type summary add -F realm_lldb.ManagedArraySummaryProvider -x \"(^|.*[.:_ ])__Array\" -w {category}")
    debugger.HandleCommand(f"type synthetic add -l realm_lldb.ManagedStringSyntheticProvider -x \"(^|.*[.:_ ])System_String$\" -w {category}")
    debugger.HandleCommand(f"type summary add -F realm_lldb.ManagedStringSummaryProvider -x \"(^|.*[.:_ ])System_String$\" -w {category}")

    debugger.HandleCommand(f"type summary add -s \"(${{var.X}}, ${{var.Y}}, ${{var.Z}})\" -x \"(^|.*[.:_ ])Vector3$\" -w {category}")

    debugger.HandleCommand("command script add -o -f realm_lldb.realm_attach realm_attach")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_load_modules realm_load_modules")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_version realm_version")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_verify realm_verify")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_doctor realm_doctor")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_inspect realm_inspect")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_watch realm_watch")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_plot realm_plot")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_export realm_export")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_step realm_step")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_break realm_break")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_recover realm_recover")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_reload realm_reload")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_where realm_where")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_exception realm_exception")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_just_my_code realm_just_my_code")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_just_my_code_tick realm_just_my_code_tick")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_step_tick realm_step_tick")
    debugger.HandleCommand("command script add -o -f realm_lldb.realm_watch_tick realm_watch_tick")
    debugger.HandleCommand(f"type category enable {category}")
