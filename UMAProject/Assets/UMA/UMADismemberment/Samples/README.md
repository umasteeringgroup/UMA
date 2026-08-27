# Dismemberment sample

`Scene/Example.unity` retains the original sample references while using the UMA 3 runtime component. The UI and callback scripts demonstrate the compatibility API and the rich multi-renderer completion event.

The supplied `SliceFill` material uses the package's unlit cross-pipeline cap shader. For project-specific lit results, create a cap material for each render pipeline and assign it through **Pipeline Slice Fill Overrides** on `UmaDismemberment`.
