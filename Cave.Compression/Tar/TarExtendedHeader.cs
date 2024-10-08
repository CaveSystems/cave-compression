using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cave.Compression.Tar;

sealed class TarExtendedHeader : Dictionary<string, string>
{
    #region Private Fields

    static readonly char[] EqualsSign = ['='];

    #endregion Private Fields

    #region Public Properties

    public DateTime AccessTime
    {
        get => new DateTime(1970, 1, 1) + TimeSpan.FromSeconds(double.Parse(TryGetValue("atime", out var value) ? value : "0"));
        set => this["atime"] = (value - new DateTime(1970, 1, 1)).TotalSeconds.ToString("R");
    }

    public DateTime CreationTime
    {
        get => new DateTime(1970, 1, 1) + TimeSpan.FromSeconds(double.Parse(TryGetValue("ctime", out var value) ? value : "0"));
        set => this["ctime"] = (value - new DateTime(1970, 1, 1)).TotalSeconds.ToString("R");
    }

    public DateTime ModificationTime
    {
        get => new DateTime(1970, 1, 1) + TimeSpan.FromSeconds(double.Parse(TryGetValue("mtime", out var value) ? value : "0"));
        set => this["mtime"] = (value - new DateTime(1970, 1, 1)).TotalSeconds.ToString("R");
    }

    public string Path
    {
        get => TryGetValue("path", out var value) ? value : string.Empty;
        set => this["path"] = value;
    }

    #endregion Public Properties

    #region Public Methods

    public static TarExtendedHeader Parse(string data)
    {
        var result = new TarExtendedHeader();
        var start = 0;
        for (var i = 0; i < data.Length; i++)
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
            start += itemLength;
            i = start - 1;
            if (data[i] != '\n')
            {
                throw new InvalidDataException("Extended header line needs to end with newline!");
            }

            var item = itemString.Split(EqualsSign, 2);
            result.Add(item[0], item[1]);
        }

        if (start != data.Length)
        {
            throw new InvalidDataException("Additional data after last header line!");
        }

        return result;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var i in this)
        {
            var content = $"{i.Key}={i.Value}\n";
            var result = content + 3;
            for (; ; )
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

    #endregion Public Methods
}
