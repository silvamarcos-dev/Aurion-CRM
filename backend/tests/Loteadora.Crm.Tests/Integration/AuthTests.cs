using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Tests.Infrastructure;

namespace Loteadora.Crm.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(CrmApiFactory factory)
{
    private async Task<TokenPair> LoginTokensAsync(string email)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = factory.Password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPair>())!;
    }

    [Fact]
    public async Task Wrong_password_returns_generic_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "admin@a.test", password = "errada" });
        var unknown = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "naoexiste@a.test", password = "errada" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_failures()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "lockout@a.test", password = "errada" });

        var correct = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "lockout@a.test", password = factory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_reuse_revokes_whole_session()
    {
        var tokens = await LoginTokensAsync("corretor.sul@a.test");
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var rotated = (await first.Content.ReadFromJsonAsync<TokenPair>())!;

        // Reuso do token antigo => detecção de roubo => família revogada.
        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);

        // O access token emitido para a sessão revogada também deixa de valer.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Logout_invalidates_access_token_immediately()
    {
        var tokens = await LoginTokensAsync("corretor.norte2@a.test");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative))).StatusCode);

        await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var tokens = await LoginTokensAsync("admin@a.test");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative))).StatusCode);
    }
}
