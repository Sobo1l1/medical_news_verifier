namespace MedicalNewsVerifier.Web;

/// <summary>
/// Npgsql для <c>timestamp with time zone</c> принимает только <see cref="DateTimeKind.Utc"/>.
/// Значения из HTML <c>datetime-local</c> приходят как <see cref="DateTimeKind.Unspecified"/>.
/// </summary>
public static class DateTimeUtc
{
    public static DateTime ToPostgresUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static DateTime? ToPostgresUtc(DateTime? value) =>
        value.HasValue ? ToPostgresUtc(value.Value) : null;
}
