using SchId.Api.Data.Entities;
using SchId.Api.Models;

namespace SchId.Api.Services;

/// <summary>
/// Compara lo que se acaba de capturar de la INE contra el registro que ya
/// existe en dbo.Personas y aplica únicamente los campos que cambiaron.
///
/// Dos reglas importantes:
///
/// 1) Los campos de la tabla son nchar (longitud fija), así que SQL Server los
///    devuelve rellenados con espacios a la derecha. Comparar sin normalizar
///    haría que TODO se viera como "cambiado" en cada captura. Por eso se
///    compara y se guarda siempre el valor recortado.
///
/// 2) Un campo vacío o nulo en lo capturado significa "no se pudo leer de la
///    INE", NO "bórralo". Se conserva el valor anterior. Si no fuera así, un
///    escaneo con el PDF417 rayado o mal enfocado borraría datos buenos que ya
///    estaban en la base.
/// </summary>
public static class PersonaMerge
{
    /// <summary>
    /// Aplica sobre <paramref name="destino"/> los campos capturados que sean
    /// distintos a los que ya tenía. Devuelve los nombres de los campos que
    /// realmente cambiaron (lista vacía = el registro previo ya estaba al día).
    /// </summary>
    public static IReadOnlyList<string> Aplicar(Persona destino, RegistroIneRequest capturado)
    {
        var cambios = new List<string>();

        AplicarTexto(cambios, nameof(Persona.Nombre), capturado.Nombre,
            destino.Nombre, v => destino.Nombre = v);

        AplicarTexto(cambios, nameof(Persona.Direccion), capturado.Direccion,
            destino.Direccion, v => destino.Direccion = v);

        AplicarTexto(cambios, nameof(Persona.Telefono), capturado.Telefono,
            destino.Telefono, v => destino.Telefono = v);

        AplicarTexto(cambios, nameof(Persona.Nacionalidad), capturado.Nacionalidad,
            destino.Nacionalidad, v => destino.Nacionalidad = v);

        AplicarTexto(cambios, nameof(Persona.Residencia), capturado.Residencia,
            destino.Residencia, v => destino.Residencia = v);

        // La edad sí puede venir nula (la INE trae fecha de nacimiento, no edad,
        // y la app puede no calcularla). Nula = no capturada = conservar.
        if (capturado.Edad is not null && capturado.Edad != destino.Edad)
        {
            destino.Edad = capturado.Edad;
            cambios.Add(nameof(Persona.Edad));
        }

        return cambios;
    }

    /// <summary>
    /// Normaliza un CURP para comparar y guardar: sin espacios de relleno y en
    /// mayúsculas, que es como viene en la INE.
    /// </summary>
    public static string? NormalizarCurp(string? curp)
    {
        var limpio = Normalizar(curp);
        return limpio?.ToUpperInvariant();
    }

    /// <summary>
    /// Quita el relleno de los nchar y trata la cadena vacía como nula, para que
    /// "  " y null se consideren el mismo valor (ausente).
    /// </summary>
    public static string? Normalizar(string? valor)
    {
        if (valor is null)
        {
            return null;
        }

        var recortado = valor.Trim();
        return recortado.Length == 0 ? null : recortado;
    }

    private static void AplicarTexto(
        List<string> cambios,
        string campo,
        string? capturado,
        string? actual,
        Action<string?> asignar)
    {
        var nuevo = Normalizar(capturado);

        // No se capturó este campo: conservar lo que ya estaba (ver regla 2).
        if (nuevo is null)
        {
            return;
        }

        if (string.Equals(nuevo, Normalizar(actual), StringComparison.Ordinal))
        {
            return;
        }

        asignar(nuevo);
        cambios.Add(campo);
    }
}
