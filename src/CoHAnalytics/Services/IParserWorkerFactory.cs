using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public interface IParserWorkerFactory
{
    IParserWorker Create(MonitoringContextId contextId);
}
