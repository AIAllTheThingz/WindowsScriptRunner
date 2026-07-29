using WindowsScriptRunner.Domain.Exceptions;

namespace WindowsScriptRunner.Domain;

internal static class EnumGuard
{
    public static TEnum RequireDefined<TEnum>(TEnum value, string fieldName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new DomainValidationException($"{fieldName} contains an undefined {typeof(TEnum).Name} value.");
        }

        return value;
    }
}
