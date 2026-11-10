using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Sentinel.IntegrationTests;

// Test/demo composition only: do not read, replace or delete the user's key files.
public static class TestDataProtection
{
    public static IDataProtectionBuilder UseInMemoryTestKeyRepository(this IDataProtectionBuilder builder)
    {
        var repository = new MemoryRepository();
        builder.Services.PostConfigure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = repository;
            // Nothing is persisted; avoid profile/DPAPI dependencies in the local fixture.
            options.XmlEncryptor = null;
        });
        return builder;
    }

    private sealed class MemoryRepository : IXmlRepository
    {
        private readonly ConcurrentQueue<XElement> elements = new();
        public IReadOnlyCollection<XElement> GetAllElements() => elements.Select(x => new XElement(x)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => elements.Enqueue(new XElement(element));
    }
}
