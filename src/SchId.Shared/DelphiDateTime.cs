namespace SchId.Shared;

/// <summary>
/// Convierte entre el formato TDateTime de Delphi (guardado en SQL Server como
/// numeric(18,8) en columnas como Personas.FechaAlta o Estancias.FIngreso/FSalida)
/// y DateTime de .NET.
///
/// En TDateTime, el día 0 es el 30 de diciembre de 1899; la parte entera del
/// número son los días transcurridos y la parte decimal es la fracción del día
/// (hora). OJO: esto NO es lo mismo que el cast implícito de SQL Server de un
/// numeric/float a DATETIME, que usa el 1 de enero de 1900 como día 0. Si se usa
/// CAST(columna AS DATETIME) directo en T-SQL, el resultado queda desfasado
/// 2 días respecto a la fecha real. Por eso esta conversión se hace aquí, en
/// código, y no con CAST en las consultas.
/// </summary>
public static class DelphiDateTime
{
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    public static DateTime? ToDateTime(decimal? value)
    {
        if (value is null)
        {
            return null;
        }

        return Epoch.AddDays((double)value.Value);
    }

    public static decimal? FromDateTime(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        var totalDays = (value.Value - Epoch).TotalDays;
        return (decimal)totalDays;
    }
}
