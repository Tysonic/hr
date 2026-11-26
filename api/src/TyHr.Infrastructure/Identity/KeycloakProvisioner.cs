using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Tenants;

namespace TyHr.Infrastructure.Identity;

public class KeycloakOptions
{
    /// <summary>Keycloak's base URL as the API reaches it, e.g. http://localhost:8082.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8082";
    public string Realm { get; set; } = "Tycount";

    /// <summary>Confidential client with the realm-management manage-users role (deploy/keycloak/setup.sh).</summary>
    public string ProvisionerClientId { get; set; } = "tyhr-provisioner";
    public string? ProvisionerSecret { get; set; }

    /// <summary>The client holding TyHR's roles (client roles), as in deploy/keycloak/tyhr-clients.json.</summary>
    public string ApiClientId { get; set; } = "tyhr-api";

    /// <summary>Where the set-password email sends the user afterwards.</summary>
    public string WebClientId { get; set; } = "tyhr-web";
    public string WebUrl { get; set; } = "http://localhost:5174/";
}

/// <summary>
/// Gives company users TyHR access through the Keycloak admin REST API, in the realm TyHR shares with Tycount. A new
/// user is created with the admin-only "tenant" attribute (their token's tenant claim) and a tyhr-api client role, and
/// emailed a link to set a password; if a later step fails they are deleted again, so a failed invite leaves nothing
/// behind. Someone who already has an account for the same company just gets the role.
/// </summary>
public class KeycloakProvisioner(HttpClient http, IOptions<KeycloakOptions> options, ILogger<KeycloakProvisioner> logger)
    : IIdentityProvisioner
{
    private static readonly string[] SetupActions = ["UPDATE_PASSWORD", "VERIFY_EMAIL"];
    private readonly KeycloakOptions _options = options.Value;

    private string Admin => $"{_options.BaseUrl.TrimEnd('/')}/admin/realms/{_options.Realm}";

    public async Task<string> InviteUserAsync(string tenant, string email, string firstName, string lastName, string role,
        CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        var (clientUuid, roleRepresentation) = await FindRoleAsync(role, token, cancellationToken);

        // The realm is shared with Tycount: someone who already signs in there for this company is given the TyHR role
        // on that same account, so they keep one password and sign in once for both apps.
        if (await FindUserAsync(email, token, cancellationToken) is { } existing)
        {
            var existingTenant = existing.TryGetProperty("attributes", out var attributes)
                && attributes.TryGetProperty(TenantSlug.ClaimType, out var values) && values.GetArrayLength() > 0
                    ? values[0].GetString()
                    : null;
            if (!string.Equals(TenantSlug.Normalize(existingTenant), tenant, StringComparison.Ordinal))
                throw new DomainException($"{email} already signs in for another company, so they can't be added to this one.");
            var existingId = existing.GetProperty("id").GetString()!;
            await AssignRoleAsync(existingId, clientUuid, roleRepresentation, token, cancellationToken);
            return existingId;
        }

        using var create = Request(HttpMethod.Post, $"{Admin}/users", token, new
        {
            username = email,
            email,
            firstName,
            lastName,
            enabled = true,
            emailVerified = false,
            attributes = new Dictionary<string, string[]> { [TenantSlug.ClaimType] = [tenant] },
            requiredActions = SetupActions,
        });
        using var created = await http.SendAsync(create, cancellationToken);
        if (created.StatusCode == HttpStatusCode.Conflict)
            throw new DomainException($"A user with the email {email} already exists.");
        await EnsureSuccessAsync(created, "create the user", cancellationToken);
        var userId = created.Headers.Location?.Segments.Last()
            ?? throw new InvalidOperationException("Keycloak did not return the new user's id.");

        try
        {
            await AssignRoleAsync(userId, clientUuid, roleRepresentation, token, cancellationToken);

            var query = $"client_id={Uri.EscapeDataString(_options.WebClientId)}&redirect_uri={Uri.EscapeDataString(_options.WebUrl)}";
            using var email_ = Request(HttpMethod.Put, $"{Admin}/users/{userId}/execute-actions-email?{query}", token, SetupActions);
            using var emailed = await http.SendAsync(email_, cancellationToken);
            await EnsureSuccessAsync(emailed, "send the set-password email", cancellationToken);
            return userId;
        }
        catch
        {
            await DeleteUserQuietlyAsync(userId, token);
            throw;
        }
    }

    /// <summary>The tyhr-api client's internal id and the representation of one of its roles.</summary>
    private async Task<(string ClientUuid, JsonElement Role)> FindRoleAsync(string role, string token, CancellationToken ct)
    {
        using var getClient = Request(HttpMethod.Get, $"{Admin}/clients?clientId={Uri.EscapeDataString(_options.ApiClientId)}", token);
        using var clientResponse = await http.SendAsync(getClient, ct);
        await EnsureSuccessAsync(clientResponse, $"find the client {_options.ApiClientId}", ct);
        var clients = await clientResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (clients.GetArrayLength() == 0)
            throw new InvalidOperationException($"Keycloak has no client {_options.ApiClientId}; run deploy/keycloak/setup.sh.");
        var clientUuid = clients[0].GetProperty("id").GetString()!;

        using var getRole = Request(HttpMethod.Get, $"{Admin}/clients/{clientUuid}/roles/{Uri.EscapeDataString(role)}", token);
        using var roleResponse = await http.SendAsync(getRole, ct);
        await EnsureSuccessAsync(roleResponse, $"find the role {role}", ct);
        return (clientUuid, await roleResponse.Content.ReadFromJsonAsync<JsonElement>(ct));
    }

    private async Task<JsonElement?> FindUserAsync(string email, string token, CancellationToken ct)
    {
        using var find = Request(HttpMethod.Get, $"{Admin}/users?email={Uri.EscapeDataString(email)}&exact=true", token);
        using var found = await http.SendAsync(find, ct);
        await EnsureSuccessAsync(found, "look up the user", ct);
        var users = await found.Content.ReadFromJsonAsync<JsonElement>(ct);
        return users.GetArrayLength() > 0 ? users[0].Clone() : null;
    }

    private async Task AssignRoleAsync(string userId, string clientUuid, JsonElement role, string token, CancellationToken ct)
    {
        using var assign = Request(HttpMethod.Post, $"{Admin}/users/{userId}/role-mappings/clients/{clientUuid}", token, new[] { role });
        using var assigned = await http.SendAsync(assign, ct);
        await EnsureSuccessAsync(assigned, $"give the user the role {role.GetProperty("name").GetString()}", ct);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.ProvisionerSecret))
            throw new InvalidOperationException(
                "Keycloak:ProvisionerSecret is not configured. deploy/keycloak/setup.sh sets it for local development; " +
                "elsewhere set the Keycloak__ProvisionerSecret environment variable.");

        using var response = await http.PostAsync(
            $"{_options.BaseUrl.TrimEnd('/')}/realms/{_options.Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ProvisionerClientId,
                ["client_secret"] = _options.ProvisionerSecret,
            }), ct);
        await EnsureSuccessAsync(response, "sign in to Keycloak", ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task DeleteUserQuietlyAsync(string userId, string token)
    {
        try
        {
            using var delete = Request(HttpMethod.Delete, $"{Admin}/users/{userId}", token);
            using var _ = await http.SendAsync(delete);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not remove Keycloak user {UserId} after a failed invite; remove it by hand.", userId);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Keycloak refused to {What}: {Status} {Detail}", what, (int)response.StatusCode, detail);
        throw new InvalidOperationException($"Could not {what} in Keycloak ({(int)response.StatusCode}).");
    }
}
