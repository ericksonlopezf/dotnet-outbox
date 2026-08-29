// Copyright © Erickson Lopez. MIT License.
using System;
using System.Reflection;

namespace EricksonLopez.Outbox.Tests.Infrastructure;

/// <summary>
/// Provides safe reflection utilities for white-box testing of internal/private members with descriptive exception messages.
/// </summary>
internal static class ReflectionTestHelper
{
    private const BindingFlags DefaultInstanceFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags DefaultStaticFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    /// <summary>
    /// Retrieves a non-public instance or static field, throwing a descriptive <see cref="InvalidOperationException"/> if not found.
    /// </summary>
    public static FieldInfo GetFieldOrThrow(Type type, string fieldName, BindingFlags flags = DefaultInstanceFlags)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(fieldName);

        var field = type.GetField(fieldName, flags);
        if (field is null)
        {
            throw new InvalidOperationException(
                $"Field '{fieldName}' was not found on type '{type.FullName}' with binding flags '{flags}'. " +
                $"Verify that the field has not been renamed or removed during a refactoring.");
        }

        return field;
    }

    /// <summary>
    /// Gets the value of a non-public instance field safely.
    /// </summary>
    public static T GetFieldValue<T>(object instance, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var field = GetFieldOrThrow(instance.GetType(), fieldName, DefaultInstanceFlags);
        var value = field.GetValue(instance);
        return (T)value!;
    }

    /// <summary>
    /// Sets the value of a non-public instance field safely.
    /// </summary>
    public static void SetFieldValue(object instance, string fieldName, object? value)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var field = GetFieldOrThrow(instance.GetType(), fieldName, DefaultInstanceFlags);
        field.SetValue(instance, value);
    }

    /// <summary>
    /// Retrieves a non-public instance or static method, throwing a descriptive <see cref="InvalidOperationException"/> if not found.
    /// </summary>
    public static MethodInfo GetMethodOrThrow(Type type, string methodName, BindingFlags flags = DefaultInstanceFlags)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(methodName);

        var method = type.GetMethod(methodName, flags);
        if (method is null)
        {
            throw new InvalidOperationException(
                $"Method '{methodName}' was not found on type '{type.FullName}' with binding flags '{flags}'. " +
                $"Verify that the method signature has not changed during a refactoring.");
        }

        return method;
    }

    /// <summary>
    /// Retrieves a non-public nested type, throwing a descriptive <see cref="InvalidOperationException"/> if not found.
    /// </summary>
    public static Type GetNestedTypeOrThrow(Type declaringType, string nestedTypeName, BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(nestedTypeName);

        var nestedType = declaringType.GetNestedType(nestedTypeName, flags);
        if (nestedType is null)
        {
            throw new InvalidOperationException(
                $"Nested type '{nestedTypeName}' was not found in declaring type '{declaringType.FullName}'. " +
                $"Verify that the nested type has not been renamed or removed during a refactoring.");
        }

        return nestedType;
    }
}
