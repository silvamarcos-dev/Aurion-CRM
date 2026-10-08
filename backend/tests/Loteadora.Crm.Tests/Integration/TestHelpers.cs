using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loteadora.Crm.Modules.Crm.Application;

namespace Loteadora.Crm.Tests.Integration;

internal static class TestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<Guid> CreateLeadAsync(this HttpClient client, string name, Guid? ownerUserId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/leads",
            new { fullName = name, phone = RandomPhone(), source = "Teste", ownerUserId }, Json);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    public static async Task<LeadDetailDto> GetLeadAsync(this HttpClient client, Guid id)
    {
        var response = await client.GetAsync(new Uri($"/api/v1/leads/{id}", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LeadDetailDto>(Json))!;
    }

    public static async Task<BoardDto> GetBoardAsync(this HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/v1/board", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardDto>(Json))!;
    }

    public static Task<HttpResponseMessage> MoveAsync(this HttpClient client, Guid leadId, Guid stageId, uint version, string? reason = null) =>
        client.PostAsJsonAsync($"/api/v1/leads/{leadId}/move", new { stageId, expectedVersion = version, reason }, Json);

    private static string RandomPhone() => "119" + Random.Shared.Next(10_000_000, 99_999_999);
}
