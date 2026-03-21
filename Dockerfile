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
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "BuzzKeepr.API.dll"]
