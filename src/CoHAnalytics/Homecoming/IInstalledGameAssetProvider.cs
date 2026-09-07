using System.Windows.Media;

namespace CoHAnalytics.Homecoming;

public interface IInstalledGameAssetProvider
{
    ImageSource? TryResolve(string? iconIdentity);
}
