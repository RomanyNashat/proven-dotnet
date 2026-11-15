using Microsoft.AspNetCore.Mvc;
using Referrals.Application;

namespace Referrals.Api;

[ApiController]
[Route("referrals")]
public sealed class Referrals(ReferralDesk desk) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateReferralRequest request, [FromHeader(Name = "X-Doctor-Id")] int doctorId, CancellationToken ct)
    {
        var result = await desk.CreateAsync(doctorId, request.PatientId, request.ClinicId, request.Reason, request.Note, ct);
        return result.Outcome switch
        {
            CreateOutcome.Created => Created($"/referrals/{result.Referral!.Id}", result.Referral),
            CreateOutcome.AlreadyOpen => Ok(result.Referral),
            CreateOutcome.TooManyToday => StatusCode(StatusCodes.Status429TooManyRequests),
            _ => NotFound(),
        };
    }

    [HttpPost("{id:int}/accept")]
    public async Task<IActionResult> Accept(int id, CancellationToken ct) =>
        await desk.AcceptAsync(id, ct) ? NoContent() : Conflict();
}
