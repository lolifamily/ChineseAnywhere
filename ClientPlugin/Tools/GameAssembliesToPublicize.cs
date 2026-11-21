using System.Runtime.CompilerServices;

// Pulsar publicizes the game assemblies named here when it compiles the plugin from source.
// Same list as the Publicize items in ClientPlugin.csproj, which local builds use.
[assembly: IgnoresAccessChecksTo("Sandbox.Graphics")]
[assembly: IgnoresAccessChecksTo("VRage.Input")]
[assembly: IgnoresAccessChecksTo("VRage.Platform.Windows")]
[assembly: IgnoresAccessChecksTo("VRage.Render")]
[assembly: IgnoresAccessChecksTo("VRage.Render11")]
