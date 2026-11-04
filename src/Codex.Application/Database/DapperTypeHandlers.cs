using System.Data;
using Dapper;

namespace Codex.Application.Database;

// Npgsql reads timestamptz as DateTime (UTC), and Dapper will not bind that to a DateTimeOffset record
// parameter on its own, so every DTO with a timestamp would need a hand-written row type. This handler lets
// queries map timestamptz columns straight to DateTimeOffset (offset zero). Parameters are not affected:
// pass UTC values (TimeProvider.GetUtcNow() is), since Npgsql rejects any other offset for timestamptz.
// Registered once by DatabaseServices (the app) and CodexDatabase (tests that query directly).
public static class DapperTypeHandlers
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        }
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
            => parameter.Value = value.ToUniversalTime();

        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTime utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            DateTimeOffset offset => offset,
            _ => throw new InvalidCastException($"Cannot read {value.GetType().Name} as DateTimeOffset."),
        };
    }
}
