---
name: docker-build
description: Build, optimize, and validate a Docker image for a .NET microservice. Delegates to devops-engineer agent for Dockerfile creation or optimization.
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

Delegate to the **devops-engineer** agent.

## Docker Build Workflow

1. **Check** if Dockerfile exists. If not, create one:
   - Reference `skills/docker-dotnet/` for the 5-stage Dockerfile template
   - Use chiseled Ubuntu base for production (`noble-chiseled`)
   - Use `chiseled-extra` if Arabic/Saudi locale support is needed

2. **Validate** `.dockerignore` exists and excludes bin/, obj/, .git/, etc.

3. **Build** the image:
   ```bash
   docker build -t <service-name>:local -f src/<Service>/Dockerfile .
   ```

4. **Verify** image:
   - Check image size: `docker images <service-name>:local`
   - Run health check: `docker run --rm -p 8080:8080 <service-name>:local` then `curl http://localhost:8080/health/live`
   - Scan for vulnerabilities: `docker scout cves <service-name>:local`

5. **Optimize** if image is too large:
   - Ensure multi-stage build with separate build/runtime stages
   - Verify `.csproj`-first copy pattern for layer caching
   - Consider Alpine or chiseled images
   - Check for unnecessary files copied into the image
