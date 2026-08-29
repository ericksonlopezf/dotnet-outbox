FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["samples/Sample.OrderService/Sample.OrderService.csproj", "samples/Sample.OrderService/"]
COPY ["src/EricksonLopez.Outbox/EricksonLopez.Outbox.csproj", "src/EricksonLopez.Outbox/"]
COPY ["src/EricksonLopez.Outbox.Dapper/EricksonLopez.Outbox.Dapper.csproj", "src/EricksonLopez.Outbox.Dapper/"]
COPY ["src/EricksonLopez.Outbox.Storage/EricksonLopez.Outbox.Storage.csproj", "src/EricksonLopez.Outbox.Storage/"]
COPY ["src/EricksonLopez.Outbox.Brokers/EricksonLopez.Outbox.Brokers.csproj", "src/EricksonLopez.Outbox.Brokers/"]
COPY ["src/EricksonLopez.Outbox.EntityFrameworkCore/EricksonLopez.Outbox.EntityFrameworkCore.csproj", "src/EricksonLopez.Outbox.EntityFrameworkCore/"]
COPY ["src/EricksonLopez.Outbox.SourceGenerators/EricksonLopez.Outbox.SourceGenerators.csproj", "src/EricksonLopez.Outbox.SourceGenerators/"]
RUN dotnet restore "samples/Sample.OrderService/Sample.OrderService.csproj"

COPY . .
WORKDIR "/src/samples/Sample.OrderService"
RUN dotnet build "Sample.OrderService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Sample.OrderService.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Sample.OrderService.dll"]
