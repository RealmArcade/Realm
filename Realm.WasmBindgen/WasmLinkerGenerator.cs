using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Realm.WasmBindgen;

[Generator]
public partial class WasmLinkerGenerator : IIncrementalGenerator
{
    private enum PrmKind
    {
        DirectInt,
        DirectFloat,
        BoolParam,
        StringParam,
        StringListParam,
        Vector3Param,
        Vector3NullableParam,
        EntityParam,
        Unsupported
    }

    private enum RetKind
    {
        Void,
        DirectInt,
        DirectFloat,
        BoolReturn,
        StringReturn,
        EntityReturn,
        EntityNullableReturn,
        EntityListReturn,
        StringListReturn,
        Unsupported
    }

    private struct ParamInfo
    {
        public string Name { get; }
        public PrmKind Kind { get; }
        public ITypeSymbol Type { get; }

        public ParamInfo(string name, PrmKind kind, ITypeSymbol type)
        {
            Name = name;
            Kind = kind;
            Type = type;
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationProvider = context.CompilationProvider;

        var witFileProvider = context.AdditionalTextsProvider
            .Where(file => System.IO.Path.GetFileName(file.Path).Equals("game.wit", System.StringComparison.OrdinalIgnoreCase))
            .Select((file, cancellationToken) => file.GetText(cancellationToken)?.ToString() ?? "");

        var combined = compilationProvider.Combine(witFileProvider.Collect());

        context.RegisterSourceOutput(combined, Execute);
    }

    private static void Execute(SourceProductionContext context, (Compilation Left, System.Collections.Immutable.ImmutableArray<string> Right) input)
    {
        var compilation = input.Left;
        var gameApiSymbol = compilation.GetTypeByMetadataName("Realm.MapAPI.IGameAPI");
        if (gameApiSymbol == null)
            return;

        var entityInterfaces = DiscoverEntityInterfaces(gameApiSymbol);
        string staticWit = input.Right.FirstOrDefault() ?? "";
        string manualFunctions = ExtractManualFunctions(staticWit);

        string witContent = GenerateWitContent(gameApiSymbol, entityInterfaces, manualFunctions);
        string mapApiWitPath = FindMapApiWitPath(compilation);
        WriteWitContent(mapApiWitPath, witContent);

        GenerateOutputSources(context, compilation.AssemblyName ?? "", gameApiSymbol, entityInterfaces, manualFunctions);
    }

    private static string ExtractManualFunctions(string staticWit)
    {
        if (string.IsNullOrEmpty(staticWit))
            return "";

        var match = GameApiInterfaceRegex().Match(staticWit);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string FindMapApiWitPath(Compilation compilation)
    {
        var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(t => !string.IsNullOrEmpty(t.FilePath));
        if (syntaxTree == null)
            throw new Exception(@"[WasmLinkerGenerator] Error: unable to determine path for Realm.MapAPI\wit\game.g.wit");

        string? dir = System.IO.Path.GetDirectoryName(syntaxTree.FilePath);
        while (dir != null)
        {
            string candidate = System.IO.Path.Combine(dir, "Realm.MapAPI", "wit", "game.g.wit");
            if (System.IO.File.Exists(candidate) || System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(candidate)))
                return candidate;

            if (System.IO.Path.GetFileName(dir) == "Realm.MapAPI")
            {
                string candidateInProject = System.IO.Path.Combine(dir, "wit", "game.g.wit");
                if (System.IO.File.Exists(candidateInProject) || System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(candidateInProject)))
                    return candidateInProject;
            }
            dir = System.IO.Path.GetDirectoryName(dir);
        }

        throw new Exception(@"[WasmLinkerGenerator] Error: unable to determine path for Realm.MapAPI\wit\game.g.wit");
    }

    private static void WriteWitContent(string mapApiWitPath, string witContent)
    {
        string? dir = System.IO.Path.GetDirectoryName(mapApiWitPath);
        if (dir != null && System.IO.Directory.Exists(dir))
        {
            if (!System.IO.File.Exists(mapApiWitPath) || System.IO.File.ReadAllText(mapApiWitPath, Encoding.UTF8) != witContent)
            {
                System.IO.File.WriteAllText(mapApiWitPath, witContent, Encoding.UTF8);
            }
        }
    }

    private static void GenerateOutputSources(SourceProductionContext context, string assemblyName, INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions)
    {
        if (assemblyName == "Realm.Godot")
        {
            context.AddSource("WasmRuntime.g.cs",
                SourceText.From(GenerateHostBindings(gameApiSymbol, entityInterfaces), Encoding.UTF8));

            context.AddSource("WasmRuntime.AutoEvents.g.cs",
                SourceText.From(GenerateAutoEvents(gameApiSymbol), Encoding.UTF8));

            context.AddSource("GeneratedWit.g.cs",
                SourceText.From(GenerateWitConstant(gameApiSymbol, entityInterfaces, manualFunctions), Encoding.UTF8));
        }
        else if (assemblyName == "Realm.MapAPI")
        {
            context.AddSource("WasmWrappers.g.cs",
                SourceText.From(GenerateWasmWrappers(gameApiSymbol, entityInterfaces), Encoding.UTF8));
        }
    }

    // ── Helper Discovery and Symbol Checks ──────────────────────────────────────

