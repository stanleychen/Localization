﻿// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Collections.Concurrent;

namespace Microsoft.Extensions.Localization.Internal
{
    public class POManager
    {
        private readonly string _baseName;
        private readonly Assembly _assembly;
        private readonly string _resourcesRelativePath;

        public POManager(string baseName, string location, string resourcesRelativePath)
            : this(baseName, Assembly.Load(new AssemblyName(location)), resourcesRelativePath)
        {
        }

        public POManager(Type resourceSource, string resourcesRelativePath)
            : this(resourceSource.Name, resourceSource.GetTypeInfo().Assembly, resourcesRelativePath)
        {
        }

        private POManager(string baseName, Assembly assembly, string resourcesRelativePath)
        {
            _baseName = baseName;
            _assembly = assembly;
            _resourcesRelativePath = resourcesRelativePath;
        }

        public string GetString(string name)
        {
            return GetString(name, CultureInfo.CurrentUICulture);
        }

        public string GetString(string name, int plurality)
        {
            return GetString(name, plurality, CultureInfo.CurrentUICulture);
        }

        public string GetString(string name, CultureInfo culture)
        {
            var poResults = GetPOResults(culture);
            POEntry poResult;
            poResults.TryGetValue(name, out poResult);
            return string.IsNullOrEmpty(poResult?.Translation) ? name : poResult.Translation;
        }

        public string GetString(string name, int plurality, CultureInfo culture)
        {
            var poResults = GetPOResults(culture);
            POEntry poResult;
            poResults.TryGetValue(name, out poResult);

            return poResults
                .First((kvp) => kvp.Value.OriginalPlural.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Value.TranslationPlurals[plurality];
        }

        private static readonly ConcurrentDictionary<string, IDictionary<string, POEntry>> _poResultsCache =
            new ConcurrentDictionary<string, IDictionary<string, POEntry>>();

        private IDictionary<string, POEntry> GetPOResults(CultureInfo culture, bool includeParentCultures = true)
        {
            IDictionary<string, POEntry> results;

            var key = GetResourceName(culture) + includeParentCultures;
            if (!_poResultsCache.TryGetValue(key, out results))
            {
                results = new Dictionary<string, POEntry>();

                CultureInfo previousCulture = null;

                // Walk the culture tree
                while (previousCulture == null || previousCulture != culture)
                {
                    var text = GetPOText(culture);
                    if (text != null)
                    {
                        var translations = ParsePOFile(text);
                        results = MergePOEntryDictionary(translations, results);
                    }

                    previousCulture = culture;
                    if (includeParentCultures)
                    {
                        culture = culture.Parent;
                    }
                }

                _poResultsCache[key] = results;
            }

            return results;
        }

        public IDictionary<string, POEntry> GetAllStrings(bool includeParentCultures)
        {
            return GetAllStrings(includeParentCultures, CultureInfo.CurrentUICulture);
        }

        public IDictionary<string, POEntry> GetAllStrings(bool includeParentCultures, CultureInfo culture)
        {
            return GetPOResults(culture, includeParentCultures);
        }

        private IDictionary<string, POEntry> MergePOEntryDictionary(
            IDictionary<string, POEntry> newEntrys,
            IDictionary<string, POEntry> existingEntries)
        {
            foreach (var kvp in newEntrys)
            {
                if (!existingEntries.ContainsKey(kvp.Key))
                {
                    existingEntries.Add(kvp);
                }
            }

            return existingEntries;
        }

        private IDictionary<string, POEntry> ParsePOFile(Stream poStream)
        {
            var translations = new POParser(poStream).ParseLocalizationStream();

            return translations;
        }

        private Stream GetPOText(CultureInfo culture)
        {
            Stream stream = _assembly.GetManifestResourceStream(GetResourceName(culture));

            if (stream == null)
            {
                var fileName = GetFileName(culture);
                if (File.Exists(GetFileName(culture)))
                {
                    stream = File.OpenRead(GetFileName(culture));
                }
            }

            return stream;
        }

        private string GetFileName(CultureInfo culture)
        {
            string result = "";

            if (!string.IsNullOrEmpty(_resourcesRelativePath))
            {
                result = _resourcesRelativePath;
            }

            result += $"/{_baseName}";

            if (!string.IsNullOrEmpty(culture.Name))
            {
                result += $".{culture.Name}";
            }

            return result + ".po";
        }

        private string GetResourceName(CultureInfo culture)
        {
            var baseNamespace = _assembly.GetName().Name;

            var prefix = GetResourcePrefix(_baseName, baseNamespace, _resourcesRelativePath);

            if (string.IsNullOrEmpty(culture.Name))
            {
                return $"{prefix}.po";
            }
            else
            {
                return $"{prefix}.{culture.Name}.po";
            }
        }

        private string GetResourcePrefix(string resourceName, string baseNamespace, string resourcesRelativePath)
        {
            return string.IsNullOrEmpty(resourcesRelativePath)
                ? _baseName
                : baseNamespace + "." + resourcesRelativePath + "." + TrimPrefix(resourceName, baseNamespace + ".");
        }

        private static string TrimPrefix(string name, string prefix)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return name.Substring(prefix.Length);
            }

            return name;
        }
    }
}
