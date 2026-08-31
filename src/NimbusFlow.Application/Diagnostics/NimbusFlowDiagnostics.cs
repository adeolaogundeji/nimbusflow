using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NimbusFlow.Application.Diagnostics;

public static class NimbusFlowDiagnostics
{
    public const string ActivitySourceName = "NimbusFlow.Workflows";
    public const string MeterName = "NimbusFlow.Workflows";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> JobsStarted = Meter.CreateCounter<long>("nimbusflow.jobs.started");
    public static readonly Counter<long> JobsSucceeded = Meter.CreateCounter<long>("nimbusflow.jobs.succeeded");
    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>("nimbusflow.jobs.failed");
    public static readonly Counter<long> RetriesScheduled = Meter.CreateCounter<long>("nimbusflow.jobs.retries_scheduled");
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>("nimbusflow.jobs.duration", "s");
}