    private static HashSet<INamedTypeSymbol> DiscoverEntityInterfaces(INamedTypeSymbol gameApiSymbol)
    {
        var discovered = new HashSet<INamedTypeSymbol>(SymbolComparer.Instance);
        var queue = new Queue<INamedTypeSymbol>();
        queue.Enqueue(gameApiSymbol);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var member in current.GetMembers())
            {
                ITypeSymbol? type = null;
                if (member is IPropertySymbol prop)
                {
                    type = prop.Type;
                }
                else if (member is IMethodSymbol method)
                {
                    foreach (var p in method.Parameters)
                    {
                        ProcessType(p.Type, discovered, queue);
                    }
                    type = method.ReturnType;
                }

                if (type != null)
                {
                    ProcessType(type, discovered, queue);
                }
            }
        }

        discovered.Remove(gameApiSymbol);
        return discovered;
    }

    private static void ProcessType(ITypeSymbol type, HashSet<INamedTypeSymbol> discovered, Queue<INamedTypeSymbol> queue)
    {
        if (IsCollection(type, out var elemType))
        {
            type = elemType;
        }

        if (IsEntityInterface(type, out var unwrapped))
        {
            if (unwrapped is INamedTypeSymbol named && !discovered.Contains(named))
            {
                discovered.Add(named);
                queue.Enqueue(named);
            }
        }
    }

    private static bool IsEntityInterface(ITypeSymbol type, out ITypeSymbol unwrappedType)
    {
        unwrappedType = type;
        if (type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            unwrappedType = named.TypeArguments[0];
        }
        else if (type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            unwrappedType = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        if (unwrappedType.TypeKind != TypeKind.Interface)
            return false;

        string display = unwrappedType.ToDisplayString();
        if (!display.StartsWith("Realm.MapAPI."))
            return false;

        if (display == "Realm.MapAPI.IGameAPI" || display == "Realm.MapAPI.IMapScript")
            return false;

        return true;
    }

    private static bool IsCollection(ITypeSymbol type, out ITypeSymbol elementType)
    {
        elementType = null!;
        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            if (namedType.IsGenericType && namedType.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            {
                elementType = namedType.TypeArguments[0];
                return true;
            }

            foreach (var iface in namedType.AllInterfaces)
            {
                if (iface.IsGenericType && iface.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                {
                    elementType = iface.TypeArguments[0];
                    return true;
                }
            }
        }

        return false;
    }

    private static string CleanInterfaceName(ITypeSymbol type)
    {
        string name = type.Name;
        if (name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
            return name.Substring(1);
        return name;
    }

    private static string FindCollectionMember(INamedTypeSymbol gameApiSymbol, ITypeSymbol entityType)
    {
        foreach (var member in gameApiSymbol.GetMembers())
        {
            ITypeSymbol? returnType = null;
            if (member is IPropertySymbol prop)
            {
                returnType = prop.Type;
            }
            else if (member is IMethodSymbol method && method.Parameters.Length == 0 && method.MethodKind == MethodKind.Ordinary)
            {
                returnType = method.ReturnType;
            }

            if (returnType != null && IsCollection(returnType, out var elemType) && SymbolEqualityComparer.Default.Equals(elemType, entityType))
            {
                return member is IMethodSymbol ? member.Name + "()" : member.Name;
            }
        }
        throw new InvalidOperationException($"Could not find collection member on IGameAPI returning a collection of {entityType.ToDisplayString()}");
    }

    private static string FindResolverMember(INamedTypeSymbol gameApiSymbol, ITypeSymbol entityType)
    {
        foreach (var member in gameApiSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32 && method.MethodKind == MethodKind.Ordinary)
            {
                ITypeSymbol ret = method.ReturnType;
                if (IsEntityInterface(ret, out var unwrapped) && SymbolEqualityComparer.Default.Equals(unwrapped, entityType))
                {
                    return member.Name;
                }
            }
        }
        throw new InvalidOperationException($"Could not find resolver method on IGameAPI returning {entityType.ToDisplayString()} taking a single int parameter");
    }

    private class SymbolComparer : IEqualityComparer<INamedTypeSymbol>
    {
        public static readonly SymbolComparer Instance = new SymbolComparer();
        public bool Equals(INamedTypeSymbol x, INamedTypeSymbol y) => SymbolEqualityComparer.Default.Equals(x, y);
        public int GetHashCode(INamedTypeSymbol obj) => obj.ToDisplayString().GetHashCode();
    }

    // ── Output 1: Host-side DefineFunction registrations ────────────────────────

    private static string GenerateHostBindings(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces)
    {
        var sb = new StringBuilder();
        AppendHostBindingsHeader(sb);

        var definedFunctions = new HashSet<string>();
        bool lastWasMultiLine = false;

        GenerateGameApiBindings(sb, gameApiSymbol, definedFunctions, ref lastWasMultiLine);
        GenerateEntityInterfaceBindings(sb, entityInterfaces, gameApiSymbol, definedFunctions, ref lastWasMultiLine);

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendHostBindingsHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Wasmtime;");
        sb.AppendLine("using Realm.MapAPI;");
        sb.AppendLine();
        sb.AppendLine("namespace Realm.Godot;");
        sb.AppendLine();
        sb.AppendLine("partial class WasmRuntime");
        sb.AppendLine("{");
        sb.AppendLine("    private partial void InitializeAutoBindings()");
        sb.AppendLine("    {");
        sb.AppendLine("        const string mod = \"custom:game/game-api\";");
        sb.AppendLine();
    }

    private static void GenerateGameApiBindings(StringBuilder sb, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, ref bool lastWasMultiLine)
    {
        var propertyAccessorMethods = CollectPropertyAccessorNames(gameApiSymbol);
        
        foreach (var member in gameApiSymbol.GetMembers())
        {
            if (member is IEventSymbol || !member.IsAbstract)
                continue;

            if (member is IPropertySymbol property)
            {
                EmitPropertyBindings(sb, property, ref lastWasMultiLine, "(_cachedApi ?? (IGameAPI?)GameHost.Instance)", definedFunctions);
            }
            else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
            {
                if (!propertyAccessorMethods.Contains(method.Name))
                {
                    EmitMethodBinding(sb, method, ref lastWasMultiLine, "(_cachedApi ?? (IGameAPI?)GameHost.Instance)", gameApiSymbol, definedFunctions);
                }
            }
        }
    }

    private static void GenerateEntityInterfaceBindings(StringBuilder sb, HashSet<INamedTypeSymbol> entityInterfaces, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, ref bool lastWasMultiLine)
    {
        foreach (var entityIface in entityInterfaces)
        {
            GenerateSingleEntityInterfaceBinding(sb, entityIface, gameApiSymbol, definedFunctions, ref lastWasMultiLine);
        }
    }

    private static void GenerateSingleEntityInterfaceBinding(StringBuilder sb, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, ref bool lastWasMultiLine)
    {
        string tKebab = ToKebabCase(CleanInterfaceName(entityIface));
        bool hasUniqueId = entityIface.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

        EmitEntityCountFunctionIfNeeded(sb, entityIface, gameApiSymbol, tKebab, hasUniqueId, definedFunctions);

        var accessorMethods = CollectPropertyAccessorNames(entityIface);
        foreach (var member in entityIface.GetMembers())
        {
            ProcessEntityInterfaceMember(sb, member, entityIface, gameApiSymbol, tKebab, accessorMethods, definedFunctions, ref lastWasMultiLine);
        }
    }

    private static void EmitEntityCountFunctionIfNeeded(StringBuilder sb, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, string tKebab, bool hasUniqueId, HashSet<string> definedFunctions)
    {
        if (hasUniqueId) return;

        string countName = $"{tKebab}-count";
        if (definedFunctions.Add(countName))
        {
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{countName}\", () => (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{FindCollectionMember(gameApiSymbol, entityIface)}?.Count() ?? 0);");
        }
    }

    private static void ProcessEntityInterfaceMember(StringBuilder sb, ISymbol member, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, string tKebab, HashSet<string> accessorMethods, HashSet<string> definedFunctions, ref bool lastWasMultiLine)
    {
        if (!member.IsAbstract) return;

        if (member is IPropertySymbol property && property.Name != "UniqueId")
        {
            EmitEntityPropertyBindings(sb, property, tKebab, entityIface, gameApiSymbol, ref lastWasMultiLine, definedFunctions);
        }
        else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
        {
            if (!accessorMethods.Contains(method.Name))
            {
                EmitEntityMethodBinding(sb, method, tKebab, entityIface, gameApiSymbol, ref lastWasMultiLine, definedFunctions);
            }
        }
    }

    private static void EmitEntityPropertyBindings(StringBuilder sb, IPropertySymbol property, string tKebab, INamedTypeSymbol entitySymbol, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions)
    {
        string witBase = tKebab + "-" + ToKebabCase(property.Name);
        string resolver = FindResolverMember(gameApiSymbol, entitySymbol);

        if (property.Type.ToDisplayString() == "System.Numerics.Vector3")
        {
            EmitEntityVector3Property(sb, property, witBase, resolver, definedFunctions);
            return;
        }

        var retKind = ClassifyReturn(property.Type);
        if (retKind == RetKind.Unsupported) return;

        EmitEntityStandardProperty(sb, property, witBase, resolver, gameApiSymbol, retKind, ref lastWasMultiLine, definedFunctions);
        EmitEntityPropertySetter(sb, property, witBase, resolver, gameApiSymbol, definedFunctions);
    }

    private static void EmitEntityVector3Property(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, HashSet<string> definedFunctions)
    {
        if (definedFunctions.Add($"{witBase}-x"))
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}-x\", (int id) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); return u != null ? u.{property.Name}.X : 0f; }});");
        if (definedFunctions.Add($"{witBase}-y"))
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}-y\", (int id) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); return u != null ? u.{property.Name}.Y : 0f; }});");
        if (definedFunctions.Add($"{witBase}-z"))
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}-z\", (int id) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); return u != null ? u.{property.Name}.Z : 0f; }});");

        if (property.SetMethod != null)
        {
            string setWitBase = "set-" + witBase;
            if (definedFunctions.Add(setWitBase))
            {
                sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, float valX, float valY, float valZ) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = new System.Numerics.Vector3(valX, valY, valZ); }});");
            }
        }
    }

    private static void EmitEntityStandardProperty(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, INamedTypeSymbol gameApiSymbol, RetKind retKind, ref bool lastWasMultiLine, HashSet<string> definedFunctions)
    {
        string expr = $"(_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id)?.{property.Name}";
        string mapped = MapEntityPropertyExpression(property, gameApiSymbol, retKind, expr);

        if (!definedFunctions.Add(witBase)) return;

        if (lastWasMultiLine) sb.AppendLine();
        if (retKind == RetKind.StringReturn)
        {
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}\", (Caller caller, int id, int retArea) => {{ string s = {mapped}; WriteGuestString(caller, retArea, s); }});");
        }
        else
        {
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}\", (int id) => {mapped});");
        }
        lastWasMultiLine = false;
    }

    private static string MapEntityPropertyExpression(IPropertySymbol property, INamedTypeSymbol gameApiSymbol, RetKind retKind, string expr)
    {
        if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
        {
            IsEntityInterface(property.Type, out var unwrapped);
            bool targetHasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
            if (targetHasId)
            {
                return $"{expr}?.UniqueId ?? 0";
            }
            else
            {
                string colName = FindCollectionMember(gameApiSymbol, unwrapped);
                return $"({expr} != null) ? ((_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{colName}.ToList().IndexOf({expr}) ?? -1) : -1";
            }
        }

        return retKind switch
        {
            RetKind.BoolReturn => $"({expr} ?? false) ? 1 : 0",
            RetKind.DirectFloat => $"{expr} ?? 0f",
            RetKind.DirectInt => $"{expr} ?? 0",
            RetKind.StringReturn => $"{expr} ?? \"\"",
            _ => "0"
        };
    }

    private static void EmitEntityPropertySetter(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions)
    {
        if (property.SetMethod == null) return;

        string setWitBase = "set-" + witBase;
        if (!definedFunctions.Add(setWitBase)) return;

        if (property.Type.SpecialType == SpecialType.System_Boolean)
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = val != 0; }});");
        else if (property.Type.SpecialType == SpecialType.System_Single)
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, float val) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = val; }});");
        else if (property.Type.SpecialType == SpecialType.System_Int32)
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = val; }});");
        else if (property.Type.SpecialType == SpecialType.System_String)
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (Caller caller, int id, int valPtr, int valLen) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = ReadGuestString(caller, valPtr, valLen); }});");
        else if (IsEntityInterface(property.Type, out var unwrapped))
        {
            string resolverR = FindResolverMember(gameApiSymbol, unwrapped);
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ var u = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolverR}(val); }});");
        }
    }

    private static void EmitEntityMethodBinding(StringBuilder sb, IMethodSymbol method, string tKebab, INamedTypeSymbol entitySymbol, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions)
    {
        var retKind = ClassifyReturn(method.ReturnType);
        if (!IsValidReturnType(retKind, method.ReturnType)) return;

        string witName = tKebab + "-" + ToKebabCase(method.Name);
        if (!definedFunctions.Add(witName)) return;

        string resolver = FindResolverMember(gameApiSymbol, entitySymbol);

        if (retKind == RetKind.StringListReturn)
        {
            EmitEntityStringListMethod(sb, witName, resolver, method, ref lastWasMultiLine);
            return;
        }

        var paramInfos = ExtractParamInfos(method);
        if (paramInfos == null) return;

        bool needsRetArea = retKind == RetKind.StringReturn;
        string lambdaParamsStr = BuildEntityLambdaParams(method, paramInfos, needsRetArea);

        if (lastWasMultiLine) sb.AppendLine();
        sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParamsStr}) =>");
        sb.AppendLine("        {");
        sb.AppendLine($"            var hostTarget = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id);");

        string defaultReturn = GetDefaultReturnValue(method, retKind, needsRetArea);
        if (!string.IsNullOrEmpty(defaultReturn))
            sb.AppendLine($"            if (hostTarget == null) return {defaultReturn};");
        else
            sb.AppendLine($"            if (hostTarget == null) return;");

        EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
        EmitEntityMethodCall(sb, method, retKind, gameApiSymbol, needsRetArea);

        if (!string.IsNullOrEmpty(defaultReturn))
            sb.AppendLine($"            return {defaultReturn};");

        sb.AppendLine("        });");
        lastWasMultiLine = true;
    }

    private static bool IsValidReturnType(RetKind retKind, ITypeSymbol returnType)
    {
        if (retKind == RetKind.EntityListReturn) return false;
        if (retKind != RetKind.Unsupported) return true;
        return returnType.ToDisplayString() == "System.Numerics.Vector3" || returnType.SpecialType == SpecialType.System_Void;
    }

    private static string GetDefaultReturnValue(IMethodSymbol method, RetKind retKind, bool needsRetArea)
    {
        if (method.ReturnType.SpecialType == SpecialType.System_Void || needsRetArea) return string.Empty;
        return retKind == RetKind.DirectFloat ? "0f" : "0";
    }

    private static List<ParamInfo>? ExtractParamInfos(IMethodSymbol method)
    {
        var paramInfos = new List<ParamInfo>();
        foreach (var param in method.Parameters)
        {
            var kind = ClassifyParam(param.Type);
            if (kind == PrmKind.Unsupported) return null;
            paramInfos.Add(new ParamInfo(param.Name, kind, param.Type));
        }
        return paramInfos;
    }

    private static string BuildEntityLambdaParams(IMethodSymbol method, List<ParamInfo> paramInfos, bool needsRetArea)
    {
        bool hasStringParam = method.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
        bool hasStringListParam = method.Parameters.Any(p => ClassifyParam(p.Type) == PrmKind.StringListParam);
        bool needsCaller = hasStringParam || hasStringListParam || needsRetArea;

        var lambdaParams = new List<string>();
        if (needsCaller) lambdaParams.Add("Caller caller");
        lambdaParams.Add("int id");
        lambdaParams.AddRange(paramInfos.Select(GetLambdaParamDefinition).Where(s => !string.IsNullOrEmpty(s)));
        if (needsRetArea) lambdaParams.Add("int retArea");

        return string.Join(", ", lambdaParams);
    }

    private static void EmitEntityStringListMethod(StringBuilder sb, string witName, string resolver, IMethodSymbol method, ref bool lastWasMultiLine)
    {
        sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}-count\", (int id) =>");
        sb.AppendLine("        {");
        sb.AppendLine($"            var target = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id);");
        sb.AppendLine($"            return target?.{method.Name}().Count() ?? 0;");
        sb.AppendLine("        });");

        sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}-get\", (Caller caller, int id, int index, int retArea) =>");
        sb.AppendLine("        {");
        sb.AppendLine($"            var target = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{resolver}(id);");
        sb.AppendLine($"            string s = target?.{method.Name}().ElementAtOrDefault(index) ?? \"\";");
        sb.AppendLine("            WriteGuestString(caller, retArea, s);");
        sb.AppendLine("        });");
        lastWasMultiLine = true;
    }

    private static string GetLambdaParamDefinition(ParamInfo p)
    {
        return p.Kind switch
        {
            PrmKind.StringParam => $"int {p.Name}Ptr, int {p.Name}Len",
            PrmKind.StringListParam => $"int {p.Name}Ptr, int {p.Name}Len",
            PrmKind.DirectFloat => $"float {p.Name}",
            PrmKind.DirectInt or PrmKind.BoolParam => $"int {p.Name}",
            PrmKind.EntityParam => $"int {p.Name}Id",
            PrmKind.Vector3Param => $"float {p.Name}X, float {p.Name}Y, float {p.Name}Z",
            PrmKind.Vector3NullableParam => $"float {p.Name}R, float {p.Name}G, float {p.Name}B, int {p.Name}HasColor",
            _ => ""
        };
    }

    private static void EmitEntityMethodParameters(StringBuilder sb, List<ParamInfo> paramInfos, INamedTypeSymbol gameApiSymbol)
    {
        foreach (var p in paramInfos)
        {
            if (p.Kind == PrmKind.StringParam)
                sb.AppendLine($"                string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
            else if (p.Kind == PrmKind.StringListParam)
                sb.AppendLine($"                string[] {p.Name} = ReadGuestStringList(caller, {p.Name}Ptr, {p.Name}Len);");
            else if (p.Kind == PrmKind.Vector3Param)
                sb.AppendLine($"                var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
            else if (p.Kind == PrmKind.EntityParam)
            {
                IsEntityInterface(p.Type, out var unwrapped);
                string res = FindResolverMember(gameApiSymbol, unwrapped);
                sb.AppendLine($"                var {p.Name} = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{res}({p.Name}Id);");
            }
        }
    }

    private static void EmitEntityMethodCall(StringBuilder sb, IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, bool needsRetArea)
    {
        string callExpr = BuildEntityMethodCallExpression(method);

        if (method.ReturnType.SpecialType == SpecialType.System_Void)
        {
            sb.AppendLine($"                {callExpr};");
            return;
        }

        if (retKind == RetKind.StringReturn)
        {
            sb.AppendLine($"                string s = {callExpr} ?? \"\";");
            sb.AppendLine("                WriteGuestString(caller, retArea, s);");
            return;
        }

        string exprMapped = MapEntityMethodReturnExpression(method, retKind, gameApiSymbol, callExpr);
        sb.AppendLine($"                return {exprMapped};");
    }

    private static string BuildEntityMethodCallExpression(IMethodSymbol method)
    {
        var callArgs = new List<string>();
        foreach (var p in method.Parameters)
        {
            if (p.Type.SpecialType == SpecialType.System_Boolean)
                callArgs.Add($"{p.Name} != 0");
            else
                callArgs.Add(p.Name);
        }

        return $"hostTarget.{method.Name}({string.Join(", ", callArgs)})";
    }

    private static string MapEntityMethodReturnExpression(IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, string callExpr)
    {
        if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
        {
            IsEntityInterface(method.ReturnType, out var unwrapped);
            bool targetHasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
            if (targetHasId)
            {
                return $"({callExpr})?.UniqueId ?? 0";
            }
            
            string colName = FindCollectionMember(gameApiSymbol, unwrapped);
            return $"({callExpr} != null) ? ((_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{colName}.ToList().IndexOf({callExpr}) ?? -1) : -1";
        }

        return retKind switch
        {
            RetKind.BoolReturn => $"({callExpr}) ? 1 : 0",
            _ => callExpr
        };
    }

    private static void EmitPropertyBindings(StringBuilder sb, IPropertySymbol property, ref bool lastWasMultiLine, string targetInstance, HashSet<string> definedFunctions)
    {
        var retKind = ClassifyReturn(property.Type);
        if (retKind == RetKind.Unsupported || RetKindToWit(retKind) == null)
            return;

        string witBase = ToKebabCase(property.Name);

        EmitPropertyGetter(sb, property, witBase, retKind, targetInstance, ref lastWasMultiLine, definedFunctions);
        EmitPropertySetter(sb, property, witBase, targetInstance, definedFunctions);
    }

    private static void EmitPropertyGetter(StringBuilder sb, IPropertySymbol property, string witBase, RetKind retKind, string targetInstance, ref bool lastWasMultiLine, HashSet<string> definedFunctions)
    {
        if (property.GetMethod == null) return;

        string getWitBase = $"get-{witBase}";
        if (!definedFunctions.Add(getWitBase)) return;

        if (lastWasMultiLine) sb.AppendLine();
        string expr = BuildPropertyGetterExpression(property.Name, retKind, targetInstance);

        if (retKind == RetKind.StringReturn)
        {
            sb.AppendLine($"        _linker.DefineFunction(mod, \"get-{witBase}\", (Caller caller, int retArea) => {{ string s = {expr}; WriteGuestString(caller, retArea, s); }});");
        }
        else
        {
            sb.AppendLine($"        _linker.DefineFunction(mod, \"get-{witBase}\", () => {expr});");
        }
        lastWasMultiLine = false;
    }

    private static void EmitPropertySetter(StringBuilder sb, IPropertySymbol property, string witBase, string targetInstance, HashSet<string> definedFunctions)
    {
        if (property.SetMethod == null) return;

        string setWitBase = $"set-{witBase}";
        if (!definedFunctions.Add(setWitBase)) return;

        if (property.Type.SpecialType == SpecialType.System_Boolean)
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int val) => {{ if ({targetInstance} != null) {targetInstance}.{property.Name} = val != 0; }});");
        else if (property.Type.SpecialType == SpecialType.System_Single)
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (float val) => {{ if ({targetInstance} != null) {targetInstance}.{property.Name} = val; }});");
        else if (property.Type.SpecialType == SpecialType.System_Int32)
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int val) => {{ if ({targetInstance} != null) {targetInstance}.{property.Name} = val; }});");
        else if (property.Type.SpecialType == SpecialType.System_String)
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (Caller caller, int valPtr, int valLen) => {{ if ({targetInstance} != null) {targetInstance}.{property.Name} = ReadGuestString(caller, valPtr, valLen); }});");
    }

    private static string BuildPropertyGetterExpression(string propertyName, RetKind retKind, string targetInstance)
    {
        return retKind switch
        {
            RetKind.DirectFloat => $"{targetInstance}?.{propertyName} ?? 0f",
            RetKind.DirectInt => $"{targetInstance}?.{propertyName} ?? 0",
            RetKind.BoolReturn => $"({targetInstance}?.{propertyName} ?? false) ? 1 : 0",
            RetKind.StringReturn => $"{targetInstance}?.{propertyName} ?? \"\"",
            _ => "0"
        };
    }

    private static void EmitMethodBinding(StringBuilder sb, IMethodSymbol method, ref bool lastWasMultiLine, string targetInstance, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions)
    {
        if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

        var paramInfos = new List<ParamInfo>();
        foreach (var param in method.Parameters)
        {
            var kind = ClassifyParam(param.Type);
            if (kind == PrmKind.Unsupported) return;
            paramInfos.Add(new ParamInfo(param.Name, kind, param.Type));
        }

        var retKind = ClassifyReturn(method.ReturnType);
        bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";
        if (retKind == RetKind.Unsupported && !isVector3Ret) return;

        var witName = ToKebabCase(method.Name);
        if (!definedFunctions.Add(witName)) return;

        if (retKind == RetKind.StringListReturn)
        {
            EmitStringListMethodBinding(sb, method, witName, paramInfos, targetInstance, gameApiSymbol, ref lastWasMultiLine);
            return;
        }

        EmitStandardMethodBinding(sb, method, witName, paramInfos, retKind, isVector3Ret, targetInstance, gameApiSymbol, ref lastWasMultiLine);
    }

    private static void EmitStringListMethodBinding(StringBuilder sb, IMethodSymbol method, string witName, List<ParamInfo> paramInfos, string targetInstance, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine)
    {
        var lambdaParamsCount = BuildLambdaParams(paramInfos, paramInfos.Exists(p => p.Kind == PrmKind.StringParam), false);
        sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}-count\", ({lambdaParamsCount}) =>");
        sb.AppendLine("        {");
        EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
        string callExprCount = BuildCallExpressionFull(method, paramInfos, targetInstance);
        sb.AppendLine($"            return {callExprCount}?.Count() ?? 0;");
        sb.AppendLine("        });");

        var paramInfosGet = new List<ParamInfo>(paramInfos);
        paramInfosGet.Add(new ParamInfo("index", PrmKind.DirectInt, gameApiSymbol));
        var lambdaParamsGet = BuildLambdaParams(paramInfosGet, needsCaller: true, needsRetArea: true);
        
        sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}-get\", ({lambdaParamsGet}) =>");
        sb.AppendLine("        {");
        EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
        string callExprGet = BuildCallExpressionFull(method, paramInfos, targetInstance);
        sb.AppendLine($"            string s = {callExprGet}?.ElementAtOrDefault(index) ?? \"\";");
        sb.AppendLine("            WriteGuestString(caller, retArea, s);");
        sb.AppendLine("        });");
        
        lastWasMultiLine = true;
    }

    private static void EmitStandardMethodBinding(StringBuilder sb, IMethodSymbol method, string witName, List<ParamInfo> paramInfos, RetKind retKind, bool isVector3Ret, string targetInstance, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine)
    {
        bool hasStringParam = method.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
        bool hasStringListParam = method.Parameters.Any(p => ClassifyParam(p.Type) == PrmKind.StringListParam);
        bool needsRetArea = !isVector3Ret && (retKind == RetKind.StringReturn || retKind == RetKind.EntityListReturn);
        bool needsCaller = hasStringParam || hasStringListParam || needsRetArea;

        var lambdaParams = BuildLambdaParams(paramInfos, needsCaller, needsRetArea);

        if (CanUseExpressionBody(method, isVector3Ret, hasStringParam, hasStringListParam, needsRetArea, retKind))
        {
            string expr = BuildSimpleExpression(method, paramInfos, retKind, targetInstance, gameApiSymbol);
            if (lastWasMultiLine) sb.AppendLine();
            sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParams}) => {expr});");
            lastWasWasMultiLine(ref lastWasMultiLine, false);
            return;
        }

        if (lastWasMultiLine) sb.AppendLine();

        if (isVector3Ret)
        {
            EmitVector3AxisBinding(sb, witName, "x", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol);
            EmitVector3AxisBinding(sb, witName, "y", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol);
            EmitVector3AxisBinding(sb, witName, "z", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol);
            lastWasWasMultiLine(ref lastWasMultiLine, true);
            return;
        }

        sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParams}) =>");
        sb.AppendLine("        {");
        EmitBlockBody(sb, method, paramInfos, retKind, targetInstance, gameApiSymbol);
        sb.AppendLine("        });");
        sb.AppendLine();
        lastWasWasMultiLine(ref lastWasMultiLine, true);
    }

    private static bool CanUseExpressionBody(IMethodSymbol method, bool isVector3Ret, bool hasStringParam, bool hasStringListParam, bool needsRetArea, RetKind retKind)
    {
        bool hasVector3Param = method.Parameters.Any(p => p.Type.ToDisplayString() == "System.Numerics.Vector3");
        bool hasVector3NullableParam = method.Parameters.Any(p => IsNullableVector3(p.Type));
        bool hasUnitParam = method.Parameters.Any(p => IsEntityInterface(p.Type, out _));

        return !isVector3Ret && !hasStringParam && !hasStringListParam && !hasVector3Param && !hasVector3NullableParam && !hasUnitParam && !needsRetArea && retKind != RetKind.EntityReturn && retKind != RetKind.EntityNullableReturn;
    }

    private static void EmitVector3AxisBinding(StringBuilder sb, string witName, string axis, string lambdaParams, List<ParamInfo> paramInfos, IMethodSymbol method, string targetInstance, INamedTypeSymbol gameApiSymbol)
    {
        sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}-{axis}\", ({lambdaParams}) =>");
        sb.AppendLine("        {");
        foreach (var p in paramInfos)
            if (p.Kind == PrmKind.StringParam)
                sb.AppendLine($"            string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
        foreach (var p in paramInfos)
            if (p.Kind == PrmKind.Vector3Param)
                sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
        foreach (var p in paramInfos)
            if (p.Kind == PrmKind.EntityParam)
            {
                IsEntityInterface(p.Type, out var unwrapped);
                string res = FindResolverMember(gameApiSymbol, unwrapped);
                sb.AppendLine($"            var {p.Name} = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{res}({p.Name}Id);");
            }

        var entityParams = paramInfos.FindAll(p => p.Kind == PrmKind.EntityParam);
        string callExpr = BuildCallExpressionFull(method, paramInfos, targetInstance);

        if (entityParams.Count == 0)
        {
            sb.AppendLine($"            return ({callExpr}).GetValueOrDefault().{axis.ToUpper()};");
        }
        else
        {
            string condition = string.Join(" && ", entityParams.Select(u => $"{u.Name} != null"));
            sb.AppendLine($"            return ({condition}) ? ({callExpr}).GetValueOrDefault().{axis.ToUpper()} : 0f;");
        }
        sb.AppendLine("        });");
    }

    private static string BuildLambdaParams(List<ParamInfo> paramInfos, bool needsCaller, bool needsRetArea)
    {
        var parts = new List<string>();

        if (needsCaller) parts.Add("Caller caller");
        parts.AddRange(paramInfos.Select(GetLambdaParamDefinition).Where(s => !string.IsNullOrEmpty(s)));
        if (needsRetArea) parts.Add("int retArea");

        return string.Join(", ", parts);
    }

    private static string BuildCallExpressionSimple(IMethodSymbol method, List<ParamInfo> paramInfos, string targetInstance)
    {
        var args = new List<string>();
        foreach (var p in method.Parameters)
        {
            if (p.Type.ToDisplayString() == "System.Numerics.Vector3")
                args.Add(p.Name);
            else if (IsNullableVector3(p.Type))
                args.Add(p.Name);
            else if (p.Type.SpecialType == SpecialType.System_Boolean)
                args.Add($"{p.Name} != 0");
            else
                args.Add(p.Name);
        }
        return $"{targetInstance}?.{method.Name}({string.Join(", ", args)})";
    }

    private static string BuildCallExpressionFull(IMethodSymbol method, List<ParamInfo> paramInfos, string targetInstance)
    {
        var args = new List<string>();
        foreach (var p in method.Parameters)
        {
            if (p.Type.ToDisplayString() == "System.Numerics.Vector3")
                args.Add(p.Name);
            else if (IsNullableVector3(p.Type))
                args.Add(p.Name);
            else if (p.Type.SpecialType == SpecialType.System_Boolean)
                args.Add($"{p.Name} != 0");
            else
                args.Add(p.Name);
        }
        return $"{targetInstance}?.{method.Name}({string.Join(", ", args)})";
    }

    private static void EmitBlockBody(StringBuilder sb, IMethodSymbol method, List<ParamInfo> paramInfos, RetKind retKind, string targetInstance, INamedTypeSymbol gameApiSymbol)
    {
        ReadStringParameters(sb, paramInfos);
        ReadVector3Parameters(sb, paramInfos);
        ReadEntityParameters(sb, paramInfos, gameApiSymbol);

        var entityParams = paramInfos.FindAll(p => p.Kind == PrmKind.EntityParam);
        string callExpr = BuildCallExpressionFull(method, paramInfos, targetInstance);

        EmitMethodCallAndReturn(sb, method, retKind, gameApiSymbol, entityParams, callExpr);
    }

    private static void ReadStringParameters(StringBuilder sb, List<ParamInfo> paramInfos)
    {
        foreach (var p in paramInfos)
        {
            if (p.Kind == PrmKind.StringParam)
                sb.AppendLine($"            string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
            else if (p.Kind == PrmKind.StringListParam)
                sb.AppendLine($"            string[] {p.Name} = ReadGuestStringList(caller, {p.Name}Ptr, {p.Name}Len);");
        }
    }

    private static void ReadVector3Parameters(StringBuilder sb, List<ParamInfo> paramInfos)
    {
        var processedVec3 = new HashSet<string>();
        foreach (var p in paramInfos)
        {
            if (p.Kind == PrmKind.Vector3Param && p.Name.EndsWith("X"))
            {
                string baseName = p.Name.Substring(0, p.Name.Length - 1);
                if (processedVec3.Add(baseName))
                    sb.AppendLine($"            var {baseName} = new System.Numerics.Vector3({baseName}X, {baseName}Y, {baseName}Z);");
            }
            else if (p.Kind == PrmKind.Vector3Param)
                sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
            else if (p.Kind == PrmKind.Vector3NullableParam)
                sb.AppendLine($"            System.Numerics.Vector3? {p.Name} = {p.Name}HasColor != 0 ? new System.Numerics.Vector3({p.Name}R, {p.Name}G, {p.Name}B) : null;");
        }
    }

    private static void ReadEntityParameters(StringBuilder sb, List<ParamInfo> paramInfos, INamedTypeSymbol gameApiSymbol)
    {
        foreach (var p in paramInfos)
        {
            if (p.Kind == PrmKind.EntityParam)
            {
                IsEntityInterface(p.Type, out var unwrapped);
                string res = FindResolverMember(gameApiSymbol, unwrapped);
                sb.AppendLine($"            var {p.Name} = (_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{res}({p.Name}Id);");
            }
        }
    }

    private static void EmitMethodCallAndReturn(StringBuilder sb, IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, List<ParamInfo> entityParams, string callExpr)
    {
        string condition = entityParams.Count > 0 ? string.Join(" && ", entityParams.Select(u => $"{u.Name} != null")) : "";

        switch (retKind)
        {
            case RetKind.Void:
                EmitVoidReturn(sb, entityParams.Count == 0, condition, callExpr);
                break;
            case RetKind.StringReturn:
                EmitStringReturn(sb, callExpr);
                break;
            case RetKind.EntityListReturn:
                EmitEntityListReturn(sb, method, gameApiSymbol, callExpr);
                break;
            case RetKind.EntityReturn:
            case RetKind.EntityNullableReturn:
                EmitEntitySingleReturn(sb, method, gameApiSymbol, entityParams.Count == 0, condition, callExpr);
                break;
            case RetKind.BoolReturn:
                EmitBoolReturn(sb, entityParams.Count == 0, condition, callExpr);
                break;
            default:
                EmitDefaultReturn(sb, retKind, entityParams.Count == 0, condition, callExpr);
                break;
        }
    }

    private static void EmitVoidReturn(StringBuilder sb, bool noEntityParams, string condition, string callExpr)
    {
        if (noEntityParams)
            sb.AppendLine($"            {callExpr};");
        else
            sb.AppendLine($"            if ({condition}) {callExpr};");
    }

    private static void EmitStringReturn(StringBuilder sb, string callExpr)
    {
        sb.AppendLine($"            string result = {callExpr} ?? \"\";");
        sb.AppendLine("            WriteGuestString(caller, retArea, result);");
    }

    private static void EmitEntityListReturn(StringBuilder sb, IMethodSymbol method, INamedTypeSymbol gameApiSymbol, string callExpr)
    {
        IsCollection(method.ReturnType, out var elemType);
        bool hasUniqueId = elemType.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
        sb.AppendLine($"            var items = {callExpr} ?? Array.Empty<{elemType.ToDisplayString()}>();");
        
        if (hasUniqueId)
        {
            sb.AppendLine("            WriteGuestIntList(caller, retArea, items.Select(e => e.UniqueId).ToList());");
        }
        else
        {
            string colName = FindCollectionMember(gameApiSymbol, elemType);
            sb.AppendLine($"            WriteGuestIntList(caller, retArea, items.Select(e => ((_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{colName}.ToList().IndexOf(e) ?? -1).ToList());");
        }
    }

    private static void EmitEntitySingleReturn(StringBuilder sb, IMethodSymbol method, INamedTypeSymbol gameApiSymbol, bool noEntityParams, string condition, string callExpr)
    {
        IsEntityInterface(method.ReturnType, out var unwrapped);
        bool hasUniqueId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

        if (noEntityParams)
        {
            sb.AppendLine($"            var result = {callExpr};");
            EmitEntityReturnMapping(sb, gameApiSymbol, unwrapped, hasUniqueId, "result");
        }
        else
        {
            sb.AppendLine($"            if ({condition})");
            sb.AppendLine("            {");
            sb.AppendLine($"                var result = {callExpr};");
            EmitEntityReturnMapping(sb, gameApiSymbol, unwrapped, hasUniqueId, "result");
            sb.AppendLine("            }");
            sb.AppendLine($"            return {(hasUniqueId ? "0" : "-1")};");
        }
    }

    private static void EmitBoolReturn(StringBuilder sb, bool noEntityParams, string condition, string callExpr)
    {
        if (noEntityParams)
            sb.AppendLine($"            return ({callExpr} ?? false) ? 1 : 0;");
        else
            sb.AppendLine($"            return ({condition}) ? (({callExpr} ?? false) ? 1 : 0) : 0;");
    }

    private static void EmitDefaultReturn(StringBuilder sb, RetKind retKind, bool noEntityParams, string condition, string callExpr)
    {
        string defaultVal = retKind == RetKind.DirectFloat ? "0f" : "0";
        if (noEntityParams)
            sb.AppendLine($"            return {callExpr} ?? {defaultVal};");
        else
            sb.AppendLine($"            return ({condition}) ? ({callExpr} ?? {defaultVal}) : {defaultVal};");
    }

    private static void EmitEntityReturnMapping(StringBuilder sb, INamedTypeSymbol gameApiSymbol, ITypeSymbol unwrapped, bool hasUniqueId, string resultVar)
    {
        if (hasUniqueId)
        {
            sb.AppendLine($"                return {resultVar}?.UniqueId ?? 0;");
        }
        else
        {
            string colName = FindCollectionMember(gameApiSymbol, unwrapped);
            sb.AppendLine($"                return ({resultVar} != null) ? ((_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{colName}.ToList().IndexOf({resultVar}) ?? -1) : -1;");
        }
    }

    private static string BuildSimpleExpression(IMethodSymbol method, List<ParamInfo> paramInfos, RetKind retKind, string targetInstance, INamedTypeSymbol gameApiSymbol)
    {
        string callExpr = BuildCallExpressionSimple(method, paramInfos, targetInstance);
        if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
        {
            IsEntityInterface(method.ReturnType, out var unwrapped);
            bool hasUniqueId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
            if (hasUniqueId)
            {
                return $"({callExpr})?.UniqueId ?? 0";
            }
            else
            {
                string colName = FindCollectionMember(gameApiSymbol, unwrapped);
                return $"({callExpr} != null) ? ((_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{colName}.ToList().IndexOf({callExpr}) ?? -1) : -1";
            }
        }

        return retKind switch
        {
            RetKind.Void => callExpr,
            RetKind.DirectInt => $"{callExpr} ?? 0",
            RetKind.DirectFloat => $"{callExpr} ?? 0f",
            RetKind.BoolReturn => $"({callExpr} ?? false) ? 1 : 0",
            RetKind.StringReturn => $"{callExpr} ?? \"\"",
            _ => callExpr
        };
    }

    private static PrmKind ClassifyParam(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Int32) return PrmKind.DirectInt;
        if (type.SpecialType == SpecialType.System_Single) return PrmKind.DirectFloat;
        if (type.SpecialType == SpecialType.System_Boolean) return PrmKind.BoolParam;
        if (type.SpecialType == SpecialType.System_String) return PrmKind.StringParam;

        if (IsCollection(type, out var elemType) && elemType.SpecialType == SpecialType.System_String)
            return PrmKind.StringListParam;

        string displayName = type.ToDisplayString();

        if (displayName == "System.Numerics.Vector3") return PrmKind.Vector3Param;
        if (IsNullableVector3(type)) return PrmKind.Vector3NullableParam;
        if (IsEntityInterface(type, out _)) return PrmKind.EntityParam;

        return PrmKind.Unsupported;
    }

    private static RetKind ClassifyReturn(ITypeSymbol type)
    {
        var specialRet = ClassifySpecialReturn(type.SpecialType);
        if (specialRet != RetKind.Unsupported) return specialRet;

        if (IsEntityInterface(type, out _))
        {
            bool isNullable = type.NullableAnnotation == NullableAnnotation.Annotated || type.ToDisplayString().EndsWith("?");
            return isNullable ? RetKind.EntityNullableReturn : RetKind.EntityReturn;
        }

        return ClassifyCollectionReturn(type);
    }

    private static RetKind ClassifySpecialReturn(SpecialType specialType)
    {
        return specialType switch
        {
            SpecialType.System_Void => RetKind.Void,
            SpecialType.System_Int32 => RetKind.DirectInt,
            SpecialType.System_Single => RetKind.DirectFloat,
            SpecialType.System_Boolean => RetKind.BoolReturn,
            SpecialType.System_String => RetKind.StringReturn,
            _ => RetKind.Unsupported
        };
    }

    private static RetKind ClassifyCollectionReturn(ITypeSymbol type)
    {
        if (!IsCollection(type, out var elemType)) return RetKind.Unsupported;
        
        if (elemType.SpecialType == SpecialType.System_String) return RetKind.StringListReturn;
        if (IsEntityInterface(elemType, out _)) return RetKind.EntityListReturn;
        
        return RetKind.Unsupported;
    }

    private static bool IsNullableVector3(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            return named.TypeArguments[0].ToDisplayString() == "System.Numerics.Vector3";
        }
        return false;
    }

    private static void lastWasWasMultiLine(ref bool last, bool current)
    {
        last = current;
    }

    // ── Output 2: Wit bindings dynamic generation ──────────────────────────────

    private static string GenerateWitContent(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions)
    {
        var functions = new SortedDictionary<string, string>(StringComparer.Ordinal);

        AppendWitGameApiMembers(functions, gameApiSymbol);
        AppendWitEntityInterfaces(functions, entityInterfaces);

        var witBody = new StringBuilder();
        foreach (var kvp in functions)
        {
            witBody.AppendLine($"    {kvp.Key}: {kvp.Value};");
        }

        if (!string.IsNullOrEmpty(manualFunctions))
        {
            witBody.AppendLine();
            witBody.AppendLine("    // Manual WIT entries");
            witBody.AppendLine("    " + manualFunctions.Replace("\r\n", "\n").Replace("\n", "\n    "));
        }

        var wit = new StringBuilder();
        wit.AppendLine("package custom:game;");
        wit.AppendLine();
        wit.AppendLine("interface game-api {");
        wit.Append(witBody.ToString());
        wit.AppendLine("}");
        wit.AppendLine();
        wit.AppendLine("world game-client {");
        wit.AppendLine("    import game-api;");
        wit.AppendLine("}");

        return wit.ToString();
    }

    private static void AppendWitGameApiMembers(SortedDictionary<string, string> functions, INamedTypeSymbol gameApiSymbol)
    {
        var propertyAccessorMethods = CollectPropertyAccessorNames(gameApiSymbol);

        foreach (var member in gameApiSymbol.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (member is IEventSymbol || !member.IsAbstract) continue;

            if (member is IPropertySymbol property)
            {
                AppendWitProperty(functions, property);
            }
            else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
            {
                if (!propertyAccessorMethods.Contains(method.Name))
                {
                    AppendWitMethod(functions, method);
                }
            }
        }
    }

    private static void AppendWitEntityInterfaces(SortedDictionary<string, string> functions, HashSet<INamedTypeSymbol> entityInterfaces)
    {
        foreach (var entityIface in entityInterfaces.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            AppendWitEntityInterface(functions, entityIface);
        }
    }

    private static string GenerateWitConstant(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions)
    {
        string witBody = GenerateWitContent(gameApiSymbol, entityInterfaces, manualFunctions);

        var wit = new StringBuilder();
        wit.AppendLine("// Do not edit file directly.");
        wit.AppendLine("// This file is auto-generated by the custom WasmLinkerGenerator tool.");
        wit.AppendLine("// Generator source: file:///D:/git/Realm/Realm.WasmBindgen/WasmLinkerGenerator.cs");
        wit.AppendLine();
        wit.AppendLine("namespace Realm.Godot;");
        wit.AppendLine();
        wit.AppendLine("public static class GeneratedWit");
        wit.AppendLine("{");
        wit.AppendLine("    public const string Content = \"\"\"");
        wit.Append(witBody);
        wit.AppendLine("\"\"\";");
        wit.AppendLine("}");

        return wit.ToString();
    }

    private static void AppendWitEntityInterface(SortedDictionary<string, string> functions, INamedTypeSymbol entitySymbol)
    {
        string tKebab = ToKebabCase(CleanInterfaceName(entitySymbol));
        bool hasUniqueId = entitySymbol.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

        if (!hasUniqueId)
        {
            AppendFunction(functions, $"{tKebab}-count", "func() -> s32");
        }

        var propertyAccessors = CollectPropertyAccessorNames(entitySymbol);
        foreach (var member in entitySymbol.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (!member.IsAbstract) continue;

            if (member is IPropertySymbol property && property.Name != "UniqueId")
            {
                AppendWitEntityProperties(functions, property, tKebab);
            }
            else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
            {
                if (!propertyAccessors.Contains(method.Name))
                {
                    AppendWitEntityMethod(functions, method, tKebab);
                }
            }
        }
    }

    private static void AppendWitEntityProperties(SortedDictionary<string, string> functions, IPropertySymbol property, string tKebab)
    {
        string witBase = $"{tKebab}-{ToKebabCase(property.Name)}";

        if (property.Type.ToDisplayString() == "System.Numerics.Vector3")
        {
            AppendFunction(functions, $"{witBase}-x", "func(id: s32) -> f32");
            AppendFunction(functions, $"{witBase}-y", "func(id: s32) -> f32");
            AppendFunction(functions, $"{witBase}-z", "func(id: s32) -> f32");
            
            if (property.SetMethod != null)
            {
                AppendFunction(functions, $"set-{witBase}", "func(id: s32, val-x: f32, val-y: f32, val-z: f32)");
            }
            return;
        }

        var retKind = ClassifyReturn(property.Type);
        if (retKind == RetKind.Unsupported) return;

        string? witType = RetKindToWit(retKind);
        if (witType != null)
        {
            AppendFunction(functions, witBase, $"func(id: s32) -> {witType}");
            if (property.SetMethod != null)
            {
                string? paramType = RetKindToWitParam(retKind);
                if (paramType != null)
                    AppendFunction(functions, $"set-{witBase}", $"func(id: s32, val: {paramType})");
            }
        }
    }

    private static void AppendWitEntityMethod(SortedDictionary<string, string> functions, IMethodSymbol method, string tKebab)
    {
        if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

        var retKind = ClassifyReturn(method.ReturnType);
        bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";

        if (retKind == RetKind.Unsupported && !isVector3Ret && method.ReturnType.SpecialType != SpecialType.System_Void) return;
        if (retKind == RetKind.EntityListReturn) return;

        string paramsStr = BuildWitEntityMethodParams(method);
        if (paramsStr == null) return;

        string witName = tKebab + "-" + ToKebabCase(method.Name);

        if (isVector3Ret)
        {
            AppendWitVector3Method(functions, witName, paramsStr);
            return;
        }

        if (retKind == RetKind.StringListReturn)
        {
            AppendWitStringListMethod(functions, witName, paramsStr);
            return;
        }

        AppendWitStandardMethod(functions, method, witName, paramsStr, retKind);
    }

    private static void AppendWitVector3Method(SortedDictionary<string, string> functions, string witName, string paramsStr)
    {
        AppendFunction(functions, $"{witName}-x", $"func({paramsStr}) -> f32");
        AppendFunction(functions, $"{witName}-y", $"func({paramsStr}) -> f32");
        AppendFunction(functions, $"{witName}-z", $"func({paramsStr}) -> f32");
    }

    private static void AppendWitStringListMethod(SortedDictionary<string, string> functions, string witName, string paramsStr)
    {
        AppendFunction(functions, $"{witName}-count", $"func({paramsStr}) -> s32");
        AppendFunction(functions, $"{witName}-get", $"func({paramsStr}, index: s32) -> string");
    }

    private static void AppendWitStandardMethod(SortedDictionary<string, string> functions, IMethodSymbol method, string witName, string paramsStr, RetKind retKind)
    {
        string retStr = (retKind == RetKind.Void || method.ReturnType.SpecialType == SpecialType.System_Void) ? "" : $" -> {RetKindToWit(retKind)}";
        AppendFunction(functions, witName, $"func({paramsStr}){retStr}");
    }

    private static string? BuildWitEntityMethodParams(IMethodSymbol method)
    {
        var witParams = new List<string> { "id: s32" };
        foreach (var param in method.Parameters)
        {
            string? paramWit = ParamToWit(param.Type, param.Name);
            if (paramWit == null) return null;
            witParams.Add(paramWit);
        }
        return string.Join(", ", witParams);
    }

    private static void AppendWitProperty(SortedDictionary<string, string> functions, IPropertySymbol property)
    {
        var retKind = ClassifyReturn(property.Type);
        if (retKind == RetKind.Unsupported)
            return;

        string witBase = ToKebabCase(property.Name);
        string? witType = RetKindToWit(retKind);
        if (witType == null)
            return;

        if (property.GetMethod != null)
            AppendFunction(functions, $"get-{witBase}", $"func() -> {witType}");

        if (property.SetMethod != null)
        {
            string? paramType = RetKindToWitParam(retKind);
            if (paramType != null)
                AppendFunction(functions, $"set-{witBase}", $"func(val: {paramType})");
        }
    }

    private static void AppendWitMethod(SortedDictionary<string, string> functions, IMethodSymbol method)
    {
        if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

        var retKind = ClassifyReturn(method.ReturnType);
        bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";
        if (retKind == RetKind.Unsupported && !isVector3Ret) return;

        string paramsStr = BuildWitMethodParams(method);
        if (paramsStr == null && method.Parameters.Length > 0) return;
        paramsStr ??= "";

        string witName = ToKebabCase(method.Name);

        if (retKind == RetKind.StringListReturn)
        {
            AppendFunction(functions, $"{witName}-count", $"func({paramsStr}) -> s32");
            AppendFunction(functions, $"{witName}-get", $"func({paramsStr}, index: s32) -> string");
        }
        else if (isVector3Ret)
        {
            AppendFunction(functions, $"{witName}-x", $"func({paramsStr}) -> f32");
            AppendFunction(functions, $"{witName}-y", $"func({paramsStr}) -> f32");
            AppendFunction(functions, $"{witName}-z", $"func({paramsStr}) -> f32");
        }
        else
        {
            string retStr = (retKind == RetKind.Void || method.ReturnType.SpecialType == SpecialType.System_Void) ? "" : $" -> {RetKindToWit(retKind)}";
            AppendFunction(functions, witName, $"func({paramsStr}){retStr}");
        }
    }

    private static string? BuildWitMethodParams(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0) return "";
        var witParams = new List<string>();
        foreach (var param in method.Parameters)
        {
            string? paramWit = ParamToWit(param.Type, param.Name);
            if (paramWit == null) return null;
            witParams.Add(paramWit);
        }
        return string.Join(", ", witParams);
    }

    private static string? ParamToWit(ITypeSymbol type, string name)
    {
        var kind = ClassifyParam(type);
        return kind switch
        {
            PrmKind.DirectInt => $"{ToKebabCase(name)}: s32",
            PrmKind.DirectFloat => $"{ToKebabCase(name)}: f32",
            PrmKind.BoolParam => $"{ToKebabCase(name)}: bool",
            PrmKind.StringParam => $"{ToKebabCase(name)}: string",
            PrmKind.StringListParam => $"{ToKebabCase(name)}: list<string>",
            PrmKind.Vector3Param => $"{ToKebabCase(name)}-x: f32, {ToKebabCase(name)}-y: f32, {ToKebabCase(name)}-z: f32",
            PrmKind.EntityParam => $"{ToKebabCase(name)}-id: s32",
            PrmKind.Vector3NullableParam => $"{ToKebabCase(name)}-r: f32, {ToKebabCase(name)}-g: f32, {ToKebabCase(name)}-b: f32, {ToKebabCase(name)}-has-color: bool",
            _ => null
        };
    }

    private static string? RetKindToWit(RetKind retKind)
    {
        return retKind switch
        {
            RetKind.Void => null,
            RetKind.DirectInt => "s32",
            RetKind.DirectFloat => "f32",
            RetKind.BoolReturn => "bool",
            RetKind.StringReturn => "string",
            RetKind.EntityReturn => "s32",
            RetKind.EntityNullableReturn => "s32",
            RetKind.EntityListReturn => "list<s32>",
            _ => null
        };
    }

    private static string? RetKindToWitParam(RetKind retKind)
    {
        return retKind switch
        {
            RetKind.Void => null,
            RetKind.DirectInt => "s32",
            RetKind.DirectFloat => "f32",
            RetKind.BoolReturn => "bool",
            RetKind.StringReturn => "string",
            RetKind.EntityReturn => "s32",
            RetKind.EntityNullableReturn => "s32",
            _ => null
        };
    }

    private static void AppendFunction(SortedDictionary<string, string> functions, string name, string body)
    {
        if (!functions.ContainsKey(name))
        {
            functions.Add(name, body);
        }
    }

    // ── Output 3: Auto-Events bindings dynamic generation ───────────────────────

    private static string GenerateAutoEvents(INamedTypeSymbol gameApiSymbol)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Wasmtime;");
        sb.AppendLine("using Realm.MapAPI;");
        sb.AppendLine();
        sb.AppendLine("namespace Realm.Godot;");
        sb.AppendLine();
        sb.AppendLine("partial class WasmRuntime");
        sb.AppendLine("{");

        var events = gameApiSymbol.GetMembers().OfType<IEventSymbol>().ToList();

        AppendAutoEventsFields(sb, events);
        AppendInitializeAutoEvents(sb, events);
        AppendSubscribeAutoEvents(sb, events);
        AppendUnsubscribeAutoEvents(sb, events);
        AppendAutoEventHandlers(sb, events, gameApiSymbol);

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendAutoEventsFields(StringBuilder sb, List<IEventSymbol> events)
    {
        foreach (var ev in events)
        {
            var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
            if (delegateMethod == null) continue;

            bool hasString = delegateMethod.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
            string fieldType = hasString ? "Function" : GetWasmDelegateType(delegateMethod.Parameters);
            sb.AppendLine($"    private {fieldType}? {GetWasmField(ev.Name)};");
        }
        sb.AppendLine();
    }

    private static void AppendInitializeAutoEvents(StringBuilder sb, List<IEventSymbol> events)
    {
        sb.AppendLine("    private partial void InitializeAutoEvents()");
        sb.AppendLine("    {");
        foreach (var ev in events)
        {
            var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
            if (delegateMethod == null) continue;

            string witName = ToKebabCase(ev.Name);
            bool hasString = delegateMethod.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);

            if (hasString)
            {
                sb.AppendLine($"        {GetWasmField(ev.Name)} = _instance.GetFunction(\"{witName}\");");
            }
            else
            {
                string typeArgs = GetWasmDelegateTypeArgs(delegateMethod.Parameters);
                string wrapMethod = string.IsNullOrEmpty(typeArgs) ? "WrapAction" : $"WrapAction<{typeArgs}>";
                sb.AppendLine($"        {GetWasmField(ev.Name)} = _instance.GetFunction(\"{witName}\")?.{wrapMethod}();");
            }
        }
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendSubscribeAutoEvents(StringBuilder sb, List<IEventSymbol> events)
    {
        sb.AppendLine("    private partial void SubscribeAutoEvents(IGameAPI api)");
        sb.AppendLine("    {");
        foreach (var ev in events)
        {
            sb.AppendLine($"        api.{ev.Name} += {GetWasmHandler(ev.Name)};");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendUnsubscribeAutoEvents(StringBuilder sb, List<IEventSymbol> events)
    {
        sb.AppendLine("    private partial void UnsubscribeAutoEvents()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_cachedApi != null)");
        sb.AppendLine("        {");
        foreach (var ev in events)
        {
            sb.AppendLine($"            _cachedApi.{ev.Name} -= {GetWasmHandler(ev.Name)};");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendAutoEventHandlers(StringBuilder sb, List<IEventSymbol> events, INamedTypeSymbol gameApiSymbol)
    {
        foreach (var ev in events)
        {
            var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
            if (delegateMethod == null) continue;

            string handlerName = GetWasmHandler(ev.Name);
            var sigParams = new List<string>();
            foreach (var p in delegateMethod.Parameters)
            {
                sigParams.Add($"{p.Type.ToDisplayString()} {p.Name}");
            }

            sb.AppendLine($"    private void {handlerName}({string.Join(", ", sigParams)})");
            sb.AppendLine("    {");

            string fieldName = GetWasmField(ev.Name);
            bool hasString = delegateMethod.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);

            if (hasString)
            {
                EmitStringAutoEventHandlerBody(sb, delegateMethod, fieldName, gameApiSymbol);
            }
            else
            {
                EmitStandardAutoEventHandlerBody(sb, delegateMethod, fieldName, gameApiSymbol);
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }
    }

    private static void EmitStringAutoEventHandlerBody(StringBuilder sb, IMethodSymbol delegateMethod, string fieldName, INamedTypeSymbol gameApiSymbol)
    {
        sb.AppendLine($"        if ({fieldName} != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            var memory = _instance.GetMemory(\"memory\");");
        sb.AppendLine("            if (memory != null)");
        sb.AppendLine("            {");

        var invokeArgs = new List<string>();
        foreach (var p in delegateMethod.Parameters)
        {
            if (p.Type.SpecialType == SpecialType.System_String)
            {
                sb.AppendLine($"                byte[] bytes_{p.Name} = System.Text.Encoding.UTF8.GetBytes({p.Name});");
                sb.AppendLine($"                int ptr_{p.Name} = AllocateInGuest(bytes_{p.Name}.Length);");
                sb.AppendLine($"                bytes_{p.Name}.CopyTo(memory.GetSpan(ptr_{p.Name}, bytes_{p.Name}.Length));");
                invokeArgs.Add($"ptr_{p.Name}");
                invokeArgs.Add($"bytes_{p.Name}.Length");
            }
            else if (p.Type.ToDisplayString() == "System.Numerics.Vector3" || p.Type.ToDisplayString() == "System.Numerics.Vector3?")
            {
                invokeArgs.Add($"{p.Name}.X");
                invokeArgs.Add($"{p.Name}.Y");
                invokeArgs.Add($"{p.Name}.Z");
            }
            else if (IsEntityInterface(p.Type, out var unwrapped))
            {
                bool hasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol prop && prop.Name == "UniqueId");
                if (hasId)
                {
                    invokeArgs.Add($"{p.Name}?.UniqueId ?? 0");
                }
                else
                {
                    string col = FindCollectionMember(gameApiSymbol, unwrapped);
                    invokeArgs.Add($"{p.Name} != null ? (((_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{col}().ToList().IndexOf({p.Name}) ?? -1) : -1");
                }
            }
            else
            {
                invokeArgs.Add(p.Name);
            }
        }

        sb.AppendLine($"                {fieldName}.Invoke({string.Join(", ", invokeArgs)});");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }

    private static void EmitStandardAutoEventHandlerBody(StringBuilder sb, IMethodSymbol delegateMethod, string fieldName, INamedTypeSymbol gameApiSymbol)
    {
        var invokeArgs = new List<string>();
        foreach (var p in delegateMethod.Parameters)
        {
            if (IsEntityInterface(p.Type, out var unwrapped))
            {
                bool hasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol prop && prop.Name == "UniqueId");
                if (hasId)
                {
                    invokeArgs.Add($"{p.Name}?.UniqueId ?? 0");
                }
                else
                {
                    string col = FindCollectionMember(gameApiSymbol, unwrapped);
                    invokeArgs.Add($"{p.Name} != null ? (((_cachedApi ?? (IGameAPI?)GameHost.Instance)?.{col}().ToList().IndexOf({p.Name}) ?? -1) : -1");
                }
            }
            else
            {
                invokeArgs.Add(p.Name);
            }
        }
        sb.AppendLine($"        {fieldName}?.Invoke({string.Join(", ", invokeArgs)});");
    }

    private static string GetWasmField(string eventName)
    {
        return $"_{char.ToLowerInvariant(eventName[2])}{eventName.Substring(3)}";
    }

    private static string GetWasmHandler(string eventName) => $"{eventName}Handler";

    private static string GetWasmDelegateType(System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters)
    {
        if (parameters.Length == 0) return "Action";
        return $"Action<{GetWasmDelegateTypeArgs(parameters)}>";
    }

    private static string GetWasmDelegateTypeArgs(System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters)
    {
        var list = new List<string>();
        foreach (var p in parameters)
        {
            if (IsEntityInterface(p.Type, out _))
                list.Add("int");
            else if (p.Type.SpecialType == SpecialType.System_Int32)
                list.Add("int");
            else if (p.Type.SpecialType == SpecialType.System_Single)
                list.Add("float");
            else if (p.Type.SpecialType == SpecialType.System_Boolean)
                list.Add("bool");
        }
        return string.Join(", ", list);
    }

    // ── Output 4: Guest-side WasmWrappers dynamic generation ────────────────────

    private static string GenerateWasmWrappers(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8603, CS1591, CS1573");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine("using GameClientWorld.wit.Imports.custom.game;");
        sb.AppendLine();
        sb.AppendLine("namespace Realm.MapAPI;");
        sb.AppendLine();
        sb.AppendLine("public interface IWasmWrapper");
        sb.AppendLine("{");
        sb.AppendLine("    int WasmId { get; }");
        sb.AppendLine("}");
        sb.AppendLine();

        GenerateEntityProxies(sb, entityInterfaces);
        GenerateGameApiProxy(sb, gameApiSymbol);

        return sb.ToString();
    }

    private static void GenerateEntityProxies(StringBuilder sb, HashSet<INamedTypeSymbol> entityInterfaces)
    {
        foreach (var entityIface in entityInterfaces)
        {
            GenerateSingleEntityProxy(sb, entityIface);
        }
    }

    private static void GenerateSingleEntityProxy(StringBuilder sb, INamedTypeSymbol entityIface)
    {
        string cleanName = CleanInterfaceName(entityIface);
        bool hasUniqueId = entityIface.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

        sb.AppendLine($"public class {cleanName}_WasmModule : {entityIface.ToDisplayString()}, IWasmWrapper");
        sb.AppendLine("{");
        
        EmitProxyIdProperty(sb, cleanName, hasUniqueId);

        string key = hasUniqueId ? "UniqueId" : "_index";
        var accessorMethods = CollectPropertyAccessorNames(entityIface);
        
        foreach (var member in entityIface.GetMembers())
        {
            EmitProxyMember(sb, member, cleanName, key, accessorMethods);
        }
        
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitProxyIdProperty(StringBuilder sb, string cleanName, bool hasUniqueId)
    {
        if (hasUniqueId)
        {
            sb.AppendLine("    public int UniqueId { get; }");
            sb.AppendLine("    public int WasmId => UniqueId;");
            sb.AppendLine($"    public {cleanName}_WasmModule(int id) => UniqueId = id;");
        }
        else
        {
            sb.AppendLine("    private readonly int _index;");
            sb.AppendLine("    public int WasmId => _index;");
            sb.AppendLine($"    public {cleanName}_WasmModule(int index) => _index = index;");
        }
        sb.AppendLine();
    }

    private static void EmitProxyMember(StringBuilder sb, ISymbol member, string cleanName, string key, HashSet<string> accessorMethods)
    {
        if (!member.IsAbstract) return;

        if (member is IPropertySymbol property && property.Name != "UniqueId")
        {
            EmitGuestProperty(sb, property, cleanName, key);
        }
        else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
        {
            if (!accessorMethods.Contains(method.Name))
            {
                EmitGuestMethod(sb, method, cleanName, key);
            }
        }
    }

    private static void GenerateGameApiProxy(StringBuilder sb, INamedTypeSymbol gameApiSymbol)
    {
        sb.AppendLine("public class GameAPI_WasmModule : IGameAPI");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly Dictionary<int, Action> _timers = new();");
        sb.AppendLine();

        var propertyAccessorMethods = CollectPropertyAccessorNames(gameApiSymbol);
        foreach (var member in gameApiSymbol.GetMembers())
        {
            if (member is IEventSymbol || !member.IsAbstract) continue;

            if (member is IPropertySymbol property)
            {
                EmitGuestProperty(sb, property, "", "");
            }
            else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
            {
                if (!propertyAccessorMethods.Contains(method.Name))
                {
                    EmitGuestMethod(sb, method, "", "");
                }
            }
        }

        GenerateProxyEvents(sb, gameApiSymbol);

        sb.AppendLine("}");
    }

    private static void GenerateProxyEvents(StringBuilder sb, INamedTypeSymbol gameApiSymbol)
    {
        var events = gameApiSymbol.GetMembers().OfType<IEventSymbol>().ToList();
        
        foreach (var ev in events)
        {
            EmitProxyEventDeclaration(sb, ev);
        }
        sb.AppendLine();

        foreach (var ev in events)
        {
            EmitProxyEventTrigger(sb, ev);
        }
    }

    private static void EmitProxyEventDeclaration(StringBuilder sb, IEventSymbol ev)
    {
        var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
        if (delegateMethod == null) return;

        sb.AppendLine($"    public event Action<{string.Join(", ", delegateMethod.Parameters.Select(p => p.Type.ToDisplayString()))}>? {ev.Name};");
    }

    private static void EmitProxyEventTrigger(StringBuilder sb, IEventSymbol ev)
    {
        var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
        if (delegateMethod == null) return;

        var triggerParams = new List<string>();
        foreach (var p in delegateMethod.Parameters)
        {
            if (IsEntityInterface(p.Type, out _))
                triggerParams.Add($"int {p.Name}");
            else if (p.Type.ToDisplayString().Contains("Vector3"))
                triggerParams.Add($"Vector3 {p.Name}");
            else
                triggerParams.Add($"{p.Type.ToDisplayString()} {p.Name}");
        }

        var invokeArgs = new List<string>();
        foreach (var p in delegateMethod.Parameters)
        {
            if (IsEntityInterface(p.Type, out var unwrapped))
            {
                string cleanElem = CleanInterfaceName(unwrapped);
                if (p.Type.NullableAnnotation == NullableAnnotation.Annotated || p.Type.ToDisplayString().Contains("?"))
                    invokeArgs.Add($"{p.Name} > 0 ? new {cleanElem}_WasmModule({p.Name}) : null");
                else
                    invokeArgs.Add($"new {cleanElem}_WasmModule({p.Name})");
            }
            else
            {
                invokeArgs.Add(p.Name);
            }
        }

        sb.AppendLine($"    public void TriggerOn{ev.Name.Substring(2)}({string.Join(", ", triggerParams)})");
        sb.AppendLine($"        => {ev.Name}?.Invoke({string.Join(", ", invokeArgs)});");
    }

    private static void EmitGuestProperty(StringBuilder sb, IPropertySymbol property, string prefix, string key)
    {
        string typeStr = property.Type.ToDisplayString();
        string keyArg = string.IsNullOrEmpty(key) ? "" : key;

        if (property.Type.ToDisplayString() == "System.Numerics.Vector3")
        {
            EmitGuestVector3Property(sb, property, typeStr, prefix, keyArg);
            return;
        }

        var retKind = ClassifyReturn(property.Type);
        if (retKind == RetKind.Unsupported) return;

        EmitGuestStandardProperty(sb, property, typeStr, prefix, keyArg, retKind);
    }

    private static void EmitGuestVector3Property(StringBuilder sb, IPropertySymbol property, string typeStr, string prefix, string keyArg)
    {
        sb.AppendLine($"    public {typeStr} {property.Name}");
        sb.AppendLine("    {");
        sb.AppendLine($"        get => new Vector3(");
        sb.AppendLine($"            IGameApiImports.{prefix}{property.Name}X({keyArg}),");
        sb.AppendLine($"            IGameApiImports.{prefix}{property.Name}Y({keyArg}),");
        sb.AppendLine($"            IGameApiImports.{prefix}{property.Name}Z({keyArg}));");
        
        if (property.SetMethod != null)
        {
            string commaKeyArg = string.IsNullOrEmpty(keyArg) ? "" : $"{keyArg}, ";
            sb.AppendLine("        set");
            sb.AppendLine("        {");
            sb.AppendLine($"            IGameApiImports.Set{prefix}{property.Name}({commaKeyArg}value.X, value.Y, value.Z);");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
    }

    private static void EmitGuestStandardProperty(StringBuilder sb, IPropertySymbol property, string typeStr, string prefix, string keyArg, RetKind retKind)
    {
        sb.AppendLine($"    public {typeStr} {property.Name}");
        sb.AppendLine("    {");

        string getterName = string.IsNullOrEmpty(prefix) ? $"Get{property.Name}" : $"{prefix}{property.Name}";

        if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
        {
            IsEntityInterface(property.Type, out var unwrapped);
            string cleanElem = CleanInterfaceName(unwrapped);
            if (retKind == RetKind.EntityNullableReturn)
            {
                sb.AppendLine($"        get {{ int id = IGameApiImports.{getterName}({keyArg}); return id > 0 ? new {cleanElem}_WasmModule(id) : null; }}");
            }
            else
            {
                sb.AppendLine($"        get => new {cleanElem}_WasmModule(IGameApiImports.{getterName}({keyArg}));");
            }
        }
        else
        {
            sb.AppendLine($"        get => IGameApiImports.{getterName}({keyArg});");
        }

        if (property.SetMethod != null)
        {
            string setVal = "value";
            if (IsEntityInterface(property.Type, out _))
            {
                bool isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated || property.Type.ToDisplayString().Contains("?");
                setVal = isNullable ? "(value != null) ? ((IWasmWrapper)value).WasmId : 0" : "((IWasmWrapper)value).WasmId";
            }
            string setArgs = string.IsNullOrEmpty(keyArg) ? setVal : $"{keyArg}, {setVal}";
            sb.AppendLine($"        set => IGameApiImports.Set{prefix}{property.Name}({setArgs});");
        }
        sb.AppendLine("    }");
    }

    private static void EmitGuestMethod(StringBuilder sb, IMethodSymbol method, string prefix, string key)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            if (BuildWitMethodParams(method) == null) return;
        }
        else
        {
            if (BuildWitEntityMethodParams(method) == null) return;
        }

        var retKind = ClassifyReturn(method.ReturnType);
        string typeStr = method.ReturnType.ToDisplayString();

        var paramsDecl = BuildGuestMethodParameters(method);
        var callArgs = BuildGuestMethodCallArgs(method, prefix, key);

        string importName = string.IsNullOrEmpty(prefix) ? method.Name : $"{prefix}{method.Name}";
        string importCall = $"IGameApiImports.{importName}({string.Join(", ", callArgs)})";

        sb.AppendLine($"    public {typeStr} {method.Name}({string.Join(", ", paramsDecl)})");
        sb.AppendLine("    {");

        EmitGuestMethodBody(sb, method, typeStr, retKind, importName, importCall, callArgs);

        sb.AppendLine("    }");
    }

    private static void EmitGuestMethodBody(StringBuilder sb, IMethodSymbol method, string typeStr, RetKind retKind, string importName, string importCall, List<string> callArgs)
    {
        switch (typeStr)
        {
            case "System.Numerics.Vector3":
                EmitGuestMethodVector3Return(sb, importName, callArgs);
                return;
        }

        switch (retKind)
        {
            case RetKind.StringListReturn:
                EmitGuestMethodStringListReturn(sb, importName, callArgs);
                return;
            case RetKind.EntityReturn:
            case RetKind.EntityNullableReturn:
                EmitGuestMethodEntityReturn(sb, method, importCall, retKind);
                return;
            case RetKind.EntityListReturn:
                EmitGuestMethodEntityListReturn(sb, method, importCall);
                return;
            case RetKind.Void:
                sb.AppendLine($"        {importCall};");
                return;
        }

        if (method.ReturnType.SpecialType == SpecialType.System_Void)
        {
            sb.AppendLine($"        {importCall};");
            return;
        }

        sb.AppendLine($"        return {importCall};");
    }

    private static List<string> BuildGuestMethodParameters(IMethodSymbol method)
    {
        var paramsDecl = new List<string>();
        foreach (var p in method.Parameters)
        {
            string pType = p.Type.ToDisplayString();
            string pName = p.Name;
            if (p.HasExplicitDefaultValue)
            {
                string defaultVal = p.ExplicitDefaultValue == null ? "null" : 
                                   (p.ExplicitDefaultValue is bool b ? b.ToString().ToLower() : 
                                   (p.ExplicitDefaultValue is string s ? $"\"{s}\"" : 
                                   (p.ExplicitDefaultValue is float f ? $"{f.ToString(System.Globalization.CultureInfo.InvariantCulture)}f" :
                                   (p.ExplicitDefaultValue is double d ? $"{d.ToString(System.Globalization.CultureInfo.InvariantCulture)}" :
                                   p.ExplicitDefaultValue.ToString()))));
                paramsDecl.Add($"{pType} {pName} = {defaultVal}");
            }
            else
            {
                paramsDecl.Add($"{pType} {pName}");
            }
        }
        return paramsDecl;
    }

    private static List<string> BuildGuestMethodCallArgs(IMethodSymbol method, string prefix, string key)
    {
        var callArgs = new List<string>();
        if (!string.IsNullOrEmpty(prefix)) callArgs.Add(key);

        foreach (var p in method.Parameters)
        {
            AppendGuestMethodCallArg(callArgs, p);
        }
        return callArgs;
    }

    private static void AppendGuestMethodCallArg(List<string> callArgs, IParameterSymbol p)
    {
        if (TryAppendEntityArg(callArgs, p)) return;
        if (TryAppendVector3Arg(callArgs, p)) return;
        if (TryAppendStringArg(callArgs, p)) return;
        if (TryAppendMiscArg(callArgs, p)) return;

        callArgs.Add(p.Name);
    }

    private static bool TryAppendEntityArg(List<string> callArgs, IParameterSymbol p)
    {
        if (!IsEntityInterface(p.Type, out _)) return false;
        
        bool isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated || p.Type.ToDisplayString().Contains("?");
        callArgs.Add(isNullable ? $"({p.Name} != null) ? ((IWasmWrapper){p.Name}).WasmId : 0" : $"((IWasmWrapper){p.Name}).WasmId");
        return true;
    }

    private static bool TryAppendVector3Arg(List<string> callArgs, IParameterSymbol p)
    {
        string typeStr = p.Type.ToDisplayString();
        if (typeStr == "System.Numerics.Vector3")
        {
            callArgs.Add($"{p.Name}.X");
            callArgs.Add($"{p.Name}.Y");
            callArgs.Add($"{p.Name}.Z");
            return true;
        }
        if (typeStr == "System.Numerics.Vector3?")
        {
            callArgs.Add($"{p.Name}?.X ?? 0f");
            callArgs.Add($"{p.Name}?.Y ?? 0f");
            callArgs.Add($"{p.Name}?.Z ?? 0f");
            callArgs.Add($"{p.Name}.HasValue");
            return true;
        }
        return false;
    }

    private static bool TryAppendStringArg(List<string> callArgs, IParameterSymbol p)
    {
        if (p.Type.SpecialType == SpecialType.System_String)
        {
            bool isNullable = p.NullableAnnotation == NullableAnnotation.Annotated || p.Type.ToDisplayString().Contains("?");
            callArgs.Add(isNullable ? $"{p.Name} ?? \"\"" : p.Name);
            return true;
        }
        return false;
    }

    private static bool TryAppendMiscArg(List<string> callArgs, IParameterSymbol p)
    {
        string typeStr = p.Type.ToDisplayString();
        if (typeStr == "object")
        {
            callArgs.Add($"{p.Name}?.ToString() ?? \"\"");
            return true;
        }
        if (ClassifyParam(p.Type) == PrmKind.StringListParam)
        {
            callArgs.Add($"{p.Name}?.ToList() ?? new List<string>()");
            return true;
        }
        return false;
    }

    private static void EmitGuestMethodVector3Return(StringBuilder sb, string importName, List<string> callArgs)
    {
        sb.AppendLine($"        return new Vector3(");
        sb.AppendLine($"            IGameApiImports.{importName}X({string.Join(", ", callArgs)}),");
        sb.AppendLine($"            IGameApiImports.{importName}Y({string.Join(", ", callArgs)}),");
        sb.AppendLine($"            IGameApiImports.{importName}Z({string.Join(", ", callArgs)}));");
    }

    private static void EmitGuestMethodStringListReturn(StringBuilder sb, string importName, List<string> callArgs)
    {
        var callArgsCount = new List<string>(callArgs);
        var callArgsGet = new List<string>(callArgs);
        callArgsGet.Add("i");
        
        sb.AppendLine($"        int count = IGameApiImports.{importName}Count({string.Join(", ", callArgsCount)});");
        sb.AppendLine($"        var list = new List<string>(count);");
        sb.AppendLine($"        for (int i = 0; i < count; i++)");
        sb.AppendLine($"            list.Add(IGameApiImports.{importName}Get({string.Join(", ", callArgsGet)}));");
        sb.AppendLine("        return list;");
    }

    private static void EmitGuestMethodEntityReturn(StringBuilder sb, IMethodSymbol method, string importCall, RetKind retKind)
    {
        IsEntityInterface(method.ReturnType, out var unwrapped);
        string cleanElem = CleanInterfaceName(unwrapped);
        if (retKind == RetKind.EntityNullableReturn)
        {
            sb.AppendLine($"        int id = {importCall};");
            sb.AppendLine($"        return id > 0 ? new {cleanElem}_WasmModule(id) : null;");
        }
        else
        {
            sb.AppendLine($"        return new {cleanElem}_WasmModule({importCall});");
        }
    }

    private static void EmitGuestMethodEntityListReturn(StringBuilder sb, IMethodSymbol method, string importCall)
    {
        IsCollection(method.ReturnType, out var elemType);
        string cleanElem = CleanInterfaceName(elemType);
        sb.AppendLine($"        return {importCall}.Select(id => ({elemType.ToDisplayString()})new {cleanElem}_WasmModule(id));");
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder();
        sb.Append(char.ToLowerInvariant(value[0]));
        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c))
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string ToPascalCase(string kebab)
    {
        if (string.IsNullOrEmpty(kebab)) return "";
        var sb = new StringBuilder();
        bool nextUpper = true;
        foreach (char c in kebab)
        {
            if (c == '-')
            {
                nextUpper = true;
            }
            else
            {
                if (nextUpper)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    nextUpper = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        return sb.ToString();
    }

    private static HashSet<string> CollectPropertyAccessorNames(INamedTypeSymbol symbol)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in symbol.GetMembers())
        {
            if (member is IPropertySymbol prop)
            {
                if (prop.GetMethod != null) set.Add(prop.GetMethod.Name);
                if (prop.SetMethod != null) set.Add(prop.SetMethod.Name);
            }
        }
        return set;
    }

    [GeneratedRegex(@"interface\s+game-api\s*\{(.*?)\}", RegexOptions.Singleline)]
    private static partial Regex GameApiInterfaceRegex();
}
