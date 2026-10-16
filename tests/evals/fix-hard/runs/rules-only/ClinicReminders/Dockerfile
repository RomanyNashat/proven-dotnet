FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ClinicReminders.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# ICU: Microsoft.Data.SqlClient refuses to connect in globalization-invariant mode.
# tzdata: Asia/Riyadh for the reminder text and the job's schedule.
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ClinicReminders.dll"]
