namespace CoHAnalytics.Homecoming;

public sealed class HomecomingPiggException : Exception
{
    public HomecomingPiggException(string message)
        : base(message)
    {
    }

    public HomecomingPiggException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
