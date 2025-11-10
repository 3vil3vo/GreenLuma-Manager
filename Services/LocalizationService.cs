using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace GreenLuma_Manager.Services
{
	public static class LocalizationService
	{
		private static readonly Dictionary<string, string> LanguageToPack = new(StringComparer.OrdinalIgnoreCase)
		{
			["en"] = "Resources/Strings/StringResources.en.xaml",
			["zh-Hans"] = "Resources/Strings/StringResources.zh-Hans.xaml",
			["zh-Hant"] = "Resources/Strings/StringResources.zh-Hant.xaml",
			["ja"] = "Resources/Strings/StringResources.ja.xaml"
		};

		public static string GetDefaultLanguage()
		{
			try
			{
				var name = CultureInfo.CurrentUICulture.Name; // e.g. zh-CN, zh-TW, ja-JP, en-US
				if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
				{
					// map zh-CN/zh-SG -> zh-Hans; zh-TW/zh-HK -> zh-Hant; fallback zh-Hans
					if (name.EndsWith("TW", StringComparison.OrdinalIgnoreCase) ||
					    name.EndsWith("HK", StringComparison.OrdinalIgnoreCase) ||
					    name.EndsWith("MO", StringComparison.OrdinalIgnoreCase))
						return "zh-Hant";
					return "zh-Hans";
				}
				if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
			}
			catch
			{
			}
			return "en";
		}

		public static void ApplyLanguage(string? languageCode)
		{
			string code = NormalizeLanguage(languageCode);
			if (!LanguageToPack.TryGetValue(code, out var path))
			{
				path = LanguageToPack["en"];
			}

			var dict = new ResourceDictionary
			{
				Source = new Uri($"pack://application:,,,/{path}", UriKind.Absolute)
			};

			// Remove previous string dictionaries
			var app = Application.Current;
			if (app == null) return;

			var existing = app.Resources.MergedDictionaries
				.Where(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Strings/", StringComparison.OrdinalIgnoreCase))
				.ToList();
			foreach (var e in existing)
			{
				app.Resources.MergedDictionaries.Remove(e);
			}

			app.Resources.MergedDictionaries.Add(dict);
		}

		private static string NormalizeLanguage(string? code)
		{
			if (string.IsNullOrWhiteSpace(code)) return GetDefaultLanguage();
			code = code.Trim();
			// accept aliases
			if (string.Equals(code, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(code, "zh-SG", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase))
				return "zh-Hans";
			if (string.Equals(code, "zh-TW", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(code, "zh-HK", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(code, "zh-MO", StringComparison.OrdinalIgnoreCase))
				return "zh-Hant";
			if (string.Equals(code, "ja-JP", StringComparison.OrdinalIgnoreCase))
				return "ja";
			if (string.Equals(code, "en-US", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(code, "en-GB", StringComparison.OrdinalIgnoreCase))
				return "en";
			return LanguageToPack.ContainsKey(code) ? code : "en";
		}
	}
}

