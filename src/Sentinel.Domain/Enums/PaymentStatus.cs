using System;
using System.Collections.Generic;
using System.Text;

namespace Sentinel.Domain.Enums
{
    public enum PaymentStatus
    {
        Authorized,
        Executing,
        Completed,
        Failed
    }
}
