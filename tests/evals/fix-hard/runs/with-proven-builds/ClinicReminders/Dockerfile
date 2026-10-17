FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ClinicReminders.csproj Directory.Build.props ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# Alpine ships without ICU and tzdata. SqlClient refuses to connect in globalization-invariant mode, the
# Arabic SMS needs the ar-SA culture, and Asia/Riyadh needs tzdata (RiyadhTime has a fixed UTC+3 fallback).
RUN apk add --no-cache icu-libs icu-data-full tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ClinicReminders.dll"]
