using SchId.Api.Services;
using SchId.Shared;

namespace SchId.Tests;

public class RetentionPolicyTests
{
    private static decimal? Delphi(DateTime fecha) => DelphiDateTime.FromDateTime(fecha);

    private static readonly DateTime Hoy = new(2026, 8, 5);
    private static readonly DateTime Limite = Hoy.AddDays(-365);

    [Fact]
    public void Toma_la_ultima_salida_cuando_es_la_senal_mas_reciente()
    {
        var ultima = RetentionPolicy.CalcularUltimaActividad(
            fechaAlta: Delphi(new DateTime(2020, 1, 1)),
            ultimaModificacion: Delphi(new DateTime(2021, 1, 1)),
            maxFIngreso: Delphi(new DateTime(2024, 3, 1)),
            maxFSalida: Delphi(new DateTime(2024, 3, 5)));

        Assert.Equal(new DateTime(2024, 3, 5), ultima!.Value.Date);
    }

    /// <summary>
    /// El hueco que cerramos: si el PMS nunca cerró la estancia, con el criterio
    /// anterior (solo FSalida) esta persona jamás calificaba y sus imágenes de
    /// INE se quedaban en disco para siempre.
    /// </summary>
    [Fact]
    public void Una_estancia_que_nunca_se_cerro_usa_la_fecha_de_ingreso()
    {
        var ultima = RetentionPolicy.CalcularUltimaActividad(
            fechaAlta: Delphi(new DateTime(2019, 5, 1)),
            ultimaModificacion: Delphi(new DateTime(2019, 5, 1)),
            maxFIngreso: Delphi(new DateTime(2019, 5, 2)),
            maxFSalida: null);

        Assert.Equal(new DateTime(2019, 5, 2), ultima!.Value.Date);
        Assert.True(RetentionPolicy.EsCandidato(ultima, Limite));
    }

    /// <summary>
    /// El otro hueco: alguien capturado en el kiosko a quien el PMS nunca le
    /// asignó una estancia (se arrepintió y no se hospedó). No tiene ninguna
    /// fila en Estancias, pero sus imágenes sí están en disco.
    /// </summary>
    [Fact]
    public void Una_persona_sin_estancias_usa_su_fecha_de_alta()
    {
        var ultima = RetentionPolicy.CalcularUltimaActividad(
            fechaAlta: Delphi(new DateTime(2020, 2, 20)),
            ultimaModificacion: null,
            maxFIngreso: null,
            maxFSalida: null);

        Assert.Equal(new DateTime(2020, 2, 20), ultima!.Value.Date);
        Assert.True(RetentionPolicy.EsCandidato(ultima, Limite));
    }

    /// <summary>
    /// Un huésped recurrente reinicia el periodo: aunque su alta sea vieja, una
    /// visita reciente lo saca de la lista de candidatos.
    /// </summary>
    [Fact]
    public void Una_visita_reciente_reinicia_el_periodo()
    {
        var ultima = RetentionPolicy.CalcularUltimaActividad(
            fechaAlta: Delphi(new DateTime(2015, 1, 1)),
            ultimaModificacion: Delphi(new DateTime(2015, 1, 1)),
            maxFIngreso: Delphi(Hoy.AddDays(-10)),
            maxFSalida: Delphi(Hoy.AddDays(-8)));

        Assert.False(RetentionPolicy.EsCandidato(ultima, Limite));
    }

    /// <summary>
    /// Una columna numeric en 0 se convierte en 1899-12-30. Eso no es actividad
    /// real, es un campo que nunca se llenó, y tomarlo como fecha volvería
    /// candidato a todo el mundo.
    /// </summary>
    [Fact]
    public void Ignora_las_fechas_en_cero_de_columnas_nunca_llenadas()
    {
        var ultima = RetentionPolicy.CalcularUltimaActividad(
            fechaAlta: 0m,
            ultimaModificacion: 0m,
            maxFIngreso: Delphi(Hoy.AddDays(-30)),
            maxFSalida: 0m);

        Assert.Equal(Hoy.AddDays(-30).Date, ultima!.Value.Date);
        Assert.False(RetentionPolicy.EsCandidato(ultima, Limite));
    }

    [Fact]
    public void Sin_ninguna_fecha_utilizable_no_es_candidato()
    {
        var ultima = RetentionPolicy.CalcularUltimaActividad(null, null, null, null);

        Assert.Null(ultima);
        Assert.False(RetentionPolicy.EsCandidato(ultima, Limite));
    }

    [Fact]
    public void Justo_en_el_limite_todavia_no_es_candidato()
    {
        Assert.False(RetentionPolicy.EsCandidato(Limite, Limite));
        Assert.True(RetentionPolicy.EsCandidato(Limite.AddSeconds(-1), Limite));
    }
}
