using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed class ParserWorkerFactory(ParserManagerOptions options) : IParserWorkerFactory
{
    public IParserWorker Create(MonitoringContextId contextId) => new ParserWorker(contextId, options);
}
