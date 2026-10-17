FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# SqlClient refuses to connect in invariant mode; the Riyadh time zone needs tzdata.
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ClinicReminders.dll"]
