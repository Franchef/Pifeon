using System;
using System.Collections.Generic;
using System.Text;

namespace Pifeon.Core.Configuration;

public static class PifeonConfigurationResolver
{
    public const string EnvSignalingUrl = "PIFEON_SIGNALING_URL";
    public const string DefaultSignalingUrl = "wss://signaling.pifeon.io/ws/pairing";

    /// <summary>
    /// Risolve l'URL finale del server di segnalazione applicando la gerarchia di priorità:
    /// 1. Parametro passato da CLI o input GUI
    /// 2. Variabile d'ambiente (PIFEON_SIGNALING_URL)
    /// 3. Configurazione appsettings.json
    /// 4. URL predefinito di fallback
    /// </summary>
    public static string ResolveSignalingUrl(string? cliOrGuiParamUrl = null, string? appSettingsUrl = null)
    {
        // 1. Parametro da riga di comando (CLI) o input GUI (Priorità massima)
        if (!string.IsNullOrWhiteSpace(cliOrGuiParamUrl))
        {
            return cliOrGuiParamUrl.Trim();
        }

        // 2. Variabile d'ambiente
        string? envUrl = Environment.GetEnvironmentVariable(EnvSignalingUrl);
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl.Trim();
        }

        // 3. Valore da appsettings.json
        if (!string.IsNullOrWhiteSpace(appSettingsUrl))
        {
            return appSettingsUrl.Trim();
        }

        // 4. Default di fallback
        return DefaultSignalingUrl;
    }
}
