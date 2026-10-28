FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/backend/LongPizzaOrderBuilder.Api/LongPizzaOrderBuilder.Api.csproj src/backend/LongPizzaOrderBuilder.Api/
RUN dotnet restore src/backend/LongPizzaOrderBuilder.Api/LongPizzaOrderBuilder.Api.csproj
COPY src/backend/ src/backend/
RUN dotnet publish src/backend/LongPizzaOrderBuilder.Api/LongPizzaOrderBuilder.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "LongPizzaOrderBuilder.Api.dll"]
