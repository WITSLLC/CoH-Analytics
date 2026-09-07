using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Pure, deterministic structural classifier for one already-framed raw parser event.</summary>
public interface IParserClassifier
{
    ParserEvent Classify(ParserRawEvent rawEvent);
}
