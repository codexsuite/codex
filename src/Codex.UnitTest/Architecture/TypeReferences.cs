using System.Reflection;
using System.Reflection.Emit;

namespace Codex.UnitTest.Architecture;

// Every type a declared type refers to: its base type and interfaces, its fields, properties, events and
// method signatures, its locals, and every type, field or method named by an IL instruction. Reflection only,
// no Roslyn. Generic arguments, arrays and by-refs are unpacked, so List<Other> counts as a reference to Other.
public static class TypeReferences
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    public static IReadOnlyCollection<Type> Of(Type type)
    {
        var found = new HashSet<Type>();
        Add(found, type.BaseType);
        foreach (var contract in type.GetInterfaces())
        {
            Add(found, contract);
        }
        foreach (var field in type.GetFields(Declared))
        {
            Add(found, field.FieldType);
        }
        foreach (var property in type.GetProperties(Declared))
        {
            Add(found, property.PropertyType);
        }
        foreach (var @event in type.GetEvents(Declared))
        {
            Add(found, @event.EventHandlerType);
        }
        foreach (var method in type.GetConstructors(Declared).Cast<MethodBase>().Concat(type.GetMethods(Declared)))
        {
            AddMethod(found, method, type);
        }
        found.Remove(type);
        return found;
    }

    private static void AddMethod(HashSet<Type> found, MethodBase method, Type owner)
    {
        if (method is MethodInfo info)
        {
            Add(found, info.ReturnType);
        }
        foreach (var parameter in method.GetParameters())
        {
            Add(found, parameter.ParameterType);
        }
        var body = method.GetMethodBody();
        if (body == null)
        {
            return;
        }
        foreach (var local in body.LocalVariables)
        {
            Add(found, local.LocalType);
        }
        var typeArguments = owner.IsGenericType ? owner.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        foreach (var token in OperandTokens(body.GetILAsByteArray()!))
        {
            AddOperand(found, method.Module, token, typeArguments, methodArguments);
        }
    }

    private static void AddOperand(HashSet<Type> found, Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
    {
        MemberInfo? member;
        try
        {
            member = module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return; // a token that is not a type, field or method (a string or signature operand)
        }
        switch (member)
        {
            case Type type:
                Add(found, type);
                break;
            case FieldInfo field:
                Add(found, field.DeclaringType);
                break;
            case MethodBase method:
                // A call into a constructed generic carries its type arguments on the declaring type.
                Add(found, method.DeclaringType);
                if (method.IsGenericMethod)
                {
                    foreach (var argument in method.GetGenericArguments())
                    {
                        Add(found, argument);
                    }
                }
                break;
        }
    }

    private static void Add(HashSet<Type> found, Type? type)
    {
        if (type == null || !found.Add(type))
        {
            return;
        }
        if (type.HasElementType)
        {
            Add(found, type.GetElementType());
        }
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Add(found, argument);
            }
        }
        Add(found, type.DeclaringType);
    }

    private static IEnumerable<int> OperandTokens(byte[] il)
    {
        var position = 0;
        while (position < il.Length)
        {
            var value = (short)il[position++];
            if (value == 0xFE)
            {
                value = (short)(0xFE00 | il[position++]);
            }
            var opCode = OpCodesByValue[value];
            switch (opCode.OperandType)
            {
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok:
                    yield return BitConverter.ToInt32(il, position);
                    position += 4;
                    break;
                case OperandType.InlineSwitch:
                    position += 4 + (4 * BitConverter.ToInt32(il, position));
                    break;
                default:
                    position += OperandSize(opCode.OperandType);
                    break;
            }
        }
    }

    private static int OperandSize(OperandType type) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4,
    };
}
