using SchId.Api.Data.Entities;
using SchId.Api.Models;
using SchId.Api.Services;

namespace SchId.Tests;

public class PersonaMergeTests
{
    private static Persona PersonaExistente() => new()
    {
        ID = 42,
        // Tal como los devuelve SQL Server desde columnas nchar: con relleno.
        Nombre = "JUAN PEREZ LOPEZ                    ",
        Direccion = "CALLE FALSA 123                     ",
        CURP = "PELJ800101HDFRPN01  ",
        Nacionalidad = "MEXICANA            ",
        Edad = 44
    };

    [Fact]
    public void Sin_diferencias_no_reporta_cambios()
    {
        var persona = PersonaExistente();

        var cambios = PersonaMerge.Aplicar(persona, new RegistroIneRequest
        {
            Curp = "PELJ800101HDFRPN01",
            Nombre = "JUAN PEREZ LOPEZ",
            Direccion = "CALLE FALSA 123",
            Nacionalidad = "MEXICANA",
            Edad = 44
        });

        Assert.Empty(cambios);
    }

    /// <summary>
    /// El caso que más fácil se rompe: si se comparara sin recortar el relleno
    /// de los nchar, cada captura reportaría todos los campos como cambiados y
    /// se escribiría en la base sin necesidad.
    /// </summary>
    [Fact]
    public void El_relleno_de_los_nchar_no_cuenta_como_cambio()
    {
        var persona = new Persona { Nombre = "ANA                 " };

        var cambios = PersonaMerge.Aplicar(persona, new RegistroIneRequest { Nombre = "ANA" });

        Assert.Empty(cambios);
    }

    [Fact]
    public void Reporta_y_aplica_solo_los_campos_que_cambiaron()
    {
        var persona = PersonaExistente();

        var cambios = PersonaMerge.Aplicar(persona, new RegistroIneRequest
        {
            Curp = "PELJ800101HDFRPN01",
            Nombre = "JUAN PEREZ LOPEZ",
            Direccion = "AVENIDA SIEMPRE VIVA 742",
            Edad = 45
        });

        Assert.Equal(new[] { nameof(Persona.Direccion), nameof(Persona.Edad) }, cambios);
        Assert.Equal("AVENIDA SIEMPRE VIVA 742", persona.Direccion);
        Assert.Equal(45, persona.Edad);
        Assert.Equal("JUAN PEREZ LOPEZ                    ", persona.Nombre);
    }

    /// <summary>
    /// Un PDF417 rayado o un escaneo malo hacen que algún campo llegue vacío. Si
    /// eso sobreescribiera, una captura defectuosa borraría datos buenos.
    /// </summary>
    [Fact]
    public void Un_campo_no_capturado_conserva_el_valor_anterior()
    {
        var persona = PersonaExistente();

        var cambios = PersonaMerge.Aplicar(persona, new RegistroIneRequest
        {
            Curp = "PELJ800101HDFRPN01",
            Nombre = null,
            Direccion = "   ",
            Edad = null
        });

        Assert.Empty(cambios);
        Assert.Equal("JUAN PEREZ LOPEZ                    ", persona.Nombre);
        Assert.Equal("CALLE FALSA 123                     ", persona.Direccion);
        Assert.Equal(44, persona.Edad);
    }

    [Fact]
    public void Llena_los_campos_que_estaban_vacios()
    {
        var persona = new Persona { ID = 7 };

        var cambios = PersonaMerge.Aplicar(persona, new RegistroIneRequest
        {
            Curp = "PELJ800101HDFRPN01",
            Nombre = "MARIA LOPEZ",
            Telefono = "5512345678"
        });

        Assert.Equal(new[] { nameof(Persona.Nombre), nameof(Persona.Telefono) }, cambios);
        Assert.Equal("MARIA LOPEZ", persona.Nombre);
    }

    [Theory]
    [InlineData("  pelj800101hdfrpn01  ", "PELJ800101HDFRPN01")]
    [InlineData("PELJ800101HDFRPN01", "PELJ800101HDFRPN01")]
    public void Normaliza_el_curp_a_mayusculas_sin_espacios(string entrada, string esperado)
    {
        Assert.Equal(esperado, PersonaMerge.NormalizarCurp(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("     ")]
    public void Un_curp_vacio_se_normaliza_a_null(string? entrada)
    {
        Assert.Null(PersonaMerge.NormalizarCurp(entrada));
    }
}
