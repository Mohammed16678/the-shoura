FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Shoura.API/*.csproj ./
RUN dotnet restore
COPY backend/Shoura.API/ ./
RUN dotnet publish Shoura.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Shoura.API.dll"]
