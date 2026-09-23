# IFEOPredatorPatcher

<div align="center">

In-memory bootstrapper and plugin loader for Acer NitroSense & PredatorSense.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)]()

</div>

---

## Overview

**IFEOPredatorPatcher** is a lightweight in-memory bootstrapper that intercepts Acer NitroSense and PredatorSense apps on launch (via AppX and IFEO hooks) and dynamically applies runtime plugins without modifying signed binaries on disk.

It comes bundled with **`OpenPredatorPlugin`**, which routes the official Acer UI directly to the [OpenPredator](https://github.com/KRW/OpenPredator) named pipe backend and bypasses proprietary OEM driver checks.

---

## Features

- **100% In-Memory**: Patches bytecode in RAM via Mono.Cecil on process startup, keeping `WindowsApps` signatures intact.
- **Universal Hooking**: Intercepts Start Menu shortcuts, laptop keyboard keys, and classic Win32 launches.
- **Fast-Boot Cache**: Caches patched assemblies in `%AppData%\IFEOPredatorPatcher\cache\` for instant startup (~15 ms).
- **Native Taskbar Grouping**: Preserves the official app icon and taskbar grouping.
- **Plugin System**: Drop custom `.dll` plugins into `%AppData%\IFEOPredatorPatcher\plugins\` to modify the app pipeline.

---

## Bundled Plugin: `OpenPredatorPlugin`

- Injects service checks for `OpenPredator`, `OpenPredatorService`, or fallback `PSSvc`.
- Bypasses missing OEM background service blocks in `Startup` and `LoadingPage`.
- Connects the official GUI seamlessly to the [OpenPredator](https://github.com/KRW/OpenPredator) hardware control backend.

---

## Quick Start

1. Download the latest release package.
2. Run **`IFEOPredatorPatcher.exe`**, select your detected app, and click **Install**.
3. Place plugins in `%AppData%\IFEOPredatorPatcher\plugins\` (or click **Plugins...**).
4. Launch NitroSense normally from your Start Menu or laptop hotkey!

---

## Writing Plugins

Create a `.NET Framework 4.8` Class Library referencing `Mono.Cecil`:

```csharp
using Mono.Cecil;

namespace MyPlugin
{
    public class CustomHook
    {
        public static void Patch(AssemblyDefinition assembly, dynamic context)
        {
            context.Log($"Hooking target: {context.TargetPath}");
            // Modify assembly with Mono.Cecil...
        }
    }
}
```

Drop the compiled `.dll` into `%AppData%\IFEOPredatorPatcher\plugins\`.

---

## License

Licensed under the [MIT License](LICENSE).  
Acer, Nitro, Predator, PredatorSense, and NitroSense are trademarks of Acer Inc. OpenPredator is an independent open-source project and is not affiliated with Acer Inc.
