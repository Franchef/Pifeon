using Microsoft.AspNetCore.Mvc.Testing;

namespace Pifeon.IntegrationTests;

// Fixture condivisa per evitare di riavviare il server ad ogni singolo test
public class ServerFixture : WebApplicationFactory<Program>
{
    // Qui puoi sovrascrivere o mockare configurazioni se necessario
}
