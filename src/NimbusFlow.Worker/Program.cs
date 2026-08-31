using NimbusFlow.Application.Diagnostics;
using NimbusFlow.Application.Jobs;
using NimbusFlow.Infrastructure;
using NimbusFlow.Worker;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WorkflowCatalog>();
builder.Services.AddScoped<IJobMessageHandler, JobMessageHandler>();
builder.Services.AddOptions<WorkflowExecutionSettings>()
    .Bind(builder.Configuration.GetSection(WorkflowExecutionSettings.SectionName))
    .Validate(settings => settings.StepDelayMilliseconds >= 0, "Step delay cannot be negative.")
    .Validate(settings => settings.RetryBaseDelaySeconds >= 1, "Retry delay must be at least one second.")
    .Validate(settings => settings.ExecutionLeaseSeconds >= 30, "Execution lease must be at least 30 seconds.")
    .ValidateOnStart();

builder.Services
    .AddNimbusFlowInfrastructure(builder.Configuration)
    .AddOutboxDispatcher()
    .AddServiceBusJobReceiver();

var exportTelemetry = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("NimbusFlow.Worker"))
    .WithTracing(tracing =>
    {
        tracing.AddSource(NimbusFlowDiagnostics.ActivitySourceName).AddHttpClientInstrumentation();
        if (exportTelemetry) tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(NimbusFlowDiagnostics.MeterName).AddRuntimeInstrumentation();
        if (exportTelemetry) metrics.AddOtlpExporter();
    });

var host = builder.Build();
await host.RunAsync();
