using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Coordinates one isolated incremental parser worker per eligible monitoring context.</summary>
public interface IParserManager
{
    ParserManagerSnapshot Current { get; }

    ParserClassificationSnapshot ClassificationCurrent { get; }

    event EventHandler<ParserManagerChangedEventArgs>? StateChanged;

    event EventHandler<ParserEventsAvailableEventArgs>? EventsAvailable;

    event EventHandler<ParserClassificationChangedEventArgs>? ClassificationChanged;

    event EventHandler<ParserEventsClassifiedEventArgs>? ClassifiedEventsAvailable;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    ParserManagerDiagnostics GetDiagnostics();

    ParserClassificationDiagnostics GetClassificationDiagnostics();
}
