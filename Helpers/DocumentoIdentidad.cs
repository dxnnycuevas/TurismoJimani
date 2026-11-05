namespace AppDonnyCuevas20210074.Helpers;

// Tipos de cuenta y documentos del registro. Una persona usa cédula o pasaporte; una empresa de viajes, RNC.
public static class DocumentoIdentidad
{
    public const string CuentaPersona = "Persona";
    public const string CuentaEmpresa = "Empresa";

    public const string Cedula = "Cedula";
    public const string Pasaporte = "Pasaporte";
    public const string Rnc = "RNC";

    // Se guarda sin guiones ni espacios para que "001-1234567-8" y "00112345678" cuenten como el mismo documento
    public static string Normalizar(string? numero) =>
        new string((numero ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    // Mensaje de error, o null si el número es válido para ese tipo de documento
    public static string? Validar(string? tipo, string numero) => tipo switch
    {
        Cedula when numero.Length != 11 || !numero.All(char.IsAsciiDigit) => "La cédula debe tener 11 dígitos.",
        Rnc when numero.Length != 9 || !numero.All(char.IsAsciiDigit) => "El RNC debe tener 9 dígitos.",
        Pasaporte when numero.Length is < 6 or > 20 || !numero.All(char.IsAsciiLetterOrDigit) => "El pasaporte debe tener entre 6 y 20 letras o números.",
        Cedula or Rnc or Pasaporte => null,
        _ => "Selecciona un tipo de documento válido."
    };
}
