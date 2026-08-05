namespace SchId.Api.Models;

/// <summary>Datos que manda la app Android, ya extraídos del PDF417 del reverso de la INE.</summary>
public class RegistroIneRequest
{
    public string Curp { get; set; } = "";
    public string? Nombre { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Nacionalidad { get; set; }
    public int? Edad { get; set; }
    public string? Residencia { get; set; }
}
