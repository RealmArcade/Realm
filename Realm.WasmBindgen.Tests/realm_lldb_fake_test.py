import importlib.util
import os
import struct
import sys
import types

BASE = 0x100000
MEMORY_SIZE = 4 * 1024 * 1024
ENTITY_ID = 1
PLAYER_ID = 0


class FakeError:
    def __init__(self):
        self.ok = True

    def Success(self):
        return self.ok


class FakeType:
    def __init__(self, name):
        self.name = name

    def IsValid(self):
        return True

    def GetName(self):
        return self.name

    def IsReferenceType(self):
        return False

    def IsPointerType(self):
        return False

    def GetArrayType(self, count):
        return FakeType(f"{self.name}[{count}]")


class FakeProcess:
    def __init__(self, memory):
        self.memory = memory
        self.thread = None

    def GetSelectedThread(self):
        return self.thread

    def ReadMemory(self, address, size, error):
        if address < 0 or address + size > len(self.memory):
            error.ok = False
            return b""
        return bytes(self.memory[address:address + size])

    def GetProcessID(self):
        return 4242

    def ReadPointerFromAddress(self, address, error):
        if address < 0 or address + 8 > len(self.memory):
            error.ok = False
            return 0
        return struct.unpack_from("<Q", self.memory, address)[0]


class FakeTarget:
    def __init__(self, process):
        self.process = process

    def IsValid(self):
        return True

    def GetProcess(self):
        return self.process

    def FindFirstType(self, name):
        return FakeType(name)

    def GetBasicType(self, kind):
        return FakeType(str(kind))

    def FindFirstGlobalVariable(self, name):
        return FakeValue(self, name, "int", 0)

    def FindSymbols(self, name):
        return FakeSymbols()

    def EvaluateExpression(self, expression):
        return FakeValue(self, expression, "int", 0)

    def GetExecutable(self):
        return None


class FakeSymbols:
    def IsValid(self):
        return False

    def GetSize(self):
        return 0


class FakeData:
    def __init__(self, value):
        self.value = value


class FakeValue:
    def __init__(self, target, name, type_name, value=None, address=None, valid=True):
        self.target = target
        self.name = name
        self.type_name = type_name
        self.value = value
        self.address = address
        self.valid = valid

    def IsValid(self):
        return self.valid

    def GetTarget(self):
        return self.target

    def GetType(self):
        return FakeType(self.type_name)

    def GetChildMemberWithName(self, name):
        if name in ("Value", "value", "<Value>k__BackingField", "_value"):
            return FakeValue(self.target, name, "int", self.value)
        return FakeValue(self.target, name, "int", None, valid=False)

    def GetValueAsSigned(self, default=0):
        if self.value is not None:
            return self.value
        if self.address is not None and self.target is not None:
            process = self.target.GetProcess()
            if self.type_name in ("float", "System.Single"):
                return int(struct.unpack_from("<f", process.memory, self.address)[0])
            if self.type_name in ("bool", "System.Boolean"):
                return 1 if process.memory[self.address] else 0
            return struct.unpack_from("<i", process.memory, self.address)[0]
        return default

    def GetValueAsDouble(self):
        if self.value is not None:
            return float(self.value)
        if self.address is not None and self.target is not None:
            process = self.target.GetProcess()
            if self.type_name in ("float", "System.Single"):
                return struct.unpack_from("<f", process.memory, self.address)[0]
            return float(struct.unpack_from("<i", process.memory, self.address)[0])
        return 0.0

    def GetNonSyntheticValue(self):
        return self

    def GetData(self):
        return None

    def CreateValueFromData(self, name, data, value_type):
        return FakeValue(self.target, name, value_type.GetName(), data)

    def CreateValueFromAddress(self, name, address, value_type):
        return FakeValue(self.target, name, value_type.GetName(), address=address)

    def Dereference(self):
        return self


