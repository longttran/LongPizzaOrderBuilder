FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/backend/LongPizzaOrderBuilder.Worker/LongPizzaOrderBuilder.Worker.csproj src/backend/LongPizzaOrderBuilder.Worker/
RUN dotnet restore src/backend/LongPizzaOrderBuilder.Worker/LongPizzaOrderBuilder.Worker.csproj
COPY src/backend/LongPizzaOrderBuilder.Worker/ src/backend/LongPizzaOrderBuilder.Worker/
RUN dotnet publish src/backend/LongPizzaOrderBuilder.Worker/LongPizzaOrderBuilder.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LongPizzaOrderBuilder.Worker.dll"]
