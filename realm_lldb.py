import lldb
import struct

# Constants auto-discovered from EcsSharedMemory
MAX_PLAYERS = 25
MAX_PLAYER_ENTITIES = 26
MAX_ENTITIES = 8192
BITSET_CHUNKS = 128
MAX_COMPONENT_TYPES = 256
COMPONENT_ARRAY_BASE_OFFSETS_OFFSET = 0
COMPONENT_STRIDES_OFFSET = 1024
RANDOM_SEED_OFFSET = 2044
HEADER_SIZE = 2048
ACTIVE_ENTITY_BITSET_OFFSET = 2048
ALIVE_OFFSET = 3072
ENTITY_MASKS_OFFSET = 11264
COMPONENTS_START_OFFSET = 273408
POSITION_OFFSET = 273408
HEALTH_OFFSET = 371712
MANA_OFFSET = 535552
ATTACK_OFFSET = 633856
ARMOR_OFFSET = 1223680
MOVEMENT_STATS_OFFSET = 1321984
UNIT_OWNER_PLAYER_OFFSET = 1485824
LEVEL_OFFSET = 1518592
EXPERIENCE_OFFSET = 1551360
MODEL_SCALE_OFFSET = 1584128
ROTATION_Y_OFFSET = 1616896
PLAYER_RESOURCES_OFFSET = 1649664
SCRIPT_PLAYER_OFFSET = 1650912
PLAYER_POPULATION_OFFSET = 1651612
TOTAL_SIZE_BYTES = 4194304

# Element sizes auto-calculated from struct layouts
POSITION_SIZE = 12
HEALTH_SIZE = 20
MANA_SIZE = 12
ATTACK_SIZE = 72
ARMOR_SIZE = 12
MOVEMENT_STATS_SIZE = 20
UNIT_OWNER_PLAYER_SIZE = 4
LEVEL_SIZE = 4
EXPERIENCE_SIZE = 4
MODEL_SCALE_SIZE = 4
ROTATION_Y_SIZE = 4
PLAYER_RESOURCES_SIZE = 48
SCRIPT_PLAYER_SIZE = 32
PLAYER_POPULATION_SIZE = 8

# Fields auto-discovered for PlayerId (name, comp_id, array_offset, elem_size, sub_offset, type_name)
PLAYER_FIELDS = [
    ("Gold", 68, PLAYER_RESOURCES_OFFSET, PLAYER_RESOURCES_SIZE, 0, "int"),
    ("Wood", 68, PLAYER_RESOURCES_OFFSET, PLAYER_RESOURCES_SIZE, 4, "int"),
    ("Stone", 68, PLAYER_RESOURCES_OFFSET, PLAYER_RESOURCES_SIZE, 8, "int"),
    ("Ore", 68, PLAYER_RESOURCES_OFFSET, PLAYER_RESOURCES_SIZE, 12, "int"),
    ("IsActive", 31, SCRIPT_PLAYER_OFFSET, SCRIPT_PLAYER_SIZE, 0, "bool"),
    ("IsComputer", 31, SCRIPT_PLAYER_OFFSET, SCRIPT_PLAYER_SIZE, 1, "bool"),
    ("Team", 31, SCRIPT_PLAYER_OFFSET, SCRIPT_PLAYER_SIZE, 4, "int"),
    ("CurrentPopulation", 24, PLAYER_POPULATION_OFFSET, PLAYER_POPULATION_SIZE, 0, "int"),
    ("MaxPopulation", 24, PLAYER_POPULATION_OFFSET, PLAYER_POPULATION_SIZE, 4, "int"),
]

# Fields auto-discovered for UnitId (name, comp_id, array_offset, elem_size, sub_offset, type_name)
ENTITY_FIELDS = [
    ("Health", 1, HEALTH_OFFSET, HEALTH_SIZE, 0, "float"),
    ("MaxHealth", 1, HEALTH_OFFSET, HEALTH_SIZE, 4, "float"),
    ("Mana", 3, MANA_OFFSET, MANA_SIZE, 0, "float"),
    ("MaxMana", 3, MANA_OFFSET, MANA_SIZE, 4, "float"),
    ("ManaRegen", 3, MANA_OFFSET, MANA_SIZE, 8, "float"),
    ("Damage", 43, ATTACK_OFFSET, ATTACK_SIZE, 0, "float"),
    ("Range", 43, ATTACK_OFFSET, ATTACK_SIZE, 4, "float"),
    ("AttackSpeed", 43, ATTACK_OFFSET, ATTACK_SIZE, 8, "float"),
    ("Armor", 48, ARMOR_OFFSET, ARMOR_SIZE, 0, "float"),
    ("Speed", 57, MOVEMENT_STATS_OFFSET, MOVEMENT_STATS_SIZE, 0, "float"),
    ("Position", 0, POSITION_OFFSET, POSITION_SIZE, 0, "Vector3"),
    ("Player", 37, UNIT_OWNER_PLAYER_OFFSET, UNIT_OWNER_PLAYER_SIZE, 0, "int"),
    ("Level", 105, LEVEL_OFFSET, LEVEL_SIZE, 0, "int"),
    ("Experience", 103, EXPERIENCE_OFFSET, EXPERIENCE_SIZE, 0, "float"),
    ("Scale", 107, MODEL_SCALE_OFFSET, MODEL_SCALE_SIZE, 0, "float"),
    ("RotationY", 111, ROTATION_Y_OFFSET, ROTATION_Y_SIZE, 0, "float"),
]

