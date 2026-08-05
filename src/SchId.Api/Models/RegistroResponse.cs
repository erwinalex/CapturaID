namespace SchId.Api.Models;

public enum ResultadoRegistro
{
    /// <summary>No existía nadie con ese CURP: se dio de alta.</summary>
    Creado,

    /// <summary>Ya existía y algún dato de la INE venía distinto: se actualizó el registro previo.</summary>
    Actualizado,

    /// <summary>Ya existía y los datos coincidían: no se tocó la fila (solo se reemplazaron las imágenes).</summary>
    SinCambios
}

/// <summary>
/// Lo que la API le contesta al kiosko.
///
/// A propósito NO incluye datos personales del huésped. El kiosko solo muestra
/// lo que acaba de capturar en pantalla, así que no necesita que le devuelvan
/// nombre ni dirección del registro previo — y no mandárselos evita que esos
/// datos queden en la memoria o el caché de un equipo que está en un mostrador,
/// a la vista de quien pase.
/// </summary>
public class RegistroResponse
{
    /// <summary>Id de la persona en dbo.Personas. Es el dato que el PMS usa para asignarle una estancia.</summary>
    public long Id { get; set; }

    public ResultadoRegistro Resultado { get; set; }

    /// <summary>
    /// Nombres de los campos que cambiaron respecto al registro previo. Vacío en
    /// altas nuevas y cuando no hubo cambios. Sirve para auditoría y para
    /// depurar el parseo del PDF417 sin exponer los valores en sí.
    /// </summary>
    public IReadOnlyList<string> CamposActualizados { get; set; } = Array.Empty<string>();

    public bool ImagenFrenteGuardada { get; set; }

    public bool ImagenReversoGuardada { get; set; }
}
