# UMA2Compatibility tests

Install this optional companion after UMA2Compatibility and Unity Test Framework. Run the
**UMA2CompatibilityTests** fixture in Unity's EditMode Test Runner.

The tests build and validate the compatibility archive, compare its coverage and GUIDs with
the installed main legacy content tree, verify the current UMA release metadata and
dependency contract, check the Welcome row grouping and path resolution, and exercise
documentation registration and unregistration without deleting the guide. When Examples
is installed, its archive is also checked for separate ownership and its utility prefabs are checked for missing scripts. The main package is checked for references to optional Examples, including name-based recipe slot and overlay lookup. All eight legacy base races are generated as smoke tests; these tests also work with Examples absent.

Build artifacts are temporary files under `Library/UMA/CompatibilityBuildTests`. The tests
do not remove the installed legacy content or alter characters in the open scene. Documentation
registration is restored after its test. Remove this companion before removing the parent.
