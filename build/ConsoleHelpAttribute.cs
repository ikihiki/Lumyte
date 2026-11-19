using System.Reflection;
using System.Text.RegularExpressions;
using Fallout.Common;
using Fallout.Common.Execution;

namespace Lumyte.Build;

// Fallout 10.4.0 reads Console.BufferWidth even when stdout is a Windows pipe.
// Use its supported initialization hook before the built-in help hook (priority 5).
// Targets come from Fallout's actual initialized graph, not a second inventory.
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ConsoleHelpAttribute : BuildExtensionAttributeBase, IOnBuildInitialized
{
    public void OnBuildInitialized(IReadOnlyCollection<ExecutableTarget> executableTargets, IReadOnlyCollection<ExecutableTarget> executionPlan)
    {
        if (!Build.Help && executionPlan.Count != 0)
        {
            return;
        }

        Console.WriteLine("Targets (with their direct dependencies):");
        Console.WriteLine();
        foreach (ExecutableTarget target in executableTargets.Where(target => target.Listed))
        {
            string dependencies = target.ExecutionDependencies.Count == 0 ? string.Empty : " -> " + string.Join(", ", target.ExecutionDependencies.Select(dependency => dependency.Name));
            Console.WriteLine($"  {target.Name}{(target.IsDefault ? " (default)" : string.Empty)}{dependencies}");
            if (!string.IsNullOrWhiteSpace(target.Description))
            {
                Console.WriteLine("    " + target.Description);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Parameters:");
        string defaults = string.Join(", ", executableTargets.Where(target => target.IsDefault).Select(target => target.Name));
        // ParameterAttribute is the public metadata contract used by Fallout injection.
        // Walk the hierarchy explicitly so private and inherited parameters remain visible.
        for (Type? type = Build.GetType(); type is not null; type = type.BaseType)
        {
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                ParameterAttribute? parameter = member.GetCustomAttribute<ParameterAttribute>();
                if (parameter is not { List: true })
                {
                    continue;
                }

                string name = parameter.Name ?? member.Name;
                string dashed = Regex.Replace(Regex.Replace(name, "([A-Z]+)([A-Z][a-z])", "$1-$2"), "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();
                Console.WriteLine($"  --{dashed}  {parameter.Description?.Replace("{default_target}", defaults, StringComparison.Ordinal)}");
            }
        }

        // A help request is complete: never run tools or the normal build executor.
        Environment.Exit(0);
    }
}
