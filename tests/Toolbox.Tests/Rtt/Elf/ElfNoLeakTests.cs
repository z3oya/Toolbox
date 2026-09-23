using System.Reflection;
using Toolbox.Core.Rtt;
using Toolbox.Core.Rtt.Elf;

namespace Toolbox.Tests;

/// <summary>Encapsulation guard: ELFSharp must stay an implementation detail of the Elf
/// submodule. This walks the public surface of every module type (plus RttControlBlock, the
/// RTT-facing consumer) and fails the build's test run if any ELFSharp type leaks into a
/// signature - the mechanical version of the "library types never leak" design rule.</summary>
public class ElfNoLeakTests
{
    public static TheoryData<Type> RootTypes => new()
    {
        typeof(ElfImage), typeof(ElfSymbol), typeof(SymbolLookup), typeof(SymbolLookupStatus),
        typeof(ElfSymbolKind), typeof(ElfSymbolBinding), typeof(ElfLoadStatus), typeof(ElfSection),
        typeof(RttControlBlock), typeof(RttElfLocateResult), typeof(RttElfLocateStatus),
    };

    [Theory]
    [MemberData(nameof(RootTypes))]
    public void Public_surface_references_no_elfsharp_types(Type root)
    {
        var visited = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Type type = queue.Dequeue();
            if (!visited.Add(type)) continue;
            if (type.IsArray || type.IsByRef || type.IsPointer)
            {
                EnqueueIfOurs(queue, type.GetElementType());
                continue;
            }
            if (type.IsGenericParameter) continue;

            Assert.False(type.FullName!.StartsWith("ELFSharp", StringComparison.Ordinal),
                $"{root.Name}'s public surface leaks {type.FullName}");

            if (type.Namespace?.StartsWith("Toolbox.Core") != true) continue;   // descend only into our own types

            foreach (ConstructorInfo ctor in type.GetConstructors())
                foreach (Type parameter in ctor.GetParameters().Select(p => p.ParameterType))
                    AddReferenced(queue, parameter);
            foreach (MethodInfo method in type.GetMethods())
            {
                AddReferenced(queue, method.ReturnType);
                foreach (Type parameter in method.GetParameters().Select(p => p.ParameterType))
                    AddReferenced(queue, parameter);
            }
            foreach (PropertyInfo property in type.GetProperties()) AddReferenced(queue, property.PropertyType);
            foreach (FieldInfo field in type.GetFields()) AddReferenced(queue, field.FieldType);
            foreach (EventInfo @event in type.GetEvents()) AddReferenced(queue, @event.EventHandlerType);
        }
    }

    private static void AddReferenced(Queue<Type> queue, Type? type)
    {
        if (type is null) return;
        queue.Enqueue(type);
        if (type.IsArray || type.IsByRef || type.IsPointer) queue.Enqueue(type.GetElementType()!);
        if (type.IsConstructedGenericType)
            foreach (Type arg in type.GetGenericArguments()) queue.Enqueue(arg);
    }

    private static void EnqueueIfOurs(Queue<Type> queue, Type? type)
    {
        if (type is not null) queue.Enqueue(type);
    }
}
