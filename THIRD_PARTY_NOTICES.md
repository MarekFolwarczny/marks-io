# Third-party notices

The [MIT license](LICENSE) covers this repository's original application source and artwork. The dependencies below retain their own licenses. Keep this notice and the referenced license files with redistributed builds.

## Snap7

- Component: `Native/snap7.dll`, distributed as a separate Windows x64 native DLL beside the application EXE.
- Project and source: [Snap7](https://snap7.sourceforge.net/) by Davide Nardella.
- License: GNU Lesser General Public License version 3. See `Native/lgpl-3.0.txt` and its base license `Native/gpl-3.0.txt` (published beside the EXE as `lgpl-3.0.txt` and `gpl-3.0.txt`).
- Version and corresponding source: the bundled DLL matches the [official Windows x64 DLL at commit `30f37da`](https://github.com/davenardella/snap7/blob/30f37da3114024a71ba93f7fd855c680b97a406f/build/bin/win64/snap7.dll), whose [source and build files are in the same snapshot](https://github.com/davenardella/snap7/tree/30f37da3114024a71ba93f7fd855c680b97a406f). Upstream `HISTORY.txt` calls this release 1.4.3. The local DLL has SHA-256 `a9d8c3395056c40006dfae0056b90baf4f3a2b51ac642f28d1d1117e9ba426a2` and Git blob SHA-1 `44fb17abcdabaca87e2ae71671bb9c46659f048c`, identical to that upstream file. No local changes were made to the DLL.
- The DLL remains separate so users can replace it with a compatible build. Keep access to the [pinned source archive](https://github.com/davenardella/snap7/archive/30f37da3114024a71ba93f7fd855c680b97a406f.zip) available with any distributed binary package, as required by LGPL-3.0.

## OPC Foundation .NET Standard stack

- Packages: `OPCFoundation.NetStandard.Opc.Ua.Client`, `.Server`, `.Configuration`, `.Core`, `.Security.Certificates` and `.Types`, version `1.5.378.182`.
- Copyright: OPC Foundation, Inc.
- License: OPC Foundation MIT License 1.00. See `Native/opc-foundation-mit.txt` (published as `opc-foundation-mit.txt`).
- Source: [UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard).

## Other NuGet and runtime components

| Component | Version | License notice |
| --- | --- | --- |
| BitFaster.Caching | 2.6.0 | MIT, Alex Peck; `Native/bitfaster-mit.txt` |
| Newtonsoft.Json | 13.0.4 | MIT, James Newton-King; `Native/newtonsoft-mit.txt` |
| Microsoft.Extensions.DependencyInjection, `.Abstractions`, `.Logging`, `.Logging.Abstractions`, `.Options`, `.Primitives` | 10.0.8 | MIT, .NET Foundation and contributors; `Native/dotnet-mit.txt` |
| System.Collections.Immutable, System.Diagnostics.DiagnosticSource, System.Formats.Asn1, System.IO.Pipelines, System.Text.Encodings.Web, System.Text.Json | 10.0.8 | MIT, .NET Foundation and contributors; `Native/dotnet-mit.txt` |
| Bundled .NET 8 runtime in a self-contained publish | .NET 8 | .NET Foundation and contributors; `Native/dotnet-mit.txt`. Additional runtime notices: [dotnet/runtime](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT). |

The table reflects the packages resolved for this project's current target. Recheck it after changing package versions or the .NET target.
