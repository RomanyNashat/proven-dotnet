FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
COPY app/ /app
ENTRYPOINT ["dotnet", "/app/Clinic.Api.dll"]
