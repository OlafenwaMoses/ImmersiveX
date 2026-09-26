using UnityEngine.Scripting;

// Nothing in a scene references a platform adapter directly: it registers itself at start-up.
// Without this, IL2CPP code stripping removes the whole assembly from device builds.
[assembly: AlwaysLinkAssembly]
