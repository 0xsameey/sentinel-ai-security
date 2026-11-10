
using System.ComponentModel.DataAnnotations;

namespace Sentinel.Infrastructure.Near;

public sealed class NearOptions
{
    public const string SectionName = "Near";

    [Required]
    [Url]
    public string RpcUrl { get; set; } = string.Empty;

    [Required]
    public string ContractId { get; set; } = string.Empty;
}
