namespace CoHAnalytics.Services.Diagnostics;

/// <summary>
/// Invokes multicast event subscribers individually so one fault cannot starve later handlers.
/// Ordering and synchronous semantics match a direct multicast <c>Invoke</c>.
/// </summary>
internal static class DiagnosticEventSubscriberDispatch
{
    public static void InvokeOrdered<TEventArgs>(
        MulticastDelegate? multicast,
        object sender,
        TEventArgs args,
        Action<string, string>? onSubscriberFault = null)
        where TEventArgs : EventArgs
    {
        if (multicast is null)
        {
            return;
        }

        foreach (var handler in multicast.GetInvocationList())
        {
            try
            {
                ((EventHandler<TEventArgs>)handler)(sender, args);
            }
            catch (Exception exception)
            {
                onSubscriberFault?.Invoke(
                    DescribeSubscriber(handler),
                    exception.GetType().FullName ?? exception.GetType().Name);
            }
        }
    }

    internal static string DescribeSubscriber(Delegate handler)
    {
        var method = handler.Method;
        var declaringType = method.DeclaringType?.FullName
            ?? method.DeclaringType?.Name
            ?? "UnknownType";
        return $"{declaringType}.{method.Name}";
    }
}
