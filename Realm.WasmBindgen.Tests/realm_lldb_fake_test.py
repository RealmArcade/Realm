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


class FakeField:
    def __init__(self, name, field_type, offset):
        self.name = name
        self.field_type = field_type
        self.offset = offset

    def GetName(self):
        return self.name

    def GetType(self):
        return self.field_type

    def GetOffsetInBytes(self):
        return self.offset


class FakeType:
    def __init__(self, name, fields=None, byte_size=4, template_argument=None, base_count=0):
        self.name = name
        self.fields = fields or []
        self.byte_size = byte_size
        self.template_argument = template_argument
        self.base_count = base_count

    def IsValid(self):
        return True

    def GetName(self):
        return self.name

    def IsReferenceType(self):
        return False

    def IsPointerType(self):
        return False

    def GetArrayType(self, count):
        return FakeType(f"{self.name}[{count}]", byte_size=self.byte_size * count)

    def GetNumberOfFields(self):
        return len(self.fields)

    def GetFieldAtIndex(self, index):
        return self.fields[index]

    def GetByteSize(self):
        return self.byte_size

    def GetTemplateArgumentType(self, index):
        return self.template_argument if index == 0 else None

    def GetNumberOfDirectBaseClasses(self):
        return self.base_count


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
    def __init__(self, process, types=None):
        self.process = process
        self.types = types or {}

    def IsValid(self):
        return True

    def GetProcess(self):
        return self.process

    def GetNumModules(self):
        return 0

    def FindFirstType(self, name):
        return self.types.get(name, FakeType(name))

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
    def __init__(self, target, name, type_name, value=None, address=None, valid=True, type_obj=None):
        self.target = target
        self.name = name
        self.type_name = type_name
        self.value = value
        self.address = address
        self.valid = valid
        self.type_obj = type_obj

    def IsValid(self):
        return self.valid

    def GetTarget(self):
        return self.target

    def GetType(self):
        return self.type_obj or FakeType(self.type_name)

    def GetLoadAddress(self):
        return self.address or 0

    def GetChildMemberWithName(self, name):
        if name in ("Value", "value", "<Value>k__BackingField", "_value"):
            return FakeValue(self.target, name, "int", self.value)
        if name == "__ptr" and self.address is not None:
            return FakeValue(self.target, name, "int", address=self.address, type_obj=FakeType("int"))
        return FakeValue(self.target, name, "int", None, valid=False)

    def GetValueAsSigned(self, default=0):
        if self.value is not None:
            if isinstance(self.value, (list, tuple)):
                return self.value[0] if self.value else default
            return self.value
        if self.address is not None and self.target is not None:
            process = self.target.GetProcess()
            if self.type_name in ("float", "System.Single"):
                return int(struct.unpack_from("<f", process.memory, self.address)[0])
            if self.type_name in ("bool", "System.Boolean"):
                return 1 if process.memory[self.address] else 0
            return struct.unpack_from("<i", process.memory, self.address)[0]
        return default

    def GetValue(self):
        if self.value is not None:
            if isinstance(self.value, (list, tuple)):
                return str(self.value[0]) if self.value else "0"
            return str(self.value)
        if self.address is not None and self.target is not None:
            return str(self.GetValueAsSigned(0))
        return None

    def GetSummary(self):
        return self.GetValue()

    def GetValueAsDouble(self):
        if self.value is not None:
            if isinstance(self.value, (list, tuple)):
                return float(self.value[0]) if self.value else 0.0
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

    def GetValueAsUnsigned(self, default=0):
        value = self.GetValueAsSigned(default)
        return value if value >= 0 else 0

    def CreateValueFromData(self, name, data, value_type):
        return FakeValue(self.target, name, value_type.GetName(), data, type_obj=value_type)

    def CreateValueFromAddress(self, name, address, value_type):
        return FakeValue(self.target, name, value_type.GetName(), address=address, type_obj=value_type)

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

    def GetPC(self):
        return 0

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
        self.stepped_out = False
        self.stop_reason = None

    def IsValid(self):
        return True

    def GetSelectedFrame(self):
        return self.frame

    def GetNumFrames(self):
        return len(self.frames)

    def GetFrameAtIndex(self, index):
        return self.frames[index]

    def GetStopReason(self):
        return self.stop_reason

    def StepOver(self):
        self.stepped = True

    def StepOut(self):
        self.stepped_out = True


