using System.Windows.Media;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Homecoming;

public interface IEnhancementIconCompositor
{
    ImageSource? TryCompose(EnhancementIconCompositionRequest request);
}
