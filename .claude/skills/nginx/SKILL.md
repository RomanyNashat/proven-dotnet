---
name: nginx
description: nginx with .NET services — which layer an error came from (ingress or the service's nginx), the 413/502/504/499/client-IP/CORS/gzip/stale-cache playbook, aligning limits and timeouts with Kestrel, ForwardedHeaders, static-file services, asking SRE. Configs tested in CI.
version: 1.0.0
---

# nginx

nginx is everywhere here: the ingress in front of every service, nginx in front of some services, and
services that are only nginx serving files. Most "the API is broken" reports that end in a 413, 502 or
504 are a disagreement between these layers, not a bug in the code.

The configs and the .NET code in this skill are tested in CI against a real nginx
(`tests/SkillSamples.Tests/Nginx`): what the static site refuses to serve, cache and charset headers,
the client IP and scheme the app sees, which layer answers a body that's too big, and which layer
answers a slow request.

## 1. Two layers, two owners

```
client ──> ingress-nginx ──> [ nginx in front of the service | nginx serving files ] ──> Kestrel
           SRE owns it:       the service owns it: config in the service's repo
           Ingress annotations
```

**Which layer answered?**
- An nginx error page is HTML with `nginx` in the body. The app answers with ProblemDetails JSON.
- The ingress's error page and the service nginx's look the same. If the request isn't in the service
  nginx's access log, the ingress answered it: ask SRE for the ingress log at that time.
- `$upstream_status` in the service nginx's log is empty when nginx answered by itself (a 413, a denied
  path) and holds the app's status otherwise. See the log format in §4.

How the config reaches a pod depends on your pipeline, which allows pre- and post-build scripts
but no Dockerfile in the repo. Copy what an existing nginx service does rather than inventing a path.

## 2. Playbook

