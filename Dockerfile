FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["BuzzKeepr.Presentation/BuzzKeepr.Presentation.csproj", "BuzzKeepr.Presentation/"]
COPY ["BuzzKeepr.Application/BuzzKeepr.Application.csproj", "BuzzKeepr.Application/"]
COPY ["BuzzKeepr.Domain/BuzzKeepr.Domain.csproj", "BuzzKeepr.Domain/"]
COPY ["BuzzKeepr.Infrastructure/BuzzKeepr.Infrastructure.csproj", "BuzzKeepr.Infrastructure/"]

RUN dotnet restore "BuzzKeepr.Presentation/BuzzKeepr.Presentation.csproj"

COPY . .

RUN dotnet publish "BuzzKeepr.Presentation/BuzzKeepr.Presentation.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Npgsql probes libgssapi during connection setup when the Postgres server advertises
# GSSAPI encryption (Neon's default connection strings include gssencmode=prefer).
# The aspnet runtime image is slim and doesn't ship Kerberos libs — without this the
# connection aborts before any SQL runs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "BuzzKeepr.API.dll"]
