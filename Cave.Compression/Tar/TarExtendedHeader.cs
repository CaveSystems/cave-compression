using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cave.IO;

namespace Cave.Compression.Tar
{
    class TarExtendedHeader : Dictionary<string, string>
    {
        public static TarExtendedHeader Parse(string data)
        {
            var result = new TarExtendedHeader();
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            {
                // read length of entry
                if (data[i] != ' ')
                {
                    continue;
                }

                var numberLength = i - start;
                var itemLength = int.Parse(data.Substring(start, numberLength));

                // read item
                var itemString = data.Substring(i + 1, itemLength - 2 - numberLength);
                start = start + itemLength;
                i = start - 1;
                if (data[i] != '\n')
                {
                    throw new InvalidDataException("Extended header line needs to end with newline!");
                }

                var item = itemString.Split(new char[] { '=' }, 2);
                result.Add(item[0], item[1]);
            }

            if (start != data.Length)
            {
                throw new InvalidDataException("Additional data after last header line!");
            }

            return result;
        }

        public string Path
        {
            get => TryGetValue("path", out string value) ? value : null;
            set => this["path"] = value;
        }

        public DateTime ModificationTime
        {
            get => new DateTime(1970, 1, 1) + TimeSpan.FromSeconds(double.Parse(TryGetValue("mtime", out string value) ? value : "0"));
            set => this["mtime"] = (value - new DateTime(1970, 1, 1)).TotalSeconds.ToString("R");
        }

        public DateTime AccessTime
        {
            get => new DateTime(1970, 1, 1) + TimeSpan.FromSeconds(double.Parse(TryGetValue("atime", out string value) ? value : "0"));
            set => this["atime"] = (value - new DateTime(1970, 1, 1)).TotalSeconds.ToString("R");
        }

        public DateTime CreationTime
        {
            get => new DateTime(1970, 1, 1) + TimeSpan.FromSeconds(double.Parse(TryGetValue("ctime", out string value) ? value : "0"));
            set => this["ctime"] = (value - new DateTime(1970, 1, 1)).TotalSeconds.ToString("R");
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var i in this)
            {
                var content = $"{i.Key}={i.Value}\n";
                string result = content + 3;
                for (; ;)
                {
                    var prefix = result.Length;
                    result = $"{prefix} {content}";
                    if (prefix == result.Length)
                    {
                        break;
                    }
                }

                sb.Append(result);
            }

            return sb.ToString();
        }
    }
}