class FakeResult:
    def __init__(self):
        self.messages = []

    def AppendMessage(self, message):
        self.messages.append(message)

    def SetError(self, message):
        self.messages.append("ERROR: " + message)


class FakeDebugger:
    def __init__(self, target):
        self.target = target
        self.commands = []

    def GetSelectedTarget(self):
        return self.target

    def HandleCommand(self, command):
        self.commands.append(command)


class FakeFileSpec:
    def __init__(self, path):
        self.fullpath = path

    def IsValid(self):
        return bool(self.fullpath)


class FakeLineEntry:
    def __init__(self, path, line):
        self.path = path
        self.line = line

    def IsValid(self):
        return bool(self.path)

    def GetFileSpec(self):
        return FakeFileSpec(self.path)

    def GetLine(self):
        return self.line


class FakeFrame:
    def __init__(self, values, function_name="", path="", line=0):
        self.values = values
        self.function_name = function_name
        self.path = path
        self.line = line

    def IsValid(self):
        return True

    def EvaluateExpression(self, expression):
        return self.values.get(expression, FakeValue(None, expression, "int", 0, valid=False))

    def GetFunctionName(self):
        return self.function_name

    def GetLineEntry(self):
        return FakeLineEntry(self.path, self.line)

    def GetValue(self):
        return "value"

    def GetSummary(self):
        return "summary"


class FakeThread:
    def __init__(self, frame):
        self.frame = frame
        self.frames = [frame]
        self.stepped = False

    def IsValid(self):
        return True

    def GetSelectedFrame(self):
        return self.frame

    def GetNumFrames(self):
        return len(self.frames)

    def GetFrameAtIndex(self, index):
        return self.frames[index]

    def StepOver(self):
        self.stepped = True


class FakeSBData:
    @staticmethod
    def CreateDataFromSInt32Array(byte_order, address_size, values):
        return list(values)

    @staticmethod
    def CreateDataFromUInt64Array(byte_order, address_size, values):
        return list(values)

    @staticmethod
    def CreateDataFromUInt32Array(byte_order, address_size, values):
        return list(values)

    @staticmethod
    def CreateDataFromCString(byte_order, address_size, text):
        return text


def install_fake_lldb():
    lldb = types.ModuleType("lldb")
    lldb.SBError = FakeError
    lldb.SBData = FakeSBData
    lldb.eByteOrderLittle = 1
    lldb.eBasicTypeInt = 1
    lldb.eBasicTypeFloat = 2
    lldb.eBasicTypeBool = 3
    lldb.eBasicTypeChar = 4
    lldb.LLDB_INVALID_ADDRESS = 0
    sys.modules["lldb"] = lldb


def write_int(memory, address, value):
    struct.pack_into("<i", memory, address, value)


def write_float(memory, address, value):
    struct.pack_into("<f", memory, address, value)


def write_byte(memory, address, value):
    memory[address] = value


def set_component_table(memory, module, component_name):
    component_id = module.COMPONENT_IDS[component_name]
    offset = module.COMPONENT_OFFSETS[component_name]
    size = module.COMPONENT_SIZES[component_name]
    write_int(memory, BASE + component_id * 4, BASE + offset)
    write_int(memory, BASE + module.COMPONENT_STRIDES_OFFSET + component_id * 4, size)


def set_tag(memory, module, entity_id, component_name):
    component_id = module.COMPONENT_IDS[component_name]
    word = component_id >> 6
    bit = component_id & 63
    address = BASE + module.ENTITY_MASKS_OFFSET + entity_id * 32 + word * 8
    current = struct.unpack_from("<Q", memory, address)[0]
    struct.pack_into("<Q", memory, address, current | (1 << bit))


def field(module, handle_name, field_name):
    for entry in module.HANDLE_TYPES[handle_name]["fields"]:
        if entry["name"] == field_name:
            return entry
    raise AssertionError(f"{handle_name}.{field_name} was not generated")


