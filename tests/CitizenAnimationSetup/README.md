Run the compatibility, complete animation metadata, graph copy/replacement, direction suggestion, additive reference and footstep regressions without s&box or external models:

```powershell
dotnet test tests/CitizenAnimationSetup/CitizenAnimationSetup.csproj
```

The editor's `UiSmokeGate` additionally checks both shipped armatures through VMDL and FBX imports, button enablement, complete animation libraries, the exact assigned graphs, editable graph compilation, successive stock replacements, and graph-driven locomotion.
