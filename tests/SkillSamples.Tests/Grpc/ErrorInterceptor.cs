using FluentValidation;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace SkillSamples.GrpcSamples;

/// <summary>
/// Turns exceptions the service didn't expect into a status the client can act on, without their text.
/// An RpcException is the service's own answer (NotFound, InvalidArgument) and passes through untouched.
/// A cancelled call says why: DeadlineExceeded if the deadline passed, Cancelled if the client gave up.
/// Left alone it reaches the client as Unknown; mapped to Cancelled every time, it hides the deadline.
/// </summary>
public sealed class ErrorInterceptor(ILogger<ErrorInterceptor> logger, TimeProvider time) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            throw Map(ex, context);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            throw Map(ex, context);
        }
    }

    private RpcException Map(Exception ex, ServerCallContext context)
    {
        if (ex is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            return context.Deadline <= time.GetUtcNow().UtcDateTime
                ? new RpcException(new Status(StatusCode.DeadlineExceeded, "The deadline passed."))
                : new RpcException(new Status(StatusCode.Cancelled, "The call was cancelled."));
        }

        if (ex is ValidationException validation)
        {
            var errors = string.Join("; ", validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            return new RpcException(new Status(StatusCode.InvalidArgument, errors));
        }

        // The exception's message can carry patient data: it goes to the log, never to the caller.
        logger.LogError(ex, "Unhandled error in {Method}", context.Method);
        return new RpcException(new Status(StatusCode.Internal, "An internal error occurred."));
    }
}
