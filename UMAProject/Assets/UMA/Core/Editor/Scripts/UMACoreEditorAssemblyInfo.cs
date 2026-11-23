using System.Runtime.CompilerServices;

// The editor tests check internal save paths of the recipe inspectors (the working-copy owners and the
// incomplete-load lock) directly.
[assembly: InternalsVisibleTo("UMA_Core_Editor_Tests")]
