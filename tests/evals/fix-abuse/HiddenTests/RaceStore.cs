using System.Reflection;
using VisitDesk;

namespace HiddenTests;

// Holds every store read a moment longer, so two bookings at once overlap on every round instead of now and
// then. A proxy rather than a class: it forwards whatever the store under test has, even members a run added.
public class RaceStore : DispatchProxy
{
    private IVisitStore _inner = null!;

    public static IVisitStore Around(IVisitStore inner)
    {
        var proxy = Create<IVisitStore, RaceStore>();
        ((RaceStore)(object)proxy)._inner = inner;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        try
        {
            return method!.Invoke(_inner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException);
            throw;
        }
        finally
        {
            if (method!.Name.StartsWith("Find", StringComparison.Ordinal) || method.Name.StartsWith("Visits", StringComparison.Ordinal))
                Thread.SpinWait(200_000);
        }
    }
}
