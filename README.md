# HermesOnDisk

It's a dumb, stupid game archive system, but FAST.

## HOW TO USE

```csharp
HermesOnDisk.Root = new DirectoryInfo(Application.persistentDataPath);

// Read
bool doorOpen = HermesOnDisk.Boolean[2];
float hp = HermesOnDisk.Float[0];
string name = HermesOnDisk.String[0];

// Write
HermesOnDisk.Boolean[0] = true;
HermesOnDisk.Float[0] = 100f;
HermesOnDisk.String[0] = "Peter";

// Store & Cleanup
HermesOnDisk.Store();
HermesOnDisk.GC();
```
