using System;
using System.Collections.Generic;
using System.Text;

namespace Sentinel.Application.DTOs
{
    public sealed record ContractConfigDto(
    string Owner,
    string Agent,
    bool Paused
);
}
