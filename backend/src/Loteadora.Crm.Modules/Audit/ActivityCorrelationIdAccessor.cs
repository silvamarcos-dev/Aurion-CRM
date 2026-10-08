using System.Diagnostics;

namespace Loteadora.Crm.Modules.Audit;

/// <summary>Correlação via trace do OpenTelemetry/Activity (HTTP e jobs).</summary>
internal sealed class ActivityCorrelationIdAccessor : ICorrelationIdAccessor
{
    public string? CorrelationId => Activity.Current?.TraceId.ToString();
}
