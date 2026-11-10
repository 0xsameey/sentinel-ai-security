using Sentinel.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sentinel.Domain.Entities
{
    public sealed record Payment(
     string Id,
     string Recipient,
     string AmountYocto,
     [property: System.Text.Json.Serialization.JsonNumberHandling(
         System.Text.Json.Serialization.JsonNumberHandling.WriteAsString)]
     ulong ExpiresAtNs,
     PaymentStatus Status
 );
}
