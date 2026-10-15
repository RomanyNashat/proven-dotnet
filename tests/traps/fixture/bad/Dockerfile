FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
COPY app/ /app
ENTRYPOINT ["dotnet", "/app/Clinic.Api.dll"]
