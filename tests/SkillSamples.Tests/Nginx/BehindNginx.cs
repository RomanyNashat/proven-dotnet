using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Nginx;

public static class BehindNginx
{
    public const int MaxBodyBytes = 5 * 1024 * 1024;                  // = nginx client_max_body_size = ingress proxy-body-size
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);   // < nginx proxy_read_timeout

    // podCidr: the cluster's pod network, where the ingress and nginx pods live (ask SRE for it).
    public static WebApplicationBuilder ConfigureBehindNginx(this WebApplicationBuilder builder, string podCidr)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // By default only loopback is trusted, so behind a proxy in another pod the headers are
            // ignored: every request seems to come from the proxy, and the scheme is http. Trust the pod
            // network, and allow two hops (ingress -> nginx -> app). The first address that isn't a
            // trusted proxy is the client; anything a client wrote further left is ignored.
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(podCidr));
            o.ForwardLimit = 2;
        });

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = MaxBodyBytes;
            // Keep Kestrel's keep-alive (default 130 s) above nginx's upstream keepalive_timeout (60 s):
            // if Kestrel closes an idle connection first, nginx can send a request into it and return 502.
            k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(130);
        });

        // The app gives up before nginx does, so the caller gets the app's answer (and the work is
        // cancelled) instead of nginx's 504 page while the request keeps running.
        builder.Services.AddRequestTimeouts(o => o.DefaultPolicy = new() { Timeout = RequestTimeout, TimeoutStatusCode = (int)HttpStatusCode.GatewayTimeout });
        return builder;
    }

    // First in the pipeline, so everything after it sees the real client and scheme.
    public static WebApplication UseBehindNginx(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseRequestTimeouts();
        return app;
    }
}
