using System.IO;
using System.Reflection;

namespace CholinanzasApp.Services;

/// <summary>
/// Los textos legales de la aplicación (términos y privacidad, avisos de software de terceros). Viven en la carpeta Legal
/// y se incrustan en el .exe; el instalador usa los mismos archivos, así que lo que se acepta al instalar y lo que se
/// muestra en la app es siempre el mismo texto.
/// </summary>
public static class LegalTexts
{
    /// <summary>
    /// Identifica la versión de Legal\Terminos.txt. Hay que cambiarla (con la fecha) cada vez que cambie el texto: así
    /// la aplicación vuelve a pedir la aceptación a quien había aceptado una versión anterior.
    /// </summary>
    public const string TermsVersion = "2026-10-20";

    public static string Terms() => Read("Terminos.txt");

    public static string ThirdPartyNotices() => Read("AvisosDeTerceros.txt");

    private static string Read(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Falta el texto incrustado «{resourceName}».");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().TrimStart('﻿'); // la marca UTF-8 del archivo no es parte del texto
    }
}
