import lldb
import struct

# {{OFFSETS}}

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
        "Realm_MapAPI_RealmSharedMemory_g_realmSharedMemoryBase",
        "Realm.MapAPI.RealmSharedMemory.g_realmSharedMemoryBase",
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

def extract_id_value(valobj):
    for candidate in ["Value", "value", "<Value>k__BackingField", "_value"]:
        child = valobj.GetChildMemberWithName(candidate)
        if child.IsValid():
            return child.GetValueAsSigned(-1)
    return valobj.GetValueAsSigned(-1)

def read_unpacked(buffer, offsets_dict, field_name, default_value, sub_offset=0):
    if field_name not in offsets_dict:
        return default_value
    offset, type_name = offsets_dict[field_name]
    target_offset = offset + sub_offset
    try:
        if type_name == "float" or (type_name == "Vector3" and sub_offset in (0, 4, 8)):
            if target_offset + 4 <= len(buffer):
                return struct.unpack_from("<f", buffer, target_offset)[0]
        elif type_name == "int":
            if target_offset + 4 <= len(buffer):
                return struct.unpack_from("<i", buffer, target_offset)[0]
        elif type_name == "bool":
            if target_offset + 1 <= len(buffer):
                return struct.unpack_from("<B", buffer, target_offset)[0] != 0
    except:
        pass
    return default_value

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
        for idx, (f_name, _, _) in enumerate(PLAYER_FIELDS):
            if f_name == name:
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

        f_name, f_offset, f_type_name = PLAYER_FIELDS[index]
        slot_addr = base_addr + PLAYER_TABLE_OFFSET + (player_id * PLAYER_SLOT_SIZE) + f_offset
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
        for idx, (f_name, _, _) in enumerate(ENTITY_FIELDS):
            if f_name == name:
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

        f_name, f_offset, f_type_name = ENTITY_FIELDS[index]
        slot_addr = base_addr + ENTITY_TABLE_OFFSET + (unit_id * ENTITY_SLOT_SIZE) + f_offset
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

    slot_base = base_addr + PLAYER_TABLE_OFFSET + (player_id * PLAYER_SLOT_SIZE)
    error = lldb.SBError()
    process = target.GetProcess()

    slot_bytes = process.ReadMemory(slot_base, PLAYER_SLOT_SIZE, error)
    if not error.Success() or len(slot_bytes) < PLAYER_SLOT_SIZE:
        return f"Player {player_id}"

    gold = read_unpacked(slot_bytes, PLAYER_OFFSETS, "Gold", 0.0)
    wood = read_unpacked(slot_bytes, PLAYER_OFFSETS, "Wood", 0.0)
    pop = read_unpacked(slot_bytes, PLAYER_OFFSETS, "CurrentPopulation", 0)
    max_pop = read_unpacked(slot_bytes, PLAYER_OFFSETS, "MaxPopulation", 0)

    return f"Player {player_id} [Gold: {int(gold)}, Wood: {int(wood)}, Pop: {pop}/{max_pop}]"

def UnitIdSummaryProvider(valobj, internal_dict):
    unit_id = extract_id_value(valobj)
    if unit_id < 0 or unit_id >= MAX_ENTITIES:
        return f"Unit {unit_id} [Invalid]"

    target = valobj.GetTarget()
    base_addr = get_shared_memory_base(target)
    if base_addr == 0:
        return f"Unit {unit_id}"

    slot_base = base_addr + ENTITY_TABLE_OFFSET + (unit_id * ENTITY_SLOT_SIZE)
    error = lldb.SBError()
    process = target.GetProcess()

    slot_bytes = process.ReadMemory(slot_base, ENTITY_SLOT_SIZE, error)
    if not error.Success() or len(slot_bytes) < ENTITY_SLOT_SIZE:
        return f"Unit {unit_id}"

    hp = read_unpacked(slot_bytes, ENTITY_OFFSETS, "Health", 0.0)
    max_hp = read_unpacked(slot_bytes, ENTITY_OFFSETS, "MaxHealth", 0.0)
    pos_x = read_unpacked(slot_bytes, ENTITY_OFFSETS, "Position", 0.0, sub_offset=0)
    pos_z = read_unpacked(slot_bytes, ENTITY_OFFSETS, "Position", 0.0, sub_offset=8)

    return f"Unit {unit_id} [Health: {int(hp)}/{int(max_hp)}, Pos: ({pos_x:.1f}, {pos_z:.1f})]"

def __lldb_init_module(debugger, internal_dict):
    category = "Realm"

    debugger.HandleCommand(f"type synthetic add -l realm_lldb.PlayerIdSyntheticProvider -x \"^Realm[.:]MapAPI[.:]PlayerId$\" -w {category}")
    debugger.HandleCommand(f"type summary add -F realm_lldb.PlayerIdSummaryProvider -x \"^Realm[.:]MapAPI[.:]PlayerId$\" -w {category}")

    debugger.HandleCommand(f"type synthetic add -l realm_lldb.UnitIdSyntheticProvider -x \"^Realm[.:]MapAPI[.:]UnitId$\" -w {category}")
    debugger.HandleCommand(f"type summary add -F realm_lldb.UnitIdSummaryProvider -x \"^Realm[.:]MapAPI[.:]UnitId$\" -w {category}")

    debugger.HandleCommand(f"type category enable {category}")
