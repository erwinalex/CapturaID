using SchId.Shared;

namespace SchId.Tests;

public class DelphiDateTimeTests
{
    [Fact]
    public void Cero_es_el_30_de_diciembre_de_1899()
    {
        Assert.Equal(new DateTime(1899, 12, 30), DelphiDateTime.ToDateTime(0m));
    }

    /// <summary>
    /// Esta es la prueba que justifica que exista toda la clase. SQL Server, al
    /// convertir un numeric a DATETIME, usa 1900-01-01 como día cero; Delphi usa
    /// 1899-12-30. Son exactamente 2 días de diferencia, y por eso un
    /// CAST(columna AS DATETIME) en T-SQL da fechas desfasadas.
    /// </summary>
    [Fact]
    public void La_epoca_de_Delphi_esta_2_dias_antes_que_la_de_SQL_Server()
    {
        var epocaDelphi = DelphiDateTime.ToDateTime(0m)!.Value;
        var epocaSqlServer = new DateTime(1900, 1, 1);

        Assert.Equal(2, (epocaSqlServer - epocaDelphi).TotalDays);
    }

    [Theory]
    [InlineData(1, "1899-12-31")]
    [InlineData(2, "1900-01-01")]
    [InlineData(45000, "2023-03-15")]
    public void Convierte_dias_enteros_a_la_fecha_correcta(int dias, string esperado)
    {
        Assert.Equal(DateTime.Parse(esperado), DelphiDateTime.ToDateTime(dias));
    }

    [Fact]
    public void La_parte_decimal_es_la_hora_del_dia()
    {
        // .5 de día = mediodía.
        var mediodia = DelphiDateTime.ToDateTime(45000.5m);

        Assert.Equal(new DateTime(2023, 3, 15, 12, 0, 0), mediodia);
    }

    [Fact]
    public void Ida_y_vuelta_conserva_la_fecha_al_segundo()
    {
        var original = new DateTime(2024, 7, 14, 9, 41, 33);

        var convertido = DelphiDateTime.ToDateTime(DelphiDateTime.FromDateTime(original));

        Assert.NotNull(convertido);
        Assert.True(Math.Abs((original - convertido!.Value).TotalSeconds) < 1);
    }

    [Fact]
    public void Null_se_propaga_en_ambas_direcciones()
    {
        Assert.Null(DelphiDateTime.ToDateTime(null));
        Assert.Null(DelphiDateTime.FromDateTime(null));
    }
}
