Run the compatibility, animation setup and CopyPinky regressions without s&box or external models:

```powershell
dotnet test tests/CitizenAnimationSetup/CitizenAnimationSetup.csproj
```

The editor's `UiSmokeGate` additionally checks both shipped armatures through VMDL and FBX imports, button enablement, complete animation libraries, the exact assigned graphs, and graph-driven locomotion.