def read(module, handle_name, value, field_name):
    target = FakeTarget(FakeProcess(MEMORY))
    valobj = FakeValue(target, handle_name, handle_name, value)
    return module.read_field_value(valobj, module.HANDLE_TYPES[handle_name], field(module, handle_name, field_name))


def expect(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    script_path = sys.argv[1]
    install_fake_lldb()

    spec = importlib.util.spec_from_file_location("realm_lldb", script_path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)

    global MEMORY
    MEMORY = bytearray(BASE + MEMORY_SIZE)
    module._CACHED_BASE_ADDR = BASE

    for component_name in module.COMPONENT_OFFSETS:
        set_component_table(MEMORY, module, component_name)

    write_byte(MEMORY, BASE + module.ALIVE_OFFSET + ENTITY_ID, 1)

    health_offset = BASE + module.COMPONENT_OFFSETS["Health"] + ENTITY_ID * module.COMPONENT_SIZES["Health"]
    write_float(MEMORY, health_offset + 0, 100.0)
    write_float(MEMORY, health_offset + 4, 150.0)

    charges_offset = BASE + module.COMPONENT_OFFSETS["UnitCharges"] + ENTITY_ID * module.COMPONENT_SIZES["UnitCharges"]
    write_int(MEMORY, charges_offset + 0, 3)
    write_int(MEMORY, charges_offset + 4, 5)
    write_float(MEMORY, charges_offset + 8, 2.5)
    write_float(MEMORY, charges_offset + 12, 1.0)

    script_player_offset = BASE + module.COMPONENT_OFFSETS["ScriptPlayer"] + PLAYER_ID * module.COMPONENT_SIZES["ScriptPlayer"]
    write_byte(MEMORY, script_player_offset + 12, 1)
    write_byte(MEMORY, script_player_offset + 13, 0)
    write_int(MEMORY, script_player_offset + 16, 2)
    write_int(MEMORY, script_player_offset + 24, 7)

    resources_offset = BASE + module.COMPONENT_OFFSETS["PlayerResources"] + (PLAYER_ID + 1) * module.COMPONENT_SIZES["PlayerResources"]
    write_int(MEMORY, resources_offset + 0, 500)
    write_int(MEMORY, resources_offset + 4, 250)
    write_int(MEMORY, resources_offset + 8, 100)
    write_int(MEMORY, resources_offset + 12, 50)

    population_offset = BASE + module.COMPONENT_OFFSETS["PlayerPopulation"] + (PLAYER_ID + 1) * module.COMPONENT_SIZES["PlayerPopulation"]
    write_int(MEMORY, population_offset + 0, 4)
    write_int(MEMORY, population_offset + 4, 10)

    for tag in ("Building", "Hero", "Invulnerable", "CommandsFrozen", "AnimationsFrozen", "OverheadBarsHidden"):
        set_tag(MEMORY, module, ENTITY_ID, tag)

    expect(read(module, "UnitId", ENTITY_ID, "Health") == 100.0, "UnitId.Health did not read shared memory")
    expect(read(module, "UnitId", ENTITY_ID, "MaxHealth") == 150.0, "UnitId.MaxHealth did not read shared memory")
    expect(read(module, "UnitId", ENTITY_ID, "Charges") == 3, "UnitId.Charges was not generated or read")
    expect(read(module, "UnitId", ENTITY_ID, "MaximumCharges") == 5, "UnitId.MaximumCharges was not generated or read")
    expect(read(module, "UnitId", ENTITY_ID, "IsBuilding") is True, "UnitId.IsBuilding tag was not read")
    expect(read(module, "UnitId", ENTITY_ID, "IsHero") is True, "UnitId.IsHero tag was not read")
    expect(read(module, "UnitId", ENTITY_ID, "Invulnerable") is True, "UnitId.Invulnerable tag was not read")
    expect(read(module, "UnitId", ENTITY_ID, "Frozen") is True, "UnitId.Frozen tag was not read")
    expect(read(module, "UnitId", ENTITY_ID, "IsDead") is False, "UnitId.IsDead was not read")
    expect(read(module, "UnitId", ENTITY_ID, "Name") == module.INSPECTION_REQUIRES_THREADS, "UnitId.Name did not report the host boundary")

    expect(read(module, "PlayerId", PLAYER_ID, "Gold") == 500, "PlayerId.Gold did not read shared memory")
    expect(read(module, "PlayerId", PLAYER_ID, "Wood") == 250, "PlayerId.Wood did not read shared memory")
    expect(read(module, "PlayerId", PLAYER_ID, "Kills") == 7, "PlayerId.Kills was not generated or read")
    expect(read(module, "PlayerId", PLAYER_ID, "CurrentPopulation") == 4, "PlayerId.CurrentPopulation was not generated or read")
    expect(read(module, "PlayerId", PLAYER_ID, "MaxPopulation") == 10, "PlayerId.MaxPopulation was not generated or read")
    expect(read(module, "PlayerId", PLAYER_ID, "IsActive") is True, "PlayerId.IsActive was not read")
    expect(read(module, "PlayerId", PLAYER_ID, "IsComputer") is False, "PlayerId.IsComputer was not read")
    expect(read(module, "PlayerId", PLAYER_ID, "Team") == 2, "PlayerId.Team was not read")
    expect(read(module, "PlayerId", PLAYER_ID, "Name") == module.INSPECTION_REQUIRES_THREADS, "PlayerId.Name did not report the host boundary")
    expect(read(module, "PlayerId", -1, "Gold") == 0, "an invalid PlayerId did not fall back to the default")
    expect(read(module, "PlayerId", 25, "Gold") == 0, "an out-of-range PlayerId did not fall back to the default")

    expect(read(module, "ResourceNodeId", 0, "ResourceAmount") == module.INSPECTION_REQUIRES_THREADS, "ResourceNodeId.ResourceAmount did not report the host boundary")
    expect(read(module, "LootItemId", 5, "Value") == 5, "LootItemId.Value was not read")
    expect(read(module, "LootItemId", 5, "IsValid") is True, "LootItemId.IsValid was not read")

    host_spec = module.HOST_TYPES["GameAPI_WasmModule"]
    host_field = next(entry for entry in host_spec["fields"] if entry["name"] == "PlayerCount")
    host_target = FakeTarget(FakeProcess(MEMORY))
    host_valobj = FakeValue(host_target, "GameAPI_WasmModule", "GameAPI_WasmModule", 0)
    expect(module.read_field_value(host_valobj, host_spec, host_field) == module.INSPECTION_REQUIRES_THREADS, "GameAPI_WasmModule.PlayerCount did not report the host boundary")

    verify_target = FakeTarget(FakeProcess(MEMORY))
    drift = module.verify_component_tables(verify_target)
    expect(drift == [], f"component table verification reported drift: {drift}")

    version_result = FakeResult()
    module.realm_version(FakeDebugger(verify_target), "", version_result, {})
    version_output = chr(10).join(version_result.messages)
    expect(module.REALM_LLDB_SCRIPT_HASH in version_output, "realm_version did not print the script hash")
    expect(module.REALM_LLDB_SOURCE_COMMIT in version_output, "realm_version did not print the source commit")

    doctor_result = FakeResult()
    module.realm_doctor(FakeDebugger(verify_target), "", doctor_result, {})
    doctor_output = chr(10).join(doctor_result.messages)
    expect("shared memory base" in doctor_output, "realm_doctor did not report the shared memory base")
    expect("component tables: 0 drift(s)" in doctor_output, "realm_doctor did not report clean component tables")

    inspect_process = FakeProcess(MEMORY)
    inspect_target = FakeTarget(inspect_process)
    unit_value = FakeValue(inspect_target, "unit", "UnitId", ENTITY_ID)
    api_value = FakeValue(inspect_target, "api", "GameAPI_WasmModule", 0)
    inspect_frame = FakeFrame({"unit": unit_value, "api": api_value})
    inspect_process.thread = FakeThread(inspect_frame)
    inspect_debugger = FakeDebugger(inspect_target)

    inspect_result = FakeResult()
    module.realm_inspect(inspect_debugger, "unit", inspect_result, {})
    inspect_output = chr(10).join(inspect_result.messages)
    expect("Health = 100.00" in inspect_output, "realm_inspect did not print UnitId.Health")
    expect("Charges = 3" in inspect_output, "realm_inspect did not print UnitId.Charges")

    host_inspect_result = FakeResult()
    module.realm_inspect(inspect_debugger, "api", host_inspect_result, {})
    host_inspect_output = chr(10).join(host_inspect_result.messages)
    expect("PlayerCount = " + module.INSPECTION_REQUIRES_THREADS in host_inspect_output, "realm_inspect did not report the host boundary")

    watch_result = FakeResult()
    module.realm_watch(inspect_debugger, "unit Health", watch_result, {})
    expect(len(module._WATCHES) == 1, "realm_watch did not add a watch")
    for _ in range(3):
        module.record_watch_samples(inspect_frame)

    plot_result = FakeResult()
    module.realm_plot(inspect_debugger, "", plot_result, {})
    plot_output = chr(10).join(plot_result.messages)
    expect("n=3" in plot_output, "realm_plot did not report the sample count")

    import tempfile
    export_path = os.path.join(tempfile.gettempdir(), "realm_watch_export_test.csv")
    export_result = FakeResult()
    module.realm_export(inspect_debugger, export_path, export_result, {})
    expect(os.path.exists(export_path), "realm_export did not write the CSV file")
    with open(export_path, "r") as export_file:
        export_content = export_file.read()
    expect("watch,index,value" in export_content, "realm_export did not write the CSV header")
    os.remove(export_path)

    user_frame = FakeFrame({}, function_name="Realm.Maps.MapScript.Update", path="D:/maps/MapScript.cs", line=42)
    host_frame = FakeFrame({}, function_name="Realm.MapAPI.GameAPI_WasmModule.get_PlayerCount", path="D:/realm/Realm.MapAPI/GameAPI.cs", line=10)
    expect(module.is_user_frame(user_frame) is True, "a map-script frame was not recognized as user code")
    expect(module.is_user_frame(host_frame) is False, "a host frame was recognized as user code")

    where_thread = FakeThread(user_frame)
    where_thread.frames = [host_frame, user_frame]
    where_process = FakeProcess(MEMORY)
    where_process.thread = where_thread
    where_target = FakeTarget(where_process)
    where_result = FakeResult()
    module.realm_where(FakeDebugger(where_target), "", where_result, {})
    where_output = chr(10).join(where_result.messages)
    expect("MapScript.cs:42" in where_output, "realm_where did not find the user frame")

    exception_debugger = FakeDebugger(where_target)
    exception_result = FakeResult()
    module.realm_exception(exception_debugger, "on", exception_result, {})
    expect(any("process handle SIGTRAP" in command for command in exception_debugger.commands), "realm_exception did not change SIGTRAP handling")

    attach_debugger = FakeDebugger(where_target)
    attach_result = FakeResult()
    module.realm_attach(attach_debugger, "1234", attach_result, {})
    expect(any("process attach --pid 1234" in command for command in attach_debugger.commands), "realm_attach did not attach to the explicit pid")

    step_thread = FakeThread(user_frame)
    step_process = FakeProcess(MEMORY)
    step_process.thread = step_thread
    step_target = FakeTarget(step_process)
    step_result = FakeResult()
    module.realm_step(FakeDebugger(step_target), "", step_result, {})
    expect(step_thread.stepped, "realm_step did not issue a step-over")

    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
