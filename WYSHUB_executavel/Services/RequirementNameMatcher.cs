using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemWM.Services
{
    public static class RequirementNameMatcher
    {
        public static bool AreEquivalent(string? requirementName, string? logName)
        {
            if (string.IsNullOrWhiteSpace(requirementName) || string.IsNullOrWhiteSpace(logName))
                return false;

            var left = Normalize(requirementName);
            var right = Normalize(logName);

            if (string.Equals(left, right, StringComparison.Ordinal))
                return true;

            if (left.Contains("net 8", StringComparison.Ordinal) && right.Contains("net 8", StringComparison.Ordinal))
                return true;

            if (left.Contains("visual c", StringComparison.Ordinal) && right.Contains("visual c", StringComparison.Ordinal))
                return true;

            if ((left.Contains("wmi", StringComparison.Ordinal) && right.Contains("wmi", StringComparison.Ordinal)) ||
                (left.Contains("firewall", StringComparison.Ordinal) && right.Contains("firewall", StringComparison.Ordinal)))
            {
                return true;
            }

            var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

            if (leftTokens.Count == 0 || rightTokens.Count == 0)
                return false;

            var sharedTokens = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
            return sharedTokens >= Math.Max(1, Math.Min(leftTokens.Count, rightTokens.Count) / 2);
        }

        private static string Normalize(string value)
        {
            var normalized = value.ToLowerInvariant();
            normalized = normalized.Replace("-", " ");
            normalized = normalized.Replace("(", " ").Replace(")", " ");
            normalized = normalized.Replace(".", " ");
            normalized = normalized.Replace(",", " ");
            normalized = normalized.Replace("/", " ");
            normalized = normalized.Replace("\\", " ");
            normalized = normalized.Replace("+", " ");

            var tokens = normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !StopWords.Contains(token))
                .ToArray();

            return string.Join(' ', tokens);
        }

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft",
            "windows",
            "desktop",
            "necessario",
            "necessário",
            "recomendado",
            "recomendada",
            "ativo",
            "serviço",
            "servico",
            "service",
            "self",
            "contained",
            "build",
            "nao",
            "não",
            "se",
            "o",
            "a",
            "de",
            "do",
            "da",
            "para",
            "por",
            "e",
            "ou",
            "the",
            "and",
            "x64",
            "x86",
            "runtime",
            "redistributable"
        };
    }
}
