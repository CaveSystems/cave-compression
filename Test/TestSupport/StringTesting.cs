using System.Collections.Generic;

namespace Cave.Compression.Tests.TestSupport
{
	public static class StringTesting
	{
		static StringTesting()
		{
			AddLanguage("Chinese", "測試.txt", "big5");
			AddLanguage("Greek", "Ϗΰ.txt", "windows-1253");
			AddLanguage("Nordic", "Åæ.txt", "windows-1252");
			AddLanguage("Arabic", "ڀڅ.txt", "windows-1256");
			AddLanguage("Russian", "Прйвёт.txt", "windows-1251");
		}

		static void AddLanguage(string language, string filename, string encoding)
		{
			languages.Add(language);
			filenames.Add(filename);
			encodings.Add(encoding);
			entries++;
		}

		static int entries = 0;
		static List<string> languages = new List<string>();
		static List<string> filenames = new List<string>();
		static List<string> encodings = new List<string>();

		public static IEnumerable<string> Languages => filenames.AsReadOnly();
		public static IEnumerable<string> Filenames => filenames.AsReadOnly();
		public static IEnumerable<string> Encodings => filenames.AsReadOnly();

		public static IEnumerable<(string language, string filename, string encoding)> GetTestSamples()
		{
			for (int i = 0; i < entries; i++)
			{
				yield return (languages[i], filenames[i], encodings[i]);
			}
		}
	}
}
