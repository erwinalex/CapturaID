using SchId.Shared;

namespace SchId.Api.Services;

/// <summary>
/// Regla de retención, aislada de la base de datos para poder probarla.
///
/// La regla de negocio original es "contar desde la última salida", porque un
/// huésped recurrente reinicia el periodo. El problema de aplicarla al pie de la
/// letra es que una persona SIN salida registrada nunca cumpliría la condición y
/// sus imágenes de INE se quedarían en disco para siempre — justo lo contrario
/// de lo que busca la minimización de datos. Y ahora eso es un caso real y
/// esperable: quien administra dbo.Estancias es el PMS, no esta API, así que
/// puede haber personas capturadas en el kiosko a las que el PMS nunca les
/// asignó una estancia (un walk-in que se arrepintió), o estancias que quedaron
/// abiertas por un cierre que no se hizo.
///
/// Por eso se toma como referencia la señal de actividad MÁS RECIENTE de la
/// persona, venga de donde venga: su última salida, su última entrada, la última
/// modificación de su registro o su fecha de alta. Tomar el máximo es la opción
/// conservadora — nunca borra antes de tiempo — y a la vez cierra el hueco de
/// las imágenes que no se borraban nunca.
/// </summary>
public static class RetentionPolicy
{
    /// <summary>
    /// Momento más reciente en que hay constancia de actividad de esta persona.
    /// Devuelve null si no hay ninguna fecha utilizable, en cuyo caso no se toca
    /// nada (mejor conservar de más que borrar a ciegas).
    /// </summary>
    public static DateTime? CalcularUltimaActividad(
        decimal? fechaAlta,
        decimal? ultimaModificacion,
        decimal? maxFIngreso,
        decimal? maxFSalida)
    {
        DateTime? maximo = null;

        foreach (var valor in new[] { fechaAlta, ultimaModificacion, maxFIngreso, maxFSalida })
        {
            var fecha = DelphiDateTime.ToDateTime(valor);

            // Un 0 (o basura cercana a 0) en la columna numeric se convierte en
            // la época de Delphi, 1899-12-30. Eso no es una fecha real de
            // actividad, es un campo que nunca se llenó: ignorarlo.
            if (fecha is null || fecha.Value.Year < 1990)
            {
                continue;
            }

            if (maximo is null || fecha > maximo)
            {
                maximo = fecha;
            }
        }

        return maximo;
    }

    /// <summary>
    /// True si ya pasó el periodo de retención desde la última actividad y las
    /// imágenes de esta persona son candidatas a borrarse.
    /// </summary>
    public static bool EsCandidato(DateTime? ultimaActividad, DateTime limite)
    {
        return ultimaActividad is not null && ultimaActividad.Value < limite;
    }
}
