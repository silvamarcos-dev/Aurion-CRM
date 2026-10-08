using Loteadora.Crm.SharedKernel.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Loteadora.Crm.Api.Infrastructure;

/// <summary>Converte exceções em ProblemDetails. Erros inesperados não expõem detalhes internos.</summary>
public sealed class CrmExceptionHandler(IProblemDetailsService problemDetails, ILogger<CrmExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message, "not_found"),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message, "forbidden"),
            ConcurrencyException e => (StatusCodes.Status409Conflict, e.Message, "concurrency_conflict"),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Message, e.Code),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Requisição inválida.", "bad_request"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno.", "internal_error"),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Erro não tratado em {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code, ["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() },
            },
        });
    }
}