PLAYER_OFFSETS = {name: (comp_id, array_offset, elem_size, sub_offset, type_name) for name, comp_id, array_offset, elem_size, sub_offset, type_name in PLAYER_FIELDS}
ENTITY_OFFSETS = {name: (comp_id, array_offset, elem_size, sub_offset, type_name) for name, comp_id, array_offset, elem_size, sub_offset, type_name in ENTITY_FIELDS}

_CACHED_BASE_ADDR = 0

def get_shared_memory_base(target):
    global _CACHED_BASE_ADDR
    if _CACHED_BASE_ADDR != 0:
        err = lldb.SBError()
        target.GetProcess().ReadMemory(_CACHED_BASE_ADDR, 4, err)
        if err.Success():
            return _CACHED_BASE_ADDR
        _CACHED_BASE_ADDR = 0

    candidate_names = [
        "g_realmSharedMemoryBase",
        "Realm_MapAPI_EcsSharedMemory_g_realmSharedMemoryBase",
        "Realm.MapAPI.EcsSharedMemory.g_realmSharedMemoryBase",
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

def extract_id_value(valobj):
    for candidate in ["Value", "value", "<Value>k__BackingField", "_value"]:
        child = valobj.GetChildMemberWithName(candidate)
        if child.IsValid():
            return child.GetValueAsSigned(-1)
    return valobj.GetValueAsSigned(-1)

class PlayerIdSyntheticProvider:
    def __init__(self, valobj, internal_dict):
        self.valobj = valobj

    def update(self):
        return False

    def num_children(self):
        return len(PLAYER_FIELDS)

    def has_children(self):
        return len(PLAYER_FIELDS) > 0

    def get_child_index(self, name):
        for idx, entry in enumerate(PLAYER_FIELDS):
            if entry[0] == name:
                return idx
        return -1

    def get_child_at_index(self, index):
        if index < 0 or index >= len(PLAYER_FIELDS):
            return None
        target = self.valobj.GetTarget()
        base_addr = get_shared_memory_base(target)
        if base_addr == 0:
            return None

        player_id = extract_id_value(self.valobj)
        if player_id < 0 or player_id >= MAX_PLAYERS:
            return None

        process = target.GetProcess()
        entry = PLAYER_FIELDS[index]
        if len(entry) == 6:
            f_name, comp_id, array_offset, elem_size, sub_offset, f_type_name = entry
            arr_base = resolve_array_base(process, base_addr, comp_id, array_offset)
            stride = resolve_component_stride(process, base_addr, comp_id, elem_size)
        else:
            f_name, array_offset, elem_size, sub_offset, f_type_name = entry
            arr_base = base_addr + array_offset
            stride = elem_size

        slot_addr = arr_base + (player_id * stride) + sub_offset
        f_type = target.FindFirstType(f_type_name)
        if not f_type.IsValid():
            if f_type_name == "float":
                f_type = target.GetBasicType(lldb.eBasicTypeFloat)
            elif f_type_name == "bool":
                f_type = target.GetBasicType(lldb.eBasicTypeBool)
            else:
                f_type = target.GetBasicType(lldb.eBasicTypeInt)

        return self.valobj.CreateValueFromAddress(f_name, slot_addr, f_type)

class UnitIdSyntheticProvider:
    def __init__(self, valobj, internal_dict):
        self.valobj = valobj

    def update(self):
        return False

    def num_children(self):
        return len(ENTITY_FIELDS)

    def has_children(self):
        return len(ENTITY_FIELDS) > 0

    def get_child_index(self, name):
        for idx, entry in enumerate(ENTITY_FIELDS):
            if entry[0] == name:
                return idx
        return -1

    def get_child_at_index(self, index):
        if index < 0 or index >= len(ENTITY_FIELDS):
            return None
        target = self.valobj.GetTarget()
        base_addr = get_shared_memory_base(target)
        if base_addr == 0:
            return None

        unit_id = extract_id_value(self.valobj)
        if unit_id < 0 or unit_id >= MAX_ENTITIES:
            return None

        process = target.GetProcess()
        entry = ENTITY_FIELDS[index]
        if len(entry) == 6:
            f_name, comp_id, array_offset, elem_size, sub_offset, f_type_name = entry
            arr_base = resolve_array_base(process, base_addr, comp_id, array_offset)
            stride = resolve_component_stride(process, base_addr, comp_id, elem_size)
        else:
            f_name, array_offset, elem_size, sub_offset, f_type_name = entry
            arr_base = base_addr + array_offset
            stride = elem_size

        slot_addr = arr_base + (unit_id * stride) + sub_offset
        f_type = target.FindFirstType(f_type_name)
        if not f_type.IsValid() and f_type_name == "Vector3":
            f_type = target.FindFirstType("System.Numerics.Vector3")
        if not f_type.IsValid():
            if f_type_name == "float":
                f_type = target.GetBasicType(lldb.eBasicTypeFloat)
            elif f_type_name == "bool":
                f_type = target.GetBasicType(lldb.eBasicTypeBool)
            else:
                f_type = target.GetBasicType(lldb.eBasicTypeInt)

        return self.valobj.CreateValueFromAddress(f_name, slot_addr, f_type)

def PlayerIdSummaryProvider(valobj, internal_dict):
    player_id = extract_id_value(valobj)
    if player_id < 0 or player_id >= MAX_PLAYERS:
        return f"Player {player_id} [Invalid]"

    target = valobj.GetTarget()
    base_addr = get_shared_memory_base(target)
    if base_addr == 0:
        return f"Player {player_id}"

    error = lldb.SBError()
    process = target.GetProcess()

    res_base = resolve_array_base(process, base_addr, 68, PLAYER_RESOURCES_OFFSET)
    res_stride = resolve_component_stride(process, base_addr, 68, PLAYER_RESOURCES_SIZE)
    res_addr = res_base + (player_id * res_stride)
    res_bytes = process.ReadMemory(res_addr, 16, error)
    gold = 0
    wood = 0
    if error.Success() and len(res_bytes) >= 8:
        gold = struct.unpack_from("<i", res_bytes, 0)[0]
        wood = struct.unpack_from("<i", res_bytes, 4)[0]

    pop_base = resolve_array_base(process, base_addr, 24, PLAYER_POPULATION_OFFSET)
    pop_stride = resolve_component_stride(process, base_addr, 24, PLAYER_POPULATION_SIZE)
    pop_addr = pop_base + (player_id * pop_stride)
    pop_bytes = process.ReadMemory(pop_addr, 8, error)
    pop = 0
    max_pop = 0
    if error.Success() and len(pop_bytes) >= 8:
        pop = struct.unpack_from("<i", pop_bytes, 0)[0]
        max_pop = struct.unpack_from("<i", pop_bytes, 4)[0]

    return f"Player {player_id} [Gold: {gold}, Wood: {wood}, Pop: {pop}/{max_pop}]"

def UnitIdSummaryProvider(valobj, internal_dict):
    unit_id = extract_id_value(valobj)
    if unit_id < 0 or unit_id >= MAX_ENTITIES:
        return f"Unit {unit_id} [Invalid]"

    target = valobj.GetTarget()
    base_addr = get_shared_memory_base(target)
    if base_addr == 0:
        return f"Unit {unit_id}"

    error = lldb.SBError()
    process = target.GetProcess()

    hp_base = resolve_array_base(process, base_addr, 1, HEALTH_OFFSET)
    hp_stride = resolve_component_stride(process, base_addr, 1, HEALTH_SIZE)
    hp_addr = hp_base + (unit_id * hp_stride)
    hp_bytes = process.ReadMemory(hp_addr, 8, error)
    hp = 0.0
    max_hp = 0.0
    if error.Success() and len(hp_bytes) >= 8:
        hp = struct.unpack_from("<f", hp_bytes, 0)[0]
        max_hp = struct.unpack_from("<f", hp_bytes, 4)[0]

    pos_base = resolve_array_base(process, base_addr, 0, POSITION_OFFSET)
    pos_stride = resolve_component_stride(process, base_addr, 0, POSITION_SIZE)
    pos_addr = pos_base + (unit_id * pos_stride)
    pos_bytes = process.ReadMemory(pos_addr, 12, error)
    pos_x = 0.0
    pos_z = 0.0
    if error.Success() and len(pos_bytes) >= 12:
        pos_x = struct.unpack_from("<f", pos_bytes, 0)[0]
        pos_z = struct.unpack_from("<f", pos_bytes, 8)[0]

    return f"Unit {unit_id} [Health: {int(hp)}/{int(max_hp)}, Pos: ({pos_x:.1f}, {pos_z:.1f})]"

def __lldb_init_module(debugger, internal_dict):
    category = "Realm"

    debugger.HandleCommand(f"type synthetic add -l realm_lldb.PlayerIdSyntheticProvider -x \"^Realm[.:]MapAPI[.:]PlayerId$\" -w {category}")
    debugger.HandleCommand(f"type summary add -F realm_lldb.PlayerIdSummaryProvider -x \"^Realm[.:]MapAPI[.:]PlayerId$\" -w {category}")

    debugger.HandleCommand(f"type synthetic add -l realm_lldb.UnitIdSyntheticProvider -x \"^Realm[.:]MapAPI[.:]UnitId$\" -w {category}")
    debugger.HandleCommand(f"type summary add -F realm_lldb.UnitIdSummaryProvider -x \"^Realm[.:]MapAPI[.:]UnitId$\" -w {category}")

    debugger.HandleCommand(f"type category enable {category}")
