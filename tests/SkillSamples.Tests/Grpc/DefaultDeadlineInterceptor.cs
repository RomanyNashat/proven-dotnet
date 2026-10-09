using Grpc.Core;
using Grpc.Core.Interceptors;

namespace SkillSamples.GrpcSamples;

/// <summary>
/// Client side: a unary call with no deadline gets one. A gRPC call without a deadline waits forever,
/// and the server can't add one afterwards. Streams are left alone: a deadline would end them.
/// </summary>
public sealed class DefaultDeadlineInterceptor(TimeProvider time, TimeSpan timeout) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        if (context.Options.Deadline is null)
        {
            var options = context.Options.WithDeadline(time.GetUtcNow().UtcDateTime + timeout);
            context = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
        }

        return continuation(request, context);
    }
}
