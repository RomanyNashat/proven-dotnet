# Use runtime image (not aspnet — no web server needed)
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/DailyReport.Job/DailyReport.Job.csproj", "src/DailyReport.Job/"]
COPY ["Directory.Build.props", "Directory.Packages.props", "NuGet.Config", "./"]
RUN dotnet restore "src/DailyReport.Job/DailyReport.Job.csproj"
COPY . .
RUN dotnet publish "src/DailyReport.Job/DailyReport.Job.csproj" \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DailyReport.Job.dll"]
