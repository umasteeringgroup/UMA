using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    internal sealed class UMAPluginAction
    {
        internal readonly MethodInfo Method;
        internal readonly GUIContent Content;
        internal readonly int Order;
        internal readonly bool SupportsMultipleTargets;
        internal readonly Action<Editor> Invoke;
        internal readonly Func<Editor, bool> Validate;
        private readonly UMAPluginAttribute[] registrations;

        internal UMAPluginAction(MethodInfo method, UMAPluginAttribute[] attributes,
            Action<Editor> action, Func<Editor, bool> validator)
        {
            Method = method;
            registrations = attributes;
            Content = new GUIContent(attributes[0].Label, attributes[0].Tooltip);
            Order = attributes[0].Order;
            SupportsMultipleTargets = attributes[0].SupportsMultipleTargets;
            Invoke = action;
            Validate = validator;
        }

        internal bool Matches(Type type) => registrations.Any(a =>
            a.IncludeDerived ? a.TargetType.IsAssignableFrom(type) : a.TargetType == type);

        internal bool Matches(Object[] targets) => targets.Length > 0 &&
            targets.All(t => t != null && Matches(t.GetType()));

        internal bool IsEnabled(Editor editor)
        {
            if (editor == null || !Matches(editor.targets) ||
                (editor.targets.Length > 1 && !SupportsMultipleTargets)) return false;
            try { return Validate == null || Validate(editor); }
            catch (Exception exception)
            {
                UMAPluginDiagnostics.Report("validator", Method, exception);
                return false;
            }
        }
    }

    internal static class UMAPluginRegistry
    {
        private static UMAPluginAction[] actions;
        private static readonly Dictionary<Type, UMAPluginAction[]> candidates = new();
        internal static int Generation { get; private set; }

        internal static UMAPluginAction[] Actions => actions ??=
            Discover(TypeCache.GetMethodsWithAttribute<UMAPluginAttribute>());

        internal static UMAPluginAction[] Match(Object[] targets)
        {
            if (targets == null || targets.Length == 0 || targets[0] == null)
                return Array.Empty<UMAPluginAction>();
            Type type = targets[0].GetType();
            if (!candidates.TryGetValue(type, out var found))
                candidates[type] = found = Actions.Where(a => a.Matches(type)).ToArray();
            return found.Where(a => a.Matches(targets)).ToArray();
        }

        internal static void Reset()
        {
            actions = null;
            candidates.Clear();
            Generation++;
        }

        internal static UMAPluginAction[] Discover(IEnumerable<MethodInfo> methods)
        {
            var result = new List<UMAPluginAction>();
            foreach (MethodInfo method in methods.Distinct())
            {
                try { result.Add(Create(method)); }
                catch (Exception exception)
                {
                    UMAPluginDiagnostics.Report("registration", method, exception);
                }
            }
            return result.OrderBy(a => a.Order)
                .ThenBy(a => a.Content.text, StringComparer.Ordinal)
                .ThenBy(a => a.Method.DeclaringType.Assembly.FullName, StringComparer.Ordinal)
                .ThenBy(a => a.Method.DeclaringType.FullName, StringComparer.Ordinal)
                .ThenBy(a => a.Method.Name, StringComparer.Ordinal).ToArray();
        }

        internal static UMAPluginAction Create(MethodInfo method)
        {
            if (!HasSignature(method, typeof(void)) ||
                method.IsDefined(typeof(AsyncStateMachineAttribute), false))
                throw new ArgumentException("An action must be a non-generic, synchronous static void Method(Editor).");
            var attributes = method.GetCustomAttributes<UMAPluginAttribute>(false).ToArray();
            if (attributes.Length == 0) throw new ArgumentException("Missing UMAPluginAttribute.");
            UMAPluginAttribute first = attributes[0];
            foreach (var attribute in attributes)
            {
                Type type = attribute.TargetType;
                if (type == null || type.ContainsGenericParameters ||
                    (!type.IsInterface && !typeof(Object).IsAssignableFrom(type)) ||
                    (type.IsInterface && !attribute.IncludeDerived))
                    throw new ArgumentException("Target must be a closed Unity object type or an assignable interface.");
                if (string.IsNullOrWhiteSpace(attribute.Label))
                    throw new ArgumentException("The action label must not be empty.");
                if (attribute.Label != first.Label || attribute.Tooltip != first.Tooltip ||
                    attribute.Order != first.Order ||
                    attribute.SupportsMultipleTargets != first.SupportsMultipleTargets ||
                    attribute.ValidateMethod != first.ValidateMethod)
                    throw new ArgumentException("Repeated attributes must use identical action metadata.");
            }

            Func<Editor, bool> validator = null;
            if (!string.IsNullOrEmpty(first.ValidateMethod))
            {
                var matches = method.DeclaringType.GetMethods(BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == first.ValidateMethod && HasSignature(m, typeof(bool))).ToArray();
                if (matches.Length != 1)
                    throw new ArgumentException("ValidateMethod must identify one static bool Method(Editor) on the declaring class.");
                validator = (Func<Editor, bool>)matches[0].CreateDelegate(typeof(Func<Editor, bool>));
            }
            return new UMAPluginAction(method, attributes,
                (Action<Editor>)method.CreateDelegate(typeof(Action<Editor>)), validator);
        }

        private static bool HasSignature(MethodInfo method, Type returnType)
        {
            if (method == null || !method.IsStatic || method.IsGenericMethod ||
                method.DeclaringType == null || method.DeclaringType.ContainsGenericParameters ||
                method.ReturnType != returnType) return false;
            var parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(Editor);
        }
    }

    internal static class UMAPluginDiagnostics
    {
        private static readonly HashSet<string> reported = new();

        internal static void Report(string phase, object source, Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            if (exception is ExitGUIException) ExceptionDispatchInfo.Capture(exception).Throw();
            string message = $"[UMA Plugins] {phase} failed for {source}: {exception}";
            if (reported.Add(message)) Debug.LogError(message);
        }
    }
}
