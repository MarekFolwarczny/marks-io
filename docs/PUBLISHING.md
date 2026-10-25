# Publishing Marks IO

This repository contains source code and the separately replaceable `Native/snap7.dll`. Keep generated builds, runtime data and certificate profiles out of Git. The root `.gitignore` excludes `dist/`, build directories and common certificate/key formats; still review every staged file before a commit.

## Before the first public release

1. Keep the pinned Snap7 source archive linked in [third-party notices](../THIRD_PARTY_NOTICES.md) available wherever the DLL is distributed. The bundled DLL was matched by Git blob SHA-1 to the official source snapshot; recheck the hash if replacing the DLL. Attach a copy of that source archive to a binary release so recipients can obtain the exact source even if the upstream link changes.
2. Run `dotnet list MarksIO.csproj package --vulnerable --include-transitive` with network access, then run the three test projects listed in [README](../README.md). The network tests use loopback ports `1102` and `14840`.
3. Publish the Windows x64 self-contained application with the command in README. Check that `dist/` contains the EXE, `snap7.dll`, `LICENSE`, `THIRD_PARTY_NOTICES.md` and every license text listed there.
4. Open the EXE on Windows and check English and Czech mode selection and at least one server/client connection. Test against the actual PLC or SCADA product separately if claiming its compatibility.
5. Review `git status --short` and the staged diff for private keys, certificates, credentials, customer names, IP addresses and generated build outputs. Do not include a real OPC UA profile in the repository or release ZIP.

The `v1.0.0` tag publishes source code and documentation. It does not include a binary release. If distributing binaries later, package the **entire** `dist/` folder and the pinned Snap7 source archive as a GitHub Release asset. Keep the copyright and third-party notices in both the source repository and release package.
