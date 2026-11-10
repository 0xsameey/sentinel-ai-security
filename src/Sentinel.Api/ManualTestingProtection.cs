using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Sentinel.Api;

// Opt-in local manual testing: never read/create/replace profile key files.
internal static class ManualTestingProtection
{
    public static void Configure(IServiceCollection services)
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        var repository = new MemoryRepository();
        services.PostConfigure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = repository;
            options.XmlEncryptor = null;
        });
    }
    private sealed class MemoryRepository : IXmlRepository
    {
        private readonly ConcurrentQueue<XElement> elements = new();
        public IReadOnlyCollection<XElement> GetAllElements() => elements.Select(x => new XElement(x)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => elements.Enqueue(new XElement(element));
    }
}
