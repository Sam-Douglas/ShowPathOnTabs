using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ShowPathOnTabs.Core.Captioning
{
    /// <summary>
    /// Builds tab captions. It never adds an ellipsis of its own and never changes the file name:
    /// Visual Studio trims whatever is still too long for the tab.
    /// </summary>
    public static class TabCaptionBuilder
    {
        private static readonly char[] DirectorySeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        // Has to be a string in .net Framework
        private static readonly string JoinSeparator = Path.DirectorySeparatorChar.ToString();

        // Fewest letters an abbreviated word keeps.
        private const int MinWordLength = 3;

        // A word is a run of capitals (an acronym), a capital followed by lower case, lower case, or digits.
        // Anything else (dots, dashes, underscores) is kept as it is.
        private static readonly Regex WordPattern = new Regex(@"\p{Lu}+(?!\p{Ll})|\p{Lu}?\p{Ll}+|\d+|.", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Builds a new caption for a given tab that includes the minimum required prefix for it to be unique.
        /// </summary>
        /// <param name="tabPath">Path of the tab to caption.</param>
        /// <param name="openTabPaths">Paths of every open tab, including <paramref name="tabPath"/>.</param>
        /// <param name="options">How the caption is laid out. Defaults to <see cref="CaptionOptions.Default"/>.</param>
        /// <param name="budget">
        /// The room available on a tab, used when <see cref="CaptionOptions.AbbreviateFolders"/> is
        /// <see cref="FolderAbbreviation.ToFit"/>. Without it folder names are not abbreviated to fit.
        /// </param>
        public static string Build(
            string tabPath,
            IReadOnlyList<string> openTabPaths,
            CaptionOptions options = null,
            CaptionBudget budget = null)
        {
            options = options ?? CaptionOptions.Default;
            string fileName = Path.GetFileName(tabPath);

            // Tabs with a unique file name need no path
            if (SiblingsOf(tabPath, openTabPaths).Count == 0)
                return fileName;

            var folders = FoldersFor(tabPath, openTabPaths, options.CollapseMiddle);

            bool abbreviate = options.AbbreviateFolders == FolderAbbreviation.Always
                || (options.AbbreviateFolders == FolderAbbreviation.ToFit && budget != null);
            if (abbreviate)
            {
                // The sibling tabs' folders, so abbreviating ours never makes it match one of them.
                var siblingFolders = SiblingsOf(tabPath, openTabPaths)
                    .Select(path => FoldersFor(path, openTabPaths, options.CollapseMiddle))
                    .ToList();

                // With no budget nothing ever fits, so the folders are shortened as far as they can go.
                var fitTo = options.AbbreviateFolders == FolderAbbreviation.ToFit ? budget : null;
                folders = AbbreviateToFit(folders, fileName, siblingFolders, options.Layout, fitTo);
            }

            return Format(folders, fileName, options.Layout);
        }

        private static List<string> SiblingsOf(string tabPath, IReadOnlyList<string> openTabPaths)
        {
            string fileName = Path.GetFileName(tabPath);

            return openTabPaths
                .Where(path => !path.Equals(tabPath, StringComparison.OrdinalIgnoreCase)
                         && Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Returns the folders shown on a tab: the fewest trailing folders that no sibling shares,
        /// reduced to the first of them when collapsing is on and stays unambiguous.
        /// </summary>
        private static string[] FoldersFor(string tabPath, IReadOnlyList<string> openTabPaths, bool collapseMiddle)
        {
            var folders = UniqueFolders(tabPath, openTabPaths);

            if (collapseMiddle && folders.Length > 1 && !CollapsedFormCollides(tabPath, folders[0], openTabPaths))
                return new[] { folders[0] };

            return folders;
        }

        /// <summary>
        /// Returns the shortest run of trailing folders that makes the path differ from every sibling.
        /// </summary>
        private static string[] UniqueFolders(string tabPath, IReadOnlyList<string> openTabPaths)
        {
            // Separate paths into lists of folders
            var separatedTabPath = tabPath.Split(DirectorySeparators);
            var separatedSiblingPaths = SiblingsOf(tabPath, openTabPaths).Select(path => path.Split(DirectorySeparators)).ToList();

            // Adds one parent folder at a time until the candidate is unique
            for (int depth = 0; depth < separatedTabPath.Length; depth++)
            {
                // Build a candidate for the current depth
                int skipCount = separatedTabPath.Length - (depth + 1);
                var candidateSegments = separatedTabPath.Skip(skipCount).ToArray();
                string candidate = string.Join(JoinSeparator, candidateSegments);

                // Check if the candidate is unique amongst all siblings
                bool isUnique = separatedSiblingPaths.All(siblingPath =>
                {
                    if (siblingPath.Length < depth + 1) return true;

                    int siblingSkipCount = siblingPath.Length - (depth + 1);
                    string siblingCandidate = string.Join(JoinSeparator, siblingPath.Skip(siblingSkipCount));

                    return !candidate.Equals(siblingCandidate, StringComparison.OrdinalIgnoreCase);
                });

                if (isUnique)
                    return candidateSegments.Take(candidateSegments.Length - 1).ToArray();
            }

            return separatedTabPath.Take(separatedTabPath.Length - 1).ToArray();
        }

        /// <summary>
        /// Collapsing keeps only the first of the unique folders, so it is unsafe when a sibling's unique
        /// folders start with the same one: <c>A\X\f.cs</c> and <c>A\Y\f.cs</c> would both become <c>A\f.cs</c>.
        /// </summary>
        private static bool CollapsedFormCollides(string tabPath, string firstFolder, IReadOnlyList<string> openTabPaths)
        {
            return SiblingsOf(tabPath, openTabPaths).Any(siblingPath =>
            {
                var siblingFolders = UniqueFolders(siblingPath, openTabPaths);

                return siblingFolders.Length > 0
                    && siblingFolders[0].Equals(firstFolder, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Clips the words in the folder names, longest first, until the caption fits the budget.
        /// Stops early rather than let the folders read the same as a sibling's. When nothing fits,
        /// or there is no budget, returns the shortest form so the file name gets as much of the tab
        /// as possible.
        /// </summary>
        private static string[] AbbreviateToFit(
            string[] folders,
            string fileName,
            IReadOnlyList<string[]> siblingFolders,
            CaptionLayout layout,
            CaptionBudget budget)
        {
            if (folders.Length == 0 || (budget != null && budget.Fits(Format(folders, fileName, layout))))
                return folders;

            string original = string.Join(JoinSeparator, folders);
            var otherFolders = siblingFolders
                .Where(sibling => !string.Join(JoinSeparator, sibling).Equals(original, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var best = folders;

            for (int wordLength = folders.Max(LongestWord) - 1; wordLength >= MinWordLength; wordLength--)
            {
                var candidate = Abbreviate(folders, wordLength);
                string joined = string.Join(JoinSeparator, candidate);

                bool matchesSibling = otherFolders.Any(sibling =>
                    string.Join(JoinSeparator, Abbreviate(sibling, wordLength)).Equals(joined, StringComparison.OrdinalIgnoreCase));
                if (matchesSibling)
                    break;

                best = candidate;

                if (budget != null && budget.Fits(Format(candidate, fileName, layout)))
                    break;
            }

            return best;
        }

        private static int LongestWord(string text)
        {
            return WordPattern.Matches(text).Cast<Match>().Max(match => match.Length);
        }

        private static string[] Abbreviate(string[] folders, int wordLength)
        {
            return folders.Select(folder => Abbreviate(folder, wordLength)).ToArray();
        }

        /// <summary>
        /// Trims every word to at most <paramref name="wordLength"/> letters:
        /// <c>EntityFrameworkCore</c> becomes <c>EntiFramCore</c> at four.
        /// </summary>
        private static string Abbreviate(string text, int wordLength)
        {
            var result = new StringBuilder(text.Length);

            foreach (Match word in WordPattern.Matches(text))
                result.Append(word.Length > wordLength ? word.Value.Substring(0, wordLength) : word.Value);

            return result.ToString();
        }

        private static string Format(string[] folders, string fileName, CaptionLayout layout)
        {
            if (folders.Length == 0)
                return fileName;

            string path = string.Join(JoinSeparator, folders);

            return layout == CaptionLayout.FileNameThenPath
                ? fileName + " (" + path + ")"
                : path + JoinSeparator + fileName;
        }
    }
}
