using Nop.Core.Events;
using Nop.Core.Infrastructure;
using Nop.Services.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Nop.Services.Observability;


namespace Nop.Services.Events;

/// <summary>
/// Represents the event publisher implementation
/// </summary>
public partial class EventPublisher : IEventPublisher
{
    #region Methods

    /// <summary>
    /// Publish event to consumers
    /// </summary>
    /// <typeparam name="TEvent">Type of event</typeparam>
    /// <param name="event">Event object</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task PublishAsync<TEvent>(TEvent @event)
    {

        using var activity = NopTelemetry.ActivitySource.StartActivity($"event.publish.{typeof(TEvent).Name}");

        activity?.SetTag("event.type", typeof(TEvent).Name);

        //get all event consumers
        var consumers = EngineContext.Current.ResolveAll<IConsumer<TEvent>>().ToList();

        activity?.SetTag("event.consumers.count", consumers.Count);

        foreach (var consumer in consumers)
        {
            try
            {
                //try to handle published event
                await consumer.HandleEventAsync(@event);

                if (@event is IStopProcessingEvent { StopProcessing: true })
                    break;
            }
            catch (Exception exception)
            {
                //log error, we put in to nested try-catch to prevent possible cyclic (if some error occurs)
                try
                {

                    activity?.AddEvent(new ActivityEvent("consumer_error",tags: new ActivityTagsCollection
                        {
                            new("consumer.type", consumer.GetType().Name),
                            new("error.message", exception.Message)
                        }));
                    

                    var logger = EngineContext.Current.Resolve<ILogger>();
                    if (logger == null)
                        return;

                    await logger.ErrorAsync(exception.Message, exception);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    #endregion
}