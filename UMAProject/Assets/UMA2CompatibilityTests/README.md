# UMA2Compatibility tests

Install this optional companion after UMA2Compatibility and Unity Test Framework. Run the
**UMA2CompatibilityTests** fixture in Unity's EditMode Test Runner.

The tests build and validate the compatibility archive, compare its coverage and GUIDs with
the installed main legacy content tree, verify the current UMA release metadata and
dependency contract, check the Welcome row grouping and path resolution, and exercise
documentation registration and unregistration without deleting the guide. When Examples
is installed, its archive is also checked for separate ownership and its utility prefabs are checked for missing scripts. The main package is checked for references to optional Examples, including name-based recipe slot and overlay lookup. All eight legacy base races are generated as smoke tests; these tests also work with Examples absent.

The **UMA2FemaleBodyAreolaTests** fixture reads the HumanFemale body textures and torso slot and
checks that the areola is in one place: the dot in the skin mask lies on the breast apex of the mesh,
and the painted areola and the mask map's detail coverage dot lie on that dot.

The **UMA2SkinDetailUVTests** fixture checks that the 33 HumanFemale and HumanMale skin slots carry baked
detail coordinates in their fourth UV channel, with one detail size inside each slot, that the head's
texture layout itself is untouched, that the head mask maps show little detail on the nose, and that
both UMA 2 skin materials use one detail tiling.

Build artifacts are temporary files under `Library/UMA/CompatibilityBuildTests`. The tests
do not remove the installed legacy content or alter characters in the open scene. Documentation
registration is restored after its test. Remove this companion before removing the parent.
