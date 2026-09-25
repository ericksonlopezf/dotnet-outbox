FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["samples/Sample.OrderService/Sample.OrderService.csproj", "samples/Sample.OrderService/"]
COPY ["src/EricksonLopez.Outbox/EricksonLopez.Outbox.csproj", "src/EricksonLopez.Outbox/"]
COPY ["src/EricksonLopez.Outbox.Abstractions/EricksonLopez.Outbox.Abstractions.csproj", "src/EricksonLopez.Outbox.Abstractions/"]
COPY ["src/EricksonLopez.Inbox/EricksonLopez.Inbox.csproj", "src/EricksonLopez.Inbox/"]
COPY ["src/EricksonLopez.Inbox.Abstractions/EricksonLopez.Inbox.Abstractions.csproj", "src/EricksonLopez.Inbox.Abstractions/"]
COPY ["src/EricksonLopez.Outbox.Inbox/EricksonLopez.Outbox.Inbox.csproj", "src/EricksonLopez.Outbox.Inbox/"]
COPY ["src/EricksonLopez.Outbox.Inbox.AspNetCore/EricksonLopez.Outbox.Inbox.AspNetCore.csproj", "src/EricksonLopez.Outbox.Inbox.AspNetCore/"]
COPY ["src/EricksonLopez.Outbox.Inbox.Events/EricksonLopez.Outbox.Inbox.Events.csproj", "src/EricksonLopez.Outbox.Inbox.Events/"]
COPY ["src/EricksonLopez.Outbox.Events/EricksonLopez.Outbox.Events.csproj", "src/EricksonLopez.Outbox.Events/"]
COPY ["src/EricksonLopez.Outbox.MediatR/EricksonLopez.Outbox.MediatR.csproj", "src/EricksonLopez.Outbox.MediatR/"]
COPY ["src/EricksonLopez.Outbox.Mediator/EricksonLopez.Outbox.Mediator.csproj", "src/EricksonLopez.Outbox.Mediator/"]
COPY ["src/EricksonLopez.Outbox.Brighter/EricksonLopez.Outbox.Brighter.csproj", "src/EricksonLopez.Outbox.Brighter/"]
COPY ["src/EricksonLopez.Outbox.Dapr/EricksonLopez.Outbox.Dapr.csproj", "src/EricksonLopez.Outbox.Dapr/"]
COPY ["src/EricksonLopez.Outbox.MassTransit/EricksonLopez.Outbox.MassTransit.csproj", "src/EricksonLopez.Outbox.MassTransit/"]
COPY ["src/EricksonLopez.Outbox.NServiceBus/EricksonLopez.Outbox.NServiceBus.csproj", "src/EricksonLopez.Outbox.NServiceBus/"]
COPY ["src/EricksonLopez.Outbox.Rebus/EricksonLopez.Outbox.Rebus.csproj", "src/EricksonLopez.Outbox.Rebus/"]
COPY ["src/EricksonLopez.Outbox.Serialization.MessagePack/EricksonLopez.Outbox.Serialization.MessagePack.csproj", "src/EricksonLopez.Outbox.Serialization.MessagePack/"]
COPY ["src/EricksonLopez.Outbox.Serialization.Protobuf/EricksonLopez.Outbox.Serialization.Protobuf.csproj", "src/EricksonLopez.Outbox.Serialization.Protobuf/"]
COPY ["src/EricksonLopez.Outbox.Storage.MariaDb/EricksonLopez.Outbox.Storage.MariaDb.csproj", "src/EricksonLopez.Outbox.Storage.MariaDb/"]
COPY ["src/EricksonLopez.Outbox.Storage.MongoDb/EricksonLopez.Outbox.Storage.MongoDb.csproj", "src/EricksonLopez.Outbox.Storage.MongoDb/"]
COPY ["src/EricksonLopez.Outbox.Storage.MySql/EricksonLopez.Outbox.Storage.MySql.csproj", "src/EricksonLopez.Outbox.Storage.MySql/"]
COPY ["src/EricksonLopez.Outbox.Storage.Oracle/EricksonLopez.Outbox.Storage.Oracle.csproj", "src/EricksonLopez.Outbox.Storage.Oracle/"]
COPY ["src/EricksonLopez.Outbox.Storage.PostgreSql/EricksonLopez.Outbox.Storage.PostgreSql.csproj", "src/EricksonLopez.Outbox.Storage.PostgreSql/"]
COPY ["src/EricksonLopez.Outbox.Storage.SqlServer/EricksonLopez.Outbox.Storage.SqlServer.csproj", "src/EricksonLopez.Outbox.Storage.SqlServer/"]
COPY ["src/EricksonLopez.Outbox.Storage.Sqlite/EricksonLopez.Outbox.Storage.Sqlite.csproj", "src/EricksonLopez.Outbox.Storage.Sqlite/"]
COPY ["src/EricksonLopez.Outbox.Brokers.AwsSqs/EricksonLopez.Outbox.Brokers.AwsSqs.csproj", "src/EricksonLopez.Outbox.Brokers.AwsSqs/"]
COPY ["src/EricksonLopez.Outbox.Brokers.AzureEventHubs/EricksonLopez.Outbox.Brokers.AzureEventHubs.csproj", "src/EricksonLopez.Outbox.Brokers.AzureEventHubs/"]
COPY ["src/EricksonLopez.Outbox.Brokers.AzureServiceBus/EricksonLopez.Outbox.Brokers.AzureServiceBus.csproj", "src/EricksonLopez.Outbox.Brokers.AzureServiceBus/"]
COPY ["src/EricksonLopez.Outbox.Brokers.GooglePubSub/EricksonLopez.Outbox.Brokers.GooglePubSub.csproj", "src/EricksonLopez.Outbox.Brokers.GooglePubSub/"]
COPY ["src/EricksonLopez.Outbox.Brokers.Kafka/EricksonLopez.Outbox.Brokers.Kafka.csproj", "src/EricksonLopez.Outbox.Brokers.Kafka/"]
COPY ["src/EricksonLopez.Outbox.Brokers.Nats/EricksonLopez.Outbox.Brokers.Nats.csproj", "src/EricksonLopez.Outbox.Brokers.Nats/"]
COPY ["src/EricksonLopez.Outbox.Brokers.RabbitMQ/EricksonLopez.Outbox.Brokers.RabbitMQ.csproj", "src/EricksonLopez.Outbox.Brokers.RabbitMQ/"]
COPY ["src/EricksonLopez.Outbox.Brokers.RedisStreams/EricksonLopez.Outbox.Brokers.RedisStreams.csproj", "src/EricksonLopez.Outbox.Brokers.RedisStreams/"]
COPY ["src/EricksonLopez.Outbox.SourceGenerators/EricksonLopez.Outbox.SourceGenerators.csproj", "src/EricksonLopez.Outbox.SourceGenerators/"]
COPY ["src/EricksonLopez.Outbox.EntityFrameworkCore/EricksonLopez.Outbox.EntityFrameworkCore.csproj", "src/EricksonLopez.Outbox.EntityFrameworkCore/"]
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
