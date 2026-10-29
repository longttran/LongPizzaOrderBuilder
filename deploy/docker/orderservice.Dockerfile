FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/backend/LongPizzaOrderBuilder.Pricing/LongPizzaOrderBuilder.Pricing.csproj src/backend/LongPizzaOrderBuilder.Pricing/
COPY src/backend/LongPizzaOrderBuilder.OrderService/LongPizzaOrderBuilder.OrderService.csproj src/backend/LongPizzaOrderBuilder.OrderService/
RUN dotnet restore src/backend/LongPizzaOrderBuilder.OrderService/LongPizzaOrderBuilder.OrderService.csproj
COPY src/backend/ src/backend/
RUN dotnet publish src/backend/LongPizzaOrderBuilder.OrderService/LongPizzaOrderBuilder.OrderService.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8090
EXPOSE 8090
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "LongPizzaOrderBuilder.OrderService.dll"]
