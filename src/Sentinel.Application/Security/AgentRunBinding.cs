using System.Security.Cryptography;
using System.Text;
namespace Sentinel.Application.Security;

// Dedicated deterministic audit claim key gives one assessment binding per run,
// enforced by the existing PostgreSQL audit-event primary key.
public static class AgentRunBinding
{
    public static Guid Id(Guid runId) => new(SHA256.HashData(Encoding.UTF8.GetBytes("sentinel-agent-assessment:"+runId.ToString("D"))).AsSpan(0,16));
}
