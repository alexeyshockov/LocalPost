using System.Collections.Immutable;
using LocalPost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace LocalPost.SqsConsumer;

internal sealed class SqsConsumerService : INamedService
{
    public static SqsConsumerService Create(IServiceProvider provider, string name,
        MiddlewareStack<ConsumeContext> handlerStack)
    {
        var options = provider.GetOptions<Options>(name);

        var client = ActivatorUtilities.CreateInstance<QueueClient>(provider, options);
        var messageSource = new MessageSource(client);
        var reader = new BackgroundServiceSupervisor(messageSource);

        HandlerFactory<ConsumeContext> handlerFactory = handlerStack.Resolve;
        Handler<ConsumeContext> handler = ActivatorUtilities.CreateInstance<ScopedHandler<ConsumeContext>>(provider,
            name, handlerFactory).InvokeAsync;

        var consumer = new BackgroundQueue<ConsumeContext>.Consumer(messageSource, handler);
        var consumerGroup = new ConsumerGroup(consumer.Run, options.MaxConcurrency);

        return new SqsConsumerService(name, reader, consumerGroup);
    }

    public SqsConsumerService(string name, IBackgroundServiceSupervisor reader,
        IBackgroundServiceSupervisor consumerGroup)
    {
        Name = name;

        Reader = reader;
        _readerReadinessCheck = new IBackgroundServiceSupervisor.ReadinessCheck(reader);
        _readerLivenessCheck = new IBackgroundServiceSupervisor.LivenessCheck(reader);

        ConsumerGroup = consumerGroup;
        _consumerGroupReadinessCheck = new IBackgroundServiceSupervisor.ReadinessCheck(consumerGroup);
        _consumerGroupLivenessCheck = new IBackgroundServiceSupervisor.LivenessCheck(consumerGroup);
    }

    public string Name { get; }

    // Expose only the root supervisor to the host, to avoid deadlocks (.NET runtime handles background services
    // synchronously by default, so if consumers are stopped first, they will block the reader from completing the
    // channel).
//    public IHostedService Supervisor { get; }

    public IConcurrentHostedService Reader { get; }
    private readonly IHealthCheck _readerReadinessCheck;
    private readonly IHealthCheck _readerLivenessCheck;

    public IConcurrentHostedService ConsumerGroup { get; }
    private readonly IHealthCheck _consumerGroupReadinessCheck;
    private readonly IHealthCheck _consumerGroupLivenessCheck;

    public static HealthCheckRegistration QueueReadinessCheck(string name, HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default) => new(name,
        provider => provider.GetRequiredService<SqsConsumerService>(name)._readerReadinessCheck,
        failureStatus,
        tags);

    public static HealthCheckRegistration QueueLivenessCheck(string name, HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default) => new(name,
        provider => provider.GetRequiredService<SqsConsumerService>(name)._readerLivenessCheck,
        failureStatus,
        tags);

    public static HealthCheckRegistration ConsumerGroupReadinessCheck(string name, HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default) => new(name,
        provider => provider.GetRequiredService<SqsConsumerService>(name)._consumerGroupReadinessCheck,
        failureStatus,
        tags);

    public static HealthCheckRegistration ConsumerGroupLivenessCheck(string name, HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default) => new(name,
        provider => provider.GetRequiredService<SqsConsumerService>(name)._consumerGroupLivenessCheck,
        failureStatus,
        tags);
}
