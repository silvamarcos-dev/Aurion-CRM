namespace Loteadora.Crm.SharedKernel.Domain;

/// <summary>Violação de regra de negócio (HTTP 422).</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Recurso inexistente OU fora do escopo do ator (HTTP 404, evita enumeração/IDOR).</summary>
public sealed class NotFoundException(string resource) : Exception($"{resource} não encontrado.")
{
    public string Resource { get; } = resource;
}

/// <summary>Ator autenticado sem a capability necessária (HTTP 403).</summary>
public sealed class ForbiddenException(string capability) : Exception($"Permissão necessária: {capability}.")
{
    public string Capability { get; } = capability;
}

/// <summary>Conflito de concorrência otimista (HTTP 409).</summary>
public sealed class ConcurrencyException(string message) : Exception(message);
