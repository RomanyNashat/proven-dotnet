using Microsoft.AspNetCore.Mvc;
using Referrals.Application;

namespace Referrals.Api;

[ApiController]
[Route("clinics")]
public sealed class Clinics(IReferralStore store) : ControllerBase
{
    [HttpGet("{id:int}/queue")]
    public async Task<IActionResult> Queue(int id, CancellationToken ct) => Ok(await store.QueueAsync(id, ct));
}
