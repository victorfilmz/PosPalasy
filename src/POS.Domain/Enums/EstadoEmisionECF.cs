using System;
using POS.Domain.Common;

namespace POS.Domain.Enums;

/// <summary>
/// Eje LOCAL (técnico) del ciclo de vida de un comprobante electrónico: qué se ha construido y qué
/// se ha intentado enviar. Es ortogonal al estado fiscal que reporta la DGII
/// (<see cref="EstadoFacturaElectronica"/>); separarlos evita que "en proceso" signifique a la vez
/// "recién creada" y "enviada y pendiente de resultado".
/// </summary>
public enum EstadoEmisionECF : int
{
    /// <summary>Comprobante registrado localmente; el XML aún no se ha construido.</summary>
    Creada = 0,

    /// <summary>XML construido y almacenado, sin validar contra el XSD oficial.</summary>
    XmlGenerado = 1,

    /// <summary>XML validado contra el XSD oficial del tipo de comprobante.</summary>
    XsdValidado = 2,

    /// <summary>XML firmado con XML-DSig y firma verificada antes de continuar.</summary>
    Firmada = 3,

    /// <summary>Comprobante colocado en la cola de emisión, pendiente de transmisión.</summary>
    Encolada = 4,

    /// <summary>Transmisión iniciada hacia la DGII.</summary>
    Enviada = 5,

    /// <summary>La DGII confirmó la recepción del documento (existe TrackId).</summary>
    ConfirmadaEnvio = 6,

    /// <summary>
    /// La transmisión terminó sin respuesta concluyente (timeout, corte de red). El documento pudo
    /// haber sido recibido: NO puede reenviarse como si fuera nuevo, debe resolverse consultando el
    /// TrackId.
    /// </summary>
    EnvioIncierto = 7,

    /// <summary>El XML no superó la validación XSD. Error permanente: no se firma ni se envía.</summary>
    ErrorXsd = 20,

    /// <summary>La firma digital falló o no pudo verificarse. Error permanente: no se envía.</summary>
    ErrorFirma = 21,

    /// <summary>Fallo recuperable (red, 5xx, 429): la cola reintentará con espera progresiva.</summary>
    ErrorTemporal = 22,

    /// <summary>Fallo no recuperable (credenciales, rechazo de validación): requiere intervención humana.</summary>
    ErrorPermanente = 23
}

/// <summary>
/// Máquina de estados del eje local de emisión.
/// </summary>
/// <remarks>
/// Reglas que impone:
/// <list type="bullet">
/// <item>Nunca se retrocede en la línea de emisión (no se puede "desfirmar" ni volver a crear).</item>
/// <item>Un estado terminal no se reabre: un comprobante confirmado no vuelve a estados de trabajo,
/// y un error permanente no entra en reintentos automáticos.</item>
/// <item>Un envío incierto solo se resuelve confirmando la recepción, nunca reenviando a ciegas.</item>
/// <item>Se admite avanzar sin pasar por un paso intermedio mientras ese paso no esté implementado
/// (validación XSD y firma); los pasos se incorporan sin cambiar la máquina.</item>
/// </list>
/// </remarks>
public static class EstadoEmisionECFTransiciones
{
    private static readonly EstadoEmisionECF[] LineaPrincipal =
    {
        EstadoEmisionECF.Creada,
        EstadoEmisionECF.XmlGenerado,
        EstadoEmisionECF.XsdValidado,
        EstadoEmisionECF.Firmada,
        EstadoEmisionECF.Encolada,
        EstadoEmisionECF.Enviada,
        EstadoEmisionECF.ConfirmadaEnvio
    };

    /// <summary>Indica si la transición está permitida por la máquina de estados.</summary>
    public static bool PuedeTransicionarA(this EstadoEmisionECF desde, EstadoEmisionECF hacia)
    {
        if (desde == hacia)
            return false;

        // Estados terminales: no se reabren bajo ninguna circunstancia.
        if (desde is EstadoEmisionECF.ConfirmadaEnvio
            or EstadoEmisionECF.ErrorXsd
            or EstadoEmisionECF.ErrorFirma
            or EstadoEmisionECF.ErrorPermanente)
        {
            return false;
        }

        // Cualquier estado de trabajo puede terminar en error.
        if (hacia is EstadoEmisionECF.ErrorXsd or EstadoEmisionECF.ErrorFirma or EstadoEmisionECF.ErrorPermanente)
            return true;

        // Un envío incierto no se reintenta: se confirma (o se marca como problema permanente).
        if (desde == EstadoEmisionECF.EnvioIncierto)
            return hacia is EstadoEmisionECF.ConfirmadaEnvio;

        // La incertidumbre nace de un intento de transmisión: puede declararla cualquier estado que
        // ya haya construido el documento, no solo el estado "Enviada".
        if (hacia == EstadoEmisionECF.EnvioIncierto)
            return desde == EstadoEmisionECF.ErrorTemporal
                || Indice(desde) >= Indice(EstadoEmisionECF.XmlGenerado);

        // Un fallo recuperable puede volver a encolarse o reintentarse.
        if (desde == EstadoEmisionECF.ErrorTemporal)
            return hacia is EstadoEmisionECF.Encolada or EstadoEmisionECF.Enviada;

        // Cualquier intento de transmisión puede fallar de forma recuperable.
        if (hacia == EstadoEmisionECF.ErrorTemporal)
            return Indice(desde) >= Indice(EstadoEmisionECF.XmlGenerado);

        // Avance en la línea principal: nunca hacia atrás.
        return Indice(hacia) > Indice(desde)
            && Indice(desde) >= 0
            && Indice(hacia) >= 0;
    }

    /// <summary>Valida la transición y devuelve el estado destino; lanza si es inválida.</summary>
    public static EstadoEmisionECF ValidarTransicion(this EstadoEmisionECF desde, EstadoEmisionECF hacia)
    {
        if (!desde.PuedeTransicionarA(hacia))
            throw new ReglaDeNegocioException(
                $"Transición de estado de emisión no permitida: {desde} → {hacia}.",
                "TRANSICION_EMISION_INVALIDA");

        return hacia;
    }

    private static int Indice(EstadoEmisionECF estado) => Array.IndexOf(LineaPrincipal, estado);
}
