using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MartianMap.Routing.FixtureGen
{
    /// <summary>
    /// A small JSON writer with a stable layout: objects indented by 2, arrays of scalars on one
    /// line, LF line ends and a final newline. Numbers are rounded to 3 decimals.
    /// </summary>
    public static class JsonText
    {
        public sealed class Obj : List<KeyValuePair<string, object>>
        {
            public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int depth)
        {
            switch (value)
            {
                case Obj obj:
                    sb.Append("{\n");
                    for (int k = 0; k < obj.Count; k++)
                    {
                        Indent(sb, depth + 1);
                        sb.Append('"').Append(obj[k].Key).Append("\": ");
                        WriteValue(sb, obj[k].Value, depth + 1);
                        sb.Append(k < obj.Count - 1 ? ",\n" : "\n");
                    }

                    Indent(sb, depth);
                    sb.Append('}');
                    break;
                case object[] arr:
                    if (Array.TrueForAll(arr, IsScalar))
                    {
                        sb.Append('[');
                        for (int k = 0; k < arr.Length; k++)
                        {
                            if (k > 0)
                            {
                                sb.Append(", ");
                            }

                            WriteValue(sb, arr[k], depth);
                        }

                        sb.Append(']');
                        break;
                    }

                    sb.Append("[\n");
                    for (int k = 0; k < arr.Length; k++)
                    {
                        Indent(sb, depth + 1);
                        WriteValue(sb, arr[k], depth + 1);
                        sb.Append(k < arr.Length - 1 ? ",\n" : "\n");
                    }

                    Indent(sb, depth);
                    sb.Append(']');
                    break;
                case string s:
                    sb.Append('"').Append(s).Append('"');
                    break;
                case bool flag:
                    sb.Append(flag ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d))
                    {
                        throw new ArgumentException("JSON carries no NaN or Infinity.");
                    }

                    double r = Math.Round(d, 3, MidpointRounding.AwayFromZero);
                    sb.Append((r == 0 ? 0.0 : r).ToString("R", CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"Unsupported JSON value: {value?.GetType().Name ?? "null"}.");
            }
        }

        private static bool IsScalar(object v) => !(v is Obj) && !(v is object[]);

        private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', 2 * depth);
    }
}
