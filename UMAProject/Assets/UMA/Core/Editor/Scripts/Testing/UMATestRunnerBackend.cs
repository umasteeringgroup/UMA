using System;

namespace UMA.Editors
{
    public interface IUMATestRunnerBackend
    {
        void RunEditModeCategory(string categoryName);
    }

    public static class UMATestRunnerBackend
    {
        private static IUMATestRunnerBackend backend;

        public static bool IsAvailable => backend != null;

        public static void Register(IUMATestRunnerBackend implementation)
        {
            backend = implementation ?? throw new ArgumentNullException(nameof(implementation));
        }

        public static bool TryRunEditModeCategory(string categoryName, out string errorMessage)
        {
            if (backend == null)
            {
                errorMessage = "Unity Test Framework is not installed.";
                return false;
            }

            backend.RunEditModeCategory(categoryName);
            errorMessage = null;
            return true;
        }
    }
}
