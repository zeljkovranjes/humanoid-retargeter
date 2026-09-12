Run these regressions without proprietary animation packs or editor dependencies:

```powershell
dotnet test tests/PosedLocomotion/PosedLocomotion.csproj
```

Synthetic FBX fixtures cover crouched reference poses, raised feet in static binds, vertical preservation during root-motion removal, centered in-place clips, and native DMX root orientation for Y-up and Z-up targets.
