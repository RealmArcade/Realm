import lldb
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
UNIT_CHARGES_OFFSET = 1653248
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
    "UnitCharges": 1653248,
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
    "UnitCharges": 16,
    "UnitOwnerPlayer": 4,
}

# Component identifiers auto-discovered from ComponentIds
COMPONENT_IDS = {
    "AnimationsFrozen": 127,
    "Attack": 45,
    "Building": 94,
    "CommandsFrozen": 125,
    "Dead": 97,
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
    "PlayerDamageEffectiveness": 134,
    "PlayerPopulation": 26,
    "PlayerResources": 71,
    "Position": 0,
    "PreviousPosition": 1,
    "PreviousRotationY": 115,
    "Resistances": 50,
    "RotationY": 114,
    "ScriptPlayer": 33,
    "Unit": 105,
    "UnitCharges": 133,
    "UnitOwnerPlayer": 39,
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
            { "name": "Charges", "kind": "component", "type": "int", "component": "UnitCharges", "component_id": 133, "requires_alive": True, "default": 0 },
            { "name": "MaximumCharges", "kind": "component", "type": "int", "component": "UnitCharges", "component_id": 133, "sub_offset": 4, "requires_alive": True, "default": 0 },
            { "name": "ChargeRechargeSeconds", "kind": "component", "type": "float", "component": "UnitCharges", "component_id": 133, "sub_offset": 8, "requires_alive": True, "default": 0 },
            { "name": "ChargeRechargeRemainingSeconds", "kind": "host", "type": "string" },
            { "name": "Scale", "kind": "component", "type": "float", "component": "ModelScale", "component_id": 110, "requires_alive": True, "default": 1 },
            { "name": "Invulnerable", "kind": "tag", "type": "bool", "component": "Invulnerable", "component_id": 99, "requires_alive": True, "tags": ["Invulnerable"], "tag_ids": [99], "default": False },
            { "name": "AnimationsFrozen", "kind": "tag", "type": "bool", "component": "AnimationsFrozen", "component_id": 127, "requires_alive": True, "tags": ["AnimationsFrozen"], "tag_ids": [127], "default": False },
            { "name": "CommandsFrozen", "kind": "tag", "type": "bool", "component": "CommandsFrozen", "component_id": 125, "requires_alive": True, "tags": ["CommandsFrozen"], "tag_ids": [125], "default": False },
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
REALM_LLDB_SCRIPT_HASH = "f683cd830eee2a946494ad66b004576a1e69bd91fef15b6693299a1f07a1a26b"
REALM_LLDB_SOURCE_COMMIT = "0181bda7930e0b2d1f17b3c8640b8a3221a909a0"

INSPECTION_REQUIRES_THREADS = "Inspection requires threads to execute. Copy value to local variable to inspect"

_CACHED_BASE_ADDR = 0

def get_shared_memory_base(target):
    global _CACHED_BASE_ADDR
    if _CACHED_BASE_ADDR != 0:
        err = lldb.SBError()
        target.GetProcess().ReadMemory(_CACHED_BASE_ADDR, 4, err)
        if err.Success():
            return _CACHED_BASE_ADDR
        _CACHED_BASE_ADDR = 0

    import os, json
    search_dirs = [os.getcwd()]
    if target and target.IsValid():
        exe = target.GetExecutable()
        if exe and exe.IsValid():
            search_dirs.append(exe.GetDirectory())
    if "__file__" in globals():
        search_dirs.append(os.path.dirname(__file__))

    for d in search_dirs:
        if not d:
            continue
        debug_json = os.path.join(d, "MapScript.debug.json")
        if os.path.exists(debug_json):
            try:
                with open(debug_json, "r") as f:
                    data = json.load(f)
                if data.get("pid") != target.GetProcess().GetProcessID():
                    continue
                ptr = data.get("sharedMemoryPointer", 0)
                if ptr != 0:
                    err = lldb.SBError()
                    target.GetProcess().ReadMemory(ptr, 4, err)
                    if err.Success():
                        _CACHED_BASE_ADDR = ptr
                        return ptr
            except Exception:
                pass

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

def realm_attach(debugger, command, result, internal_dict):
    import os
    pid = None
    search_dirs = [os.getcwd()]
    target = debugger.GetSelectedTarget()
    if target and target.IsValid():
        exe = target.GetExecutable()
        if exe and exe.IsValid():
            search_dirs.append(exe.GetDirectory())
    if "__file__" in globals():
        search_dirs.append(os.path.dirname(__file__))

    for d in search_dirs:
        if not d:
            continue
        pid_file = os.path.join(d, "authority.pid")
        if os.path.exists(pid_file):
            try:
                with open(pid_file, "r") as f:
                    val = f.read().strip()
                if val and val.isdigit():
                    pid = val
                    break
            except Exception:
                pass

    if pid:
        debugger.HandleCommand(f"process attach --pid {pid}")
    else:
        result.SetError("authority.pid was not found in the workspace.")

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
            target.SetModuleLoadAddress(mod, 0)
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

    debugger.HandleCommand(f"type summary add -s \"(${{var.X}}, ${{var.Y}}, ${{var.Z}})\" -x \"(^|.*[.:_ ])Vector3$\" -w {category}")

    debugger.HandleCommand("command script add -f realm_lldb.realm_attach realm_attach")
    debugger.HandleCommand("command script add -f realm_lldb.realm_load_modules realm_load_modules")
    debugger.HandleCommand("command script add -f realm_lldb.realm_version realm_version")
    debugger.HandleCommand("command script add -f realm_lldb.realm_verify realm_verify")
    debugger.HandleCommand("command script add -f realm_lldb.realm_doctor realm_doctor")
    debugger.HandleCommand(f"type category enable {category}")
