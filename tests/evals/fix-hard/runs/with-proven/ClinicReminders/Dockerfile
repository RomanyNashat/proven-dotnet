FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
# The project file first, so the restore layer is cached until a package changes.
COPY ["ClinicReminders.csproj", "."]
RUN dotnet restore "ClinicReminders.csproj"
COPY . .
RUN dotnet publish "ClinicReminders.csproj" -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# The Alpine image has no ICU and no tzdata, and turns globalization-invariant mode on. SqlClient refuses to
# connect in invariant mode, the Arabic SMS text needs ar-SA (icu-data-full: icu-libs alone has only a few
# locales), and Asia/Riyadh needs tzdata.
RUN apk add --no-cache icu-libs icu-data-full tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ClinicReminders.dll"]