| You see | Usually | Fix | Who |
|---|---|---|---|
| **413** | The body is over the smallest of three limits: ingress `proxy-body-size` (default 1m), nginx `client_max_body_size` (default **1m**), Kestrel `MaxRequestBodySize` (30 MB) | Set all three to the same value, on purpose | Ingress: SRE. nginx and Kestrel: you |
| **502** | Kestrel closed an idle keep-alive connection that nginx then reused; or the pod was stopping and still got traffic; or the app crashed | Kestrel `KeepAliveTimeout` above nginx's upstream `keepalive_timeout` (§3); a `preStop` sleep so endpoints update before shutdown (`kubernetes-dotnet`); check restarts (`production-diagnostics`) | You |
| **504** | The app didn't answer within `proxy_read_timeout` (default 60 s) | The app's own timeout, shorter than nginx's, and handlers that honour `RequestAborted` (§3, tested). A request that's slow by nature becomes `202 Accepted` plus a job, not a longer timeout | You; ingress timeouts: SRE |
| **499** in nginx's log | The client gave up first (the mobile app's own timeout) | Find what's slow (`production-diagnostics`); the app sees an aborted request | You |
| **Wrong client IP**, or `http` in generated links | `ForwardedHeaders` trusts only loopback by default, so behind a proxy pod it ignores the headers (tested) | §3: trust the pod network, two hops | You; the pod CIDR from SRE |
| **CORS error, header present** | Two layers add `Access-Control-Allow-Origin` (ingress `enable-cors` or nginx, plus the app's `UseCors`); browsers reject two values | One layer only: the app for APIs, nginx for static files | You / SRE |
| **No gzip, or garbled responses** | `gzip_types` doesn't list `application/json`; or the app and nginx both compress | Compress at one layer | You |
| **Old data after a change** | `Cache-Control` too long, or a cache in between (CDN, `proxy_cache`, the app's own) | Short `max-age` with `ETag` for content that changes; a new file name for content that must change at once | You |

## 3. The .NET side

<!-- sample: tests/SkillSamples.Tests/Nginx/BehindNginx.cs -->
```csharp
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
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.ConfigureBehindNginx(builder.Configuration["Cluster:PodCidr"]!);
var app = builder.Build();
app.UseBehindNginx();   // before authentication, rate limiting and anything that reads the client IP or scheme
```

What the tests show:
- **Defaults behind a proxy pod:** the app sees nginx's IP as the client and `http` as the scheme. Rate
  limits treat every user as one, audit logs record the proxy, and OIDC callback URLs say `http`.
- **Configured:** the app sees the real client and `https`.
- **A client that writes its own `X-Forwarded-For`** isn't believed: the app takes the first address
  that isn't a trusted proxy, counting from the right.
- **A slow request:** with the app's timeout shorter than nginx's, the caller gets the app's answer at 1 s
  and the work is cancelled. Without it, nginx answers 504 at its own timeout and closes the connection,
  and only a handler that passes `RequestAborted` on actually stops.

.NET 8 and 9 use `KnownNetworks` (with `Microsoft.AspNetCore.HttpOverrides.IPNetwork`); .NET 10 marks it
obsolete in favour of `KnownIPNetworks` (with `System.Net.IPNetwork`).

## 4. nginx in front of a service

<!-- sample: tests/SkillSamples.Tests/Nginx/conf/proxy.conf -->
```nginx
# nginx in front of a .NET service. Each limit lines up with the app and the ingress:
#   body size:  ingress proxy-body-size = client_max_body_size = Kestrel MaxRequestBodySize
#   timeouts:   app request timeout < proxy_read_timeout <= ingress proxy-read-timeout
#   keep-alive: Kestrel's KeepAliveTimeout (130 s) > nginx's upstream keepalive_timeout (60 s)
upstream api {
    server 127.0.0.1:5099;
    keepalive 16;                       # reuse connections to Kestrel instead of opening one per request
}

server {
    listen 8089;
    server_name _;
    server_tokens off;
    client_max_body_size 5m;

    location / {
        proxy_pass http://api;
        proxy_http_version 1.1;         # upstream keep-alive needs HTTP/1.1...
        proxy_set_header Connection ""; # ...and an empty Connection header, or nginx sends "close"
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;   # appends the caller to what the ingress sent
        proxy_set_header X-Forwarded-Proto $http_x_forwarded_proto;    # the scheme the ingress saw (TLS ends there)
        proxy_connect_timeout 5s;
        proxy_read_timeout 3s;          # tests use 3 s; in a service, a little above the app's own timeout
        proxy_send_timeout 3s;
    }
}
```

A log format that says which layer was slow:

```nginx
log_format timed '$remote_addr "$request" $status $body_bytes_sent '
                 'rt=$request_time upstream=$upstream_addr us=$upstream_status urt=$upstream_response_time';
access_log /dev/stdout timed;
```

- `rt` close to `urt`: the app was slow.
- `rt` far above `urt`: the client was slow (a big upload on a weak mobile connection).
- `us` empty: nginx answered without calling the app.
- Never log `$request_body` or the query string of endpoints that carry personal data.

## 5. nginx serving files (banners, lookup lists)

<!-- sample: tests/SkillSamples.Tests/Nginx/conf/banners.conf -->
```nginx
# Public, read-only JSON for the app (banners, lookup lists). The files live in the service's repo, so
# root points at the content folder only: never the repo root, where .git, appsettings and CI files sit.
server {
    listen 8088;
    server_name _;
    root /srv/repo/banners;

    server_tokens off;                  # no nginx version in headers or error pages
    autoindex off;                      # no directory listings
    default_type application/json;
    charset utf-8;
    charset_types application/json;     # Arabic text: the Content-Type says UTF-8

    gzip on;
    gzip_types application/json;
    gzip_min_length 256;

    location ~ /\. {
        return 404;                     # .git, .env, .gitignore: never served, even inside the folder
    }

    location / {
        limit_except GET HEAD {
            deny all;                   # read-only
        }

        try_files $uri =404;
        etag on;

        # add_header in a location drops every add_header from the server block, so all of them live
        # here. "always" adds them to 304 and error responses too.
        add_header Cache-Control "public, max-age=60, stale-while-revalidate=300" always;   # a change shows within a minute
        add_header Access-Control-Allow-Origin "*" always;   # public data: any origin may read it
        add_header X-Content-Type-Options "nosniff" always;
    }
}
```

Tested against a folder laid out like a real repo (content beside `.git`, `.env` and `appsettings.json`):
none of those are served, directories don't list, `..` tricks don't escape, POST is refused, Arabic
JSON comes back as UTF-8, a repeat request with the ETag gets a 304, and larger files are gzipped.

**"It's public, so there's no risk."** The content may be public; three things around it aren't:
1. **What else is in the folder.** When the files live in the service's repo, `root` must be the
   content folder, never the repo root, or `.git` (with remotes and history) and config files are
   downloadable. The dotfile rule above is the second line of defence.
2. **Who can change what every user sees.** Anyone who can merge to that repo changes the app's home
   screen. Keep the files under the same review as code.
3. **What the app does with the content.** If the JSON carries image URLs or deep links, the app follows
   them. Validate the files in the pipeline (a JSON schema check in a post-build script): a malformed
   file shouldn't reach production, where it can break the screen for every user at once.

## 6. Asking SRE about the ingress

The ingress is configured by annotations on the service's Ingress. Ask for a change with the numbers
and the reason:

| Annotation | What it sets |
|---|---|
| `nginx.ingress.kubernetes.io/proxy-body-size` | Largest request body (match nginx and Kestrel) |
| `nginx.ingress.kubernetes.io/proxy-read-timeout` / `proxy-send-timeout` | Seconds to wait for the service (above the app's own timeout) |
| `nginx.ingress.kubernetes.io/enable-cors` | Leave off when the app handles CORS |

Whether the ingress passes the client IP on (`use-forwarded-headers`, `compute-full-forwarded-for`) is
controller-wide configuration: ask how it's set rather than asking to change it.

```text
Service: <name>, namespace <ns>
Seen: 413 on POST /attachments for files over 1 MB since <date>
Need: proxy-body-size "5m" on its Ingress (nginx and the app already allow 5 MB)
Why: users upload lab reports up to 5 MB
```

## 7. Review checklist
- A body limit, a read timeout or a keep-alive setting changed in one layer without the other two.
- `UseForwardedHeaders` missing, after middleware that reads the client IP, or with default trust behind
  a proxy pod.
- A handler doing slow I/O without passing `HttpContext.RequestAborted` (or the endpoint's token) on.
- CORS configured in both the app and nginx or the ingress.
- A static-file `root` that is the repo root, `autoindex on`, or no rule for dotfiles.
- `add_header` in a `location` while expecting the server block's headers to still apply.
