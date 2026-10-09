FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["eldenRingApi/eldenRingApi.csproj", "eldenRingApi/"]
RUN dotnet restore "eldenRingApi/eldenRingApi.csproj"

COPY . .
WORKDIR "/src/eldenRingApi"
RUN dotnet publish "eldenRingApi.csproj" --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Render injects PORT at runtime. Falls back to 8080 for local containers.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet eldenRingApi.dll"]
