using Mono.Cecil;
using Mono.Cecil.Cil;

var dumpMode = args.Length > 0 && args[0] == "--dump";
var asmPath = dumpMode
    ? args.ElementAtOrDefault(1) ?? @"C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\Managed\com.uboat.game.dll"
    : args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\Managed\com.uboat.game.dll";
var terms = (dumpMode ? args.Skip(2) : args.Skip(1)).DefaultIfEmpty("AirQuality").Select(x => x.ToLowerInvariant()).ToArray();
var asm = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { ReadSymbols = false });
var allTypes = asm.MainModule.Types.SelectMany(Flatten).ToList();

Console.WriteLine($"ASSEMBLY {asmPath}");
Console.WriteLine($"TERMS {string.Join(", ", terms)}");

if (dumpMode)
{
    foreach (var type in allTypes)
    {
        foreach (var method in type.Methods)
        {
            if (!Hits(method.FullName, terms))
                continue;

            Console.WriteLine();
            Console.WriteLine($"METHOD {method.FullName}");
            if (!method.HasBody)
            {
                Console.WriteLine("  <no body>");
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                var operand = FormatOperand(instruction.Operand);
                Console.WriteLine($"  {instruction.Offset:X4}: {instruction.OpCode,-12} {operand}");
            }
        }
    }

    return;
}

Console.WriteLine();
Console.WriteLine("== NAME HITS ==");
foreach (var type in allTypes)
{
    var typeLinePrinted = false;

    void PrintTypeOnce()
    {
        if (typeLinePrinted)
            return;

        Console.WriteLine($"TYPE {type.FullName}");
        typeLinePrinted = true;
    }

    if (Hits(type.FullName, terms))
        PrintTypeOnce();

    foreach (var field in type.Fields.Where(field => Hits(field.Name, terms) || Hits(field.FieldType.FullName, terms)))
    {
        PrintTypeOnce();
        Console.WriteLine($"  FIELD {field.FieldType.FullName} {field.Name}");
    }

    foreach (var property in type.Properties.Where(property => Hits(property.Name, terms) || Hits(property.PropertyType.FullName, terms)))
    {
        PrintTypeOnce();
        Console.WriteLine($"  PROP {property.PropertyType.FullName} {property.Name}");
    }

    foreach (var method in type.Methods.Where(method => Hits(method.FullName, terms)))
    {
        PrintTypeOnce();
        Console.WriteLine($"  METHOD {method.FullName}");
    }
}

Console.WriteLine();
Console.WriteLine("== IL STRING / MEMBER HITS ==");
var methodHitCount = 0;
foreach (var type in allTypes)
{
    foreach (var method in type.Methods)
    {
        if (!method.HasBody)
            continue;

        var hits = new List<string>();
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand is string s && Hits(s, terms))
                hits.Add($"ldstr \"{s}\"");

            if (instruction.Operand is MemberReference member)
            {
                var declaringType = member.DeclaringType?.FullName ?? "";
                var memberText = $"{declaringType}.{member.Name}";
                if (Hits(memberText, terms))
                    hits.Add($"{instruction.OpCode.Code} {memberText}");
            }
        }

        if (hits.Count == 0)
            continue;

        methodHitCount++;
        Console.WriteLine($"METHOD {method.FullName}");
        foreach (var hit in hits.Distinct().Take(20))
            Console.WriteLine($"  {hit}");
    }
}

Console.WriteLine();
Console.WriteLine($"IL_METHOD_HITS {methodHitCount}");

static bool Hits(string? value, string[] terms)
{
    if (string.IsNullOrEmpty(value))
        return false;

    var lowered = value.ToLowerInvariant();
    return terms.Any(lowered.Contains);
}

static string FormatOperand(object? operand)
{
    return operand switch
    {
        null => "",
        string text => $"\"{text}\"",
        Instruction instruction => $"IL_{instruction.Offset:X4}",
        Instruction[] instructions => string.Join(", ", instructions.Select(x => $"IL_{x.Offset:X4}")),
        MemberReference member => $"{member.DeclaringType?.FullName}.{member.Name}",
        _ => operand.ToString() ?? "",
    };
}

static IEnumerable<TypeDefinition> Flatten(TypeDefinition t)
{
    yield return t;
    foreach (var n in t.NestedTypes)
        foreach (var x in Flatten(n)) yield return x;
}
