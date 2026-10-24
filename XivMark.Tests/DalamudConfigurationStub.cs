// The configuration contract is all Configuration.cs needs; no game runtime is loaded by these tests.
namespace Dalamud.Configuration;
public interface IPluginConfiguration
{
    int Version { get; set; }
}
