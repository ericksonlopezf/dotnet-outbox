## Description

Brief description of the changes in this PR.

## Type of Change

- [ ] 🐛 Bug fix (non-breaking change that fixes an issue)
- [ ] ✨ New feature (non-breaking change that adds functionality)
- [ ] 💥 Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] 📖 Documentation update
- [ ] ♻️ Refactoring (no functional changes)
- [ ] ⚡ Performance improvement
- [ ] 🔧 Build / CI / infrastructure change

## Affected Package(s)

- [ ] `EricksonLopez.Outbox` (Core)
- [ ] `EricksonLopez.Outbox.EntityFrameworkCore`
- [ ] Storage Providers (`SqlServer`, `Sqlite`, `PostgreSql`, `Oracle`, `MySql`)
- [ ] Broker Publishers (`RabbitMQ`, `Kafka`, `AwsSqs`, `AzureServiceBus`, `GooglePubSub`, `Nats`, `RedisStreams`)
- [ ] Integrations (`MassTransit`)
- [ ] `EricksonLopez.Outbox.SourceGenerators` / `Analyzers`
- [ ] Benchmarks / Tests

## Checklist

- [ ] My code follows the project's code standards
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] All new and existing tests pass (`dotnet test`)
- [ ] The mutation score has not decreased below the 95% threshold (`dotnet stryker`)
- [ ] My commits follow [Conventional Commits](https://www.conventionalcommits.org) format
- [ ] I have updated documentation as needed
- [ ] Zero-allocation guarantees are maintained for the dispatcher hot-path (GC Gen 0/1/2 = 0)

## Related Issues

Closes #<!-- issue number -->