class FakeExecutionContext:
    def __init__(self, frame):
        self.frame = frame
        self.thread = FakeThread(frame)

    def GetFrame(self):
        return self.frame

    def GetThread(self):
        return self.thread


class FakeStream:
    def __init__(self):
        self.messages = []

    def Print(self, message):
        self.messages.append(message)


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
    lldb.eStopReasonPlanComplete = 7
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
    sys.modules["realm_lldb"] = module
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

    user_source_path = os.path.join(os.path.dirname(os.path.abspath(script_path)), "MapScript.cs")
    with open(user_source_path, "w") as user_source_file:
        user_source_file.write("// test map script" + chr(10))
    user_frame = FakeFrame({}, function_name="Realm.Maps.MapScript.Update", path=user_source_path, line=42)
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

    int_type = FakeType("int", byte_size=4)
    unit_type = FakeType("Realm_Ecs_Realm_Ecs_UnitId", byte_size=4)
    array_data_type = FakeType("Realm_Ecs_Realm_Ecs_UnitId[0]", byte_size=4)
    array_type = FakeType(
        "__Array<Realm_Ecs_Realm_Ecs_UnitId>",
        fields=[FakeField("Length", int_type, 4), FakeField("Data", array_data_type, 8)],
        byte_size=8)
    wrapper_array_type = FakeType(
        "WebAssemblyRefWrapper<__Array<Realm_Ecs_Realm_Ecs_UnitId>>",
        template_argument=array_type,
        byte_size=4)
    list_type = FakeType(
        "S_P_CoreLib_System_Collections_Generic_List_1<Realm_Ecs_Realm_Ecs_UnitId>",
        fields=[
            FakeField("_items", wrapper_array_type, 4),
            FakeField("_size", int_type, 8),
            FakeField("_version", int_type, 12),
        ],
        byte_size=16,
        base_count=1)
    wrapper_type = FakeType(
        "WebAssemblyRefWrapper<S_P_CoreLib_System_Collections_Generic_List_1<Realm_Ecs_Realm_Ecs_UnitId>>",
        template_argument=list_type,
        byte_size=4)
    types = {item.name: item for item in [int_type, unit_type, array_type, wrapper_array_type, list_type, wrapper_type]}

    generic_process = FakeProcess(MEMORY)
    generic_target = FakeTarget(generic_process, types)
    wrapper_host = BASE + 0x3000
    list_host = BASE + 0x1000
    array_host = BASE + 0x2000
    write_int(MEMORY, wrapper_host, 0x1000)
    write_int(MEMORY, list_host + 4, 0x2000)
    write_int(MEMORY, list_host + 8, 2)
    write_int(MEMORY, list_host + 12, 1)
    write_int(MEMORY, array_host + 4, 2)
    write_int(MEMORY, array_host + 8, 10)
    write_int(MEMORY, array_host + 12, 20)

    wrapper_value = FakeValue(generic_target, "units", wrapper_type.GetName(), address=wrapper_host, type_obj=wrapper_type)
    provider = module.WebAssemblyRefWrapperSyntheticProvider(wrapper_value, {})
    expect(provider.mode == "list", "the generic wrapper provider did not recognize List<UnitId>")
    expect(provider.num_children() == 4, "the generic list provider did not expose Count, Capacity and two elements")
    expect(provider.get_child_at_index(0).GetValueAsSigned() == 2, "the generic list provider did not read Count")
    expect(provider.get_child_at_index(1).GetValueAsSigned() == 2, "the generic list provider did not read Capacity")
    expect(provider.get_child_at_index(2).GetValueAsSigned() == 10, "the generic list provider did not read element 0")
    expect(provider.get_child_at_index(3).GetValueAsSigned() == 20, "the generic list provider did not read element 1")

    list_value = FakeValue(generic_target, "units_list", list_type.GetName(), address=list_host, type_obj=list_type)
    list_provider = module.ManagedListSyntheticProvider(list_value, {})
    expect(list_provider.num_children() == 4, "the direct list provider did not expose the initial elements")
    write_int(MEMORY, list_host + 8, 3)
    write_int(MEMORY, array_host + 16, 30)
    expect(list_provider.update() is True, "the direct list provider did not report a change")
    expect(list_provider.num_children() == 5, "the direct list provider did not refresh after the list grew")
    expect(list_provider.get_child_at_index(4).GetValueAsSigned() == 30, "the direct list provider did not read the new element")

    class_type = FakeType("SomeClass", byte_size=8, base_count=1)
    class_data_type = FakeType("SomeClass[0]", byte_size=4)
    class_array_type = FakeType(
        "__Array<SomeClass>",
        fields=[FakeField("Length", int_type, 4), FakeField("Data", class_data_type, 8)],
        byte_size=8)
    class_wrapper_array_type = FakeType(
        "WebAssemblyRefWrapper<__Array<SomeClass>>",
        template_argument=class_array_type,
        byte_size=4)
    class_list_type = FakeType(
        "S_P_CoreLib_System_Collections_Generic_List_1<SomeClass>",
        fields=[
            FakeField("_items", class_wrapper_array_type, 4),
            FakeField("_size", int_type, 8),
            FakeField("_version", int_type, 12),
        ],
        byte_size=16,
        base_count=1)
    class_wrapper_type = FakeType(
        "WebAssemblyRefWrapper<S_P_CoreLib_System_Collections_Generic_List_1<SomeClass>>",
        template_argument=class_list_type,
        byte_size=4)
    types[class_type.name] = class_type
    types[class_array_type.name] = class_array_type
    types[class_wrapper_array_type.name] = class_wrapper_array_type
    types[class_list_type.name] = class_list_type
    types[class_wrapper_type.name] = class_wrapper_type

    class_wrapper_host = BASE + 0x4000
    class_list_host = BASE + 0x4100
    class_array_host = BASE + 0x4200
    write_int(MEMORY, class_wrapper_host, 0x4100)
    write_int(MEMORY, class_list_host + 4, 0x4200)
    write_int(MEMORY, class_list_host + 8, 2)
    write_int(MEMORY, class_list_host + 12, 1)
    write_int(MEMORY, class_array_host + 4, 2)
    write_int(MEMORY, class_array_host + 8, 0x4300)
    write_int(MEMORY, class_array_host + 12, 0x4400)

    class_wrapper_value = FakeValue(generic_target, "classes", class_wrapper_type.GetName(), address=class_wrapper_host, type_obj=class_wrapper_type)
    class_provider = module.WebAssemblyRefWrapperSyntheticProvider(class_wrapper_value, {})
    expect(class_provider.mode == "list", "the generic wrapper provider did not recognize List<SomeClass>")
    expect(class_provider.get_child_at_index(2).GetLoadAddress() == BASE + 0x4300, "the generic list provider did not translate class element 0")
    expect(class_provider.get_child_at_index(3).GetLoadAddress() == BASE + 0x4400, "the generic list provider did not translate class element 1")

    inspect_list_frame = FakeFrame({"units": wrapper_value})
    inspect_list_process = FakeProcess(MEMORY)
    inspect_list_process.thread = FakeThread(inspect_list_frame)
    inspect_list_target = FakeTarget(inspect_list_process, types)
    inspect_list_result = FakeResult()
    module.realm_inspect(FakeDebugger(inspect_list_target), "units", inspect_list_result, {})
    inspect_list_output = chr(10).join(inspect_list_result.messages)
    expect("List<UnitId>" in inspect_list_output, "realm_inspect did not summarize the list")
    expect("Count = " in inspect_list_output, "realm_inspect did not print the list count")
    expect("[0] = " in inspect_list_output, "realm_inspect did not print list element 0")

    string_type = FakeType(
        "System_String",
        fields=[
            FakeField("_stringLength", int_type, 4),
            FakeField("_firstChar", FakeType("char16_t", byte_size=2), 8),
        ],
        byte_size=12,
        base_count=1)
    wrapper_string_type = FakeType(
        "WebAssemblyRefWrapper<System_String>",
        template_argument=string_type,
        byte_size=4)
    types[string_type.name] = string_type
    types[wrapper_string_type.name] = wrapper_string_type

    string_guest = 0x5000
    string_host = BASE + string_guest
    string_text = "unit/chad"
    write_int(MEMORY, string_host + 4, len(string_text))
    encoded = string_text.encode("utf-16-le")
    MEMORY[string_host + 8:string_host + 8 + len(encoded)] = encoded
    string_wrapper_host = BASE + 0x5100
    write_int(MEMORY, string_wrapper_host, string_guest)

    string_wrapper_value = FakeValue(generic_target, "templateId", wrapper_string_type.GetName(), address=string_wrapper_host, type_obj=wrapper_string_type)
    string_provider = module.WebAssemblyRefWrapperSyntheticProvider(string_wrapper_value, {})
    expect(string_provider.mode == "string", "the generic wrapper provider did not recognize System_String")
    expect(string_provider.get_child_at_index(0).GetValue() == string_text, "the wrapper string provider did not decode Value")
    expect(string_provider.get_child_at_index(1).GetValueAsSigned() == len(string_text), "the wrapper string provider did not read Length")
    expect(module.WebAssemblyRefWrapperSummaryProvider(string_wrapper_value, {}) == '"unit/chad"', "the wrapper string summary did not quote the value")

    direct_string_value = FakeValue(generic_target, "templateId", string_type.GetName(), address=string_host, type_obj=string_type)
    direct_string_provider = module.ManagedStringSyntheticProvider(direct_string_value, {})
    expect(direct_string_provider.get_child_at_index(0).GetValue() == string_text, "the direct string provider did not decode Value")
    expect(module.ManagedStringSummaryProvider(direct_string_value, {}) == '"unit/chad"', "the direct string summary did not quote the value")

    string_provider = module.ManagedStringSyntheticProvider(direct_string_value, {})
    write_int(MEMORY, string_host + 4, 4)
    MEMORY[string_host + 8:string_host + 16] = "hero".encode("utf-16-le")
    expect(string_provider.update() is True, "the direct string provider did not report a change")
    expect(string_provider.get_child_at_index(0).GetValue() == "hero", "the direct string provider did not refresh the decoded text")

    line_entries = [
        ("C:/map/MapScript.cs", 15, 0x100),
        ("C:/map/MapScript.cs", 15, 0x200),
        ("C:/map/MapScript.cs", 16, 0x300),
        ("C:/host/Other.cs", 15, 0x400),
    ]
    expect(module.select_statement_addresses(line_entries, "MapScript.cs", 15) == [0x100, 0x200], "address-based breakpoint selection did not filter by file and line")

    module._STEP_UNTIL_USER = False
    just_my_code_debugger = FakeDebugger(generic_target)
    just_my_code_result = FakeResult()
    module.realm_just_my_code(just_my_code_debugger, "on", just_my_code_result, {})
    expect(module._JUST_MY_CODE_ENABLED is True, "realm_just_my_code did not enable the mode")
    expect(any("target stop-hook add" in command for command in just_my_code_debugger.commands), "realm_just_my_code did not install the stop hook")

    host_frame = FakeFrame({}, function_name="Realm.MapAPI.GameAPI_WasmModule.get_PlayerCount", path="D:/realm/Realm.MapAPI/WasmWrappers.g.cs", line=10)
    host_context = FakeExecutionContext(host_frame)
    host_context.thread.stop_reason = module.lldb.eStopReasonPlanComplete
    module.RealmJustMyCodeStopHook().handle_stop(host_context, FakeStream())
    expect(host_context.thread.stepped_out is True, "just-my-code did not step out of a host wrapper frame")

    user_context = FakeExecutionContext(user_frame)
    user_context.thread.stop_reason = module.lldb.eStopReasonPlanComplete
    module.RealmJustMyCodeStopHook().handle_stop(user_context, FakeStream())
    expect(user_context.thread.stepped_out is False, "just-my-code stepped out of a user frame")

    breakpoint_context = FakeExecutionContext(host_frame)
    breakpoint_context.thread.stop_reason = 2
    module.RealmJustMyCodeStopHook().handle_stop(breakpoint_context, FakeStream())
    expect(breakpoint_context.thread.stepped_out is False, "just-my-code interfered with a breakpoint stop")

    module._JUST_MY_CODE_ENABLED = True
    module._JUST_MY_CODE_INSTALLED = True
    module._STEP_HOOK_INSTALLED = True
    module._WATCH_HOOK_INSTALLED = True
    module._WATCHES = [{"expression": "unit", "handle_spec": module.HANDLE_TYPES["UnitId"], "field_spec": module.find_field_spec(module.HANDLE_TYPES["UnitId"], "Health"), "samples": [100.0]}]
    reload_result = FakeResult()
    module.realm_reload(FakeDebugger(verify_target), "", reload_result, {})
    expect(module._JUST_MY_CODE_ENABLED is True, "realm_reload reset the just-my-code enabled flag")
    expect(module._JUST_MY_CODE_INSTALLED is True, "realm_reload reset the just-my-code installed flag")
    expect(module._STEP_HOOK_INSTALLED is True, "realm_reload reset the step hook flag")
    expect(module._WATCH_HOOK_INSTALLED is True, "realm_reload reset the watch hook flag")
    expect(len(module._WATCHES) == 1, "realm_reload lost the active watches")

    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
