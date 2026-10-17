FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ClinicReminders.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# ICU: SqlClient refuses to connect in globalization-invariant mode, and the Arabic SMS needs real cultures.
# tzdata: the Asia/Riyadh zone (the code falls back to a fixed UTC+3 without it, but keep the real data).
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ClinicReminders.dll"]
