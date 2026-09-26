using UnityEngine.Scripting;

// Required for every adapter: nothing in a scene references it directly (it registers itself at start-up),
// so without this, IL2CPP code stripping removes the whole assembly from device builds (see ADR-0003).
[assembly: AlwaysLinkAssembly]
