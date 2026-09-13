using Dalamud.Configuration;
using SentinelProfiles.Models;

namespace SentinelProfiles;

[Serializable]
public sealed class Configuration : ProfileConfigurationData, IPluginConfiguration
{
}
