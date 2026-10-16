using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Appointments;

/// <summary>No exception detail leaves the service.</summary>
public sealed class ErrorInterceptor(ILogger<ErrorInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "gRPC call {Method} failed", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "An internal error occurred."));
        }
    }
}

public sealed class AppointmentsGrpcService(AppointmentsDbContext db) : AppointmentsApi.AppointmentsApiBase
{
    public override async Task<AppointmentReply> GetAppointment(AppointmentQuery request, ServerCallContext context)
    {
        var appointment = await db.Appointments.FindAsync([request.Id], context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"No appointment {request.Id}."));
        return new AppointmentReply { Id = appointment.Id, PatientId = appointment.PatientId };
    }
}
