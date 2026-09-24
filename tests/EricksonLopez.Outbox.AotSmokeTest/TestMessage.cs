// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.Outbox.Contracts;

namespace EricksonLopez.Outbox.AotSmokeTest;

[OutboxMessage("test.message.v1")]
public class TestMessage
{
    public string Id { get; set; } = "";
}
